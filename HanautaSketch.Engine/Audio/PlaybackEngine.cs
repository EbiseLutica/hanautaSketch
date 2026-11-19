using HanautaSketch.Engine.Core;
using MeltySynth;

namespace HanautaSketch.Engine.Audio;

/// <summary>
/// MeltySynth を使って曲を再生する。出力デバイスには依存せず、
/// 呼び出し側（オーディオスレッド）が <see cref="Render"/> でサンプルを引き出す。
/// </summary>
public sealed class PlaybackEngine
{
    /// <summary>最後のノートオフ後、余韻のために鳴らし続ける秒数</summary>
    private const double ReleaseTailSeconds = 1.5;

    /// <summary>入力プレビュー専用のチャンネル。曲のトラックには割り当てない</summary>
    private const int PreviewChannel = 15;

    /// <summary>SF2 でドラムキットが入っているバンク</summary>
    private const int DrumBank = 128;

    /// <summary>クリックの長さ（秒）</summary>
    private const double ClickSeconds = 0.05;

    /// <summary>止めずに鳴らし続けるとき（<see cref="PlaybackOptions.Continuous"/>）に、クリックを用意しておく長さ（秒）</summary>
    private const double ContinuousClickSeconds = 30 * 60;

    private readonly Synthesizer _synth;
    private readonly object _lock = new();

    /// <summary>チャンネルごとに鳴らすか（ミュート・ソロの反映）</summary>
    private readonly bool[] _audible = Enumerable.Repeat(true, 16).ToArray();

    private List<ScheduledEvent> _events = [];

    /// <summary>鳴らしているプレビュー音（チャンネル、音高）</summary>
    private readonly List<(int Channel, int Pitch)> _previewing = [];
    private int _nextEvent;
    private long _position;
    private long _endPosition;
    private Song? _song;
    private int _startTick;

    /// <summary>カウントインの長さ（サンプル）。再生位置はこの分だけ負から始まる</summary>
    private long _preRollSamples;

    /// <summary>カウントインのテンポ（開始位置のテンポ）</summary>
    private double _preRollBpm;

    /// <param name="soundFontPath">SF2 ファイルのパス</param>
    /// <param name="sampleRate">出力サンプルレート</param>
    public PlaybackEngine(string soundFontPath, int sampleRate)
    {
        SampleRate = sampleRate;
        _synth = new Synthesizer(soundFontPath, sampleRate);
    }

    public int SampleRate { get; }

    /// <summary>
    /// SF2 に入っているバンク0のプリセット名（キーは音色番号 0〜127）。
    /// </summary>
    public IReadOnlyDictionary<int, string> GetPresetNames() => GetPresetNames(0);

    /// <summary>
    /// SF2 に入っているドラムキット（バンク128）の名前（キーはキット番号 0〜127）。
    /// </summary>
    public IReadOnlyDictionary<int, string> GetDrumKitNames() => GetPresetNames(DrumBank);

    private IReadOnlyDictionary<int, string> GetPresetNames(int bank)
    {
        lock (_lock)
        {
            return _synth.SoundFont.Presets
                .Where(p => p.BankNumber == bank && p.PatchNumber is >= 0 and < 128)
                .GroupBy(p => p.PatchNumber)
                .ToDictionary(g => g.Key, g => g.First().Name.Trim());
        }
    }

    public bool IsPlaying { get; private set; }

    /// <summary>現在の再生位置（tick）。停止中は最後に再生した位置</summary>
    public double PositionTick
    {
        get
        {
            lock (_lock)
            {
                if (_song is null) return 0;
                var conductor = _song.Conductor;
                var seconds = (double)_position / SampleRate;
                // カウントイン中は開始位置のテンポで、開始位置より前の tick にする
                if (_position < 0) return _startTick + seconds * _preRollBpm / 60 * conductor.Ppq;
                return conductor.SecondsToTick(conductor.TickToSeconds(_startTick) + seconds);
            }
        }
    }

    /// <summary>
    /// 最後に再生を始めたときの、鳴らし始めの曲上の時刻（秒）。カウントインがあればその分だけ開始位置より前（負にもなる）。
    /// </summary>
    public double PlaybackStartSeconds
    {
        get
        {
            lock (_lock)
            {
                if (_song is null) return 0;
                return _song.Conductor.TickToSeconds(_startTick) - (double)_preRollSamples / SampleRate;
            }
        }
    }

    /// <summary>最後に再生を始めてから書き出した長さ（秒）。カウントインを含む</summary>
    public double ElapsedSeconds
    {
        get
        {
            lock (_lock)
            {
                return (double)(_position + _preRollSamples) / SampleRate;
            }
        }
    }

    /// <summary>
    /// 曲を startTick から再生する。呼び出し時点のノーツをスナップショットとして使う。
    /// </summary>
    public void Play(Song song, int startTick = 0, PlaybackOptions? options = null)
    {
        options ??= new PlaybackOptions();
        var events = BuildEvents(song, startTick, options, SampleRate);
        var preRoll = events.Count > 0 ? Math.Max(0, -events[0].Sample) : 0;
        lock (_lock)
        {
            _synth.Reset();
            UpdateAudible(song);
            _song = song;
            _startTick = startTick;
            _preRollSamples = preRoll;
            _preRollBpm = song.Conductor.BpmAt(startTick);
            _events = events;
            _nextEvent = 0;
            _position = -preRoll;
            _endPosition = options.Continuous
                ? long.MaxValue
                : (events.Count > 0 ? events[^1].Sample : 0) + (long)(ReleaseTailSeconds * SampleRate);
            IsPlaying = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            IsPlaying = false;
            _synth.NoteOffAll(false);
        }
    }

    /// <summary>
    /// トラックの音色・音量・ミュート・ソロを、再生中のチャンネルにすぐ反映する。
    /// 鳴らさなくなったチャンネルは鳴っている音を止める。
    /// </summary>
    public void ApplyMixer(Song song)
    {
        lock (_lock)
        {
            foreach (var (track, channel) in AssignChannels(song))
            {
                _synth.ProcessMidiMessage(channel, 0xC0, track.Program, 0);
                _synth.ProcessMidiMessage(channel, 0xB0, 7, track.Volume);
            }

            var before = (bool[])_audible.Clone();
            UpdateAudible(song);
            for (var channel = 0; channel < _audible.Length; channel++)
            {
                if (before[channel] && !_audible[channel]) _synth.NoteOffAll(channel, false);
            }
        }
    }

    /// <summary>
    /// 入力中の確認用に音（和音も可）を鳴らす。前のプレビュー音は止める。
    /// 通常はプレビュー専用のチャンネル、ドラムは 10ch で鳴らす（10ch は再生中の音を止めないよう、プレビューした音だけ止める）。
    /// </summary>
    public void PreviewNotesOn(int program, IEnumerable<(int Pitch, int Velocity)> notes, bool isDrum = false)
    {
        lock (_lock)
        {
            StopPreviewNotes();
            var channel = isDrum ? MidiChannels.Drum : PreviewChannel;
            _synth.ProcessMidiMessage(channel, 0xC0, program, 0);
            foreach (var (pitch, velocity) in notes)
            {
                _synth.NoteOn(channel, pitch, velocity);
                _previewing.Add((channel, pitch));
            }
        }
    }

    /// <summary>
    /// プレビュー音を止める。
    /// </summary>
    public void PreviewNoteOff()
    {
        lock (_lock)
        {
            StopPreviewNotes();
        }
    }

    private void StopPreviewNotes()
    {
        foreach (var (channel, pitch) in _previewing)
            _synth.NoteOff(channel, pitch);
        _previewing.Clear();
    }

    /// <summary>
    /// オーディオスレッドから呼ばれ、left / right にサンプルを書き込む。
    /// イベントはサンプル単位の位置で発行する。
    /// </summary>
    public void Render(Span<float> left, Span<float> right)
    {
        lock (_lock)
        {
            var offset = 0;
            var remaining = left.Length;
            while (remaining > 0)
            {
                var chunk = remaining;
                if (IsPlaying)
                {
                    DispatchDueEvents();
                    if (_nextEvent < _events.Count)
                        chunk = (int)Math.Clamp(_events[_nextEvent].Sample - _position, 1, remaining);
                }

                _synth.Render(left.Slice(offset, chunk), right.Slice(offset, chunk));
                offset += chunk;
                remaining -= chunk;

                if (!IsPlaying) continue;
                _position += chunk;
                if (_position >= _endPosition) IsPlaying = false;
            }
        }
    }

    private void DispatchDueEvents()
    {
        while (_nextEvent < _events.Count && _events[_nextEvent].Sample <= _position)
        {
            var e = _events[_nextEvent++];
            if (e.Command == 0x90 && !e.Force && !_audible[e.Channel]) continue;
            _synth.ProcessMidiMessage(e.Channel, e.Command, e.Data1, e.Data2);
        }
    }

    /// <summary>
    /// チャンネルごとに鳴らすかを決める。複数のトラックが同じチャンネル（ドラムの 10ch など）を使うときは、
    /// どれか1つでも鳴らす状態なら鳴らす。
    /// </summary>
    private void UpdateAudible(Song song)
    {
        Array.Fill(_audible, true);
        foreach (var group in AssignChannels(song).GroupBy(a => a.Channel))
            _audible[group.Key] = group.Any(a => song.IsAudible(a.Track));
    }

    /// <summary>
    /// トラックにチャンネルを割り当てる。ドラムは 10ch、それ以外はプレビュー用（15）を除いてトラック順。
    /// </summary>
    private static IEnumerable<(Track Track, int Channel)> AssignChannels(Song song) =>
        MidiChannels.Assign(song.Tracks, PreviewChannel);

    #region イベント生成

    /// <param name="Force">ミュート・ソロに関係なく鳴らす（クリック）</param>
    internal readonly record struct ScheduledEvent(long Sample, int Channel, int Command, int Data1, int Data2, int Order, bool Force = false);

    /// <summary>
    /// 曲のノーツとクリックを、サンプル位置（開始位置が 0。カウントインは負）つきの MIDI メッセージ列に変換する。
    /// 同じ位置ではノートオフ → 設定 → ノートオン の順に並べる。
    /// </summary>
    internal static List<ScheduledEvent> BuildEvents(Song song, int startTick, PlaybackOptions options, int sampleRate)
    {
        var events = new List<ScheduledEvent>();
        var setup = new List<ScheduledEvent>();
        var conductor = song.Conductor;
        var startSeconds = conductor.TickToSeconds(startTick);
        long SecondsToSample(double seconds) => (long)Math.Round(seconds * sampleRate);
        long TickToSample(int tick) => SecondsToSample(conductor.TickToSeconds(tick) - startSeconds);

        foreach (var (track, channel) in AssignChannels(song))
        {
            if (ReferenceEquals(track, options.ExcludedTrack)) continue;
            // 10ch の Program Change はドラムキットの切り替え（SF2 のバンク128）
            setup.Add(new ScheduledEvent(0, channel, 0xC0, track.Program, 0, 1));
            setup.Add(new ScheduledEvent(0, channel, 0xB0, 7, track.Volume, 1));

            if (!options.PlaySong) continue;
            foreach (var note in track.PlaybackNotes)
            {
                if (note.EndTick <= startTick) continue;

                var on = Math.Max(note.Tick, startTick);
                events.Add(new ScheduledEvent(TickToSample(on), channel, 0x90, note.Pitch,
                    note.Velocity, 2));
                events.Add(new ScheduledEvent(TickToSample(note.EndTick), channel, 0x80,
                    note.Pitch, 0, 0));
            }
        }

        // クリックは 10ch（ドラム）で、曲の終わり（止めずに鳴らすときは十分先）まで刻む
        var songEnd = events.Count > 0 ? events.Max(e => e.Sample) : 0;
        var clickCount = events.Count;
        var clickEndTick = options.Continuous
            ? (int)Math.Ceiling(conductor.SecondsToTick(startSeconds + ContinuousClickSeconds))
            : (int)Math.Ceiling(conductor.SecondsToTick(startSeconds + (double)songEnd / sampleRate));
        foreach (var click in MetronomeClicks.Build(conductor, startTick, options, clickEndTick))
        {
            var pitch = click.IsAccent ? MetronomeClicks.AccentPitch : MetronomeClicks.BeatPitch;
            var on = SecondsToSample(click.Seconds);
            events.Add(new ScheduledEvent(on, MidiChannels.Drum, 0x90, pitch, click.IsAccent ? 110 : 80, 2, Force: true));
            events.Add(new ScheduledEvent(on + SecondsToSample(ClickSeconds), MidiChannels.Drum, 0x80, pitch, 0, 0, Force: true));
        }

        // 音色・音量の設定は、カウントインも含めた鳴らし始めに送る（クリックにも曲のドラムキットを使う）
        var first = events.Skip(clickCount).Select(e => e.Sample).DefaultIfEmpty(0).Min();
        events.AddRange(setup.Select(e => e with { Sample = Math.Min(0, first) }));
        return events.OrderBy(e => e.Sample).ThenBy(e => e.Order).ToList();
    }


    #endregion
}
