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
    public IReadOnlyDictionary<int, string> GetPresetNames()
    {
        lock (_lock)
        {
            return _synth.SoundFont.Presets
                .Where(p => p.BankNumber == 0 && p.PatchNumber is >= 0 and < 128)
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
                return _startTick + SecondsToTicks(_song, (double)_position / SampleRate);
            }
        }
    }

    /// <summary>
    /// 曲を startTick から再生する。呼び出し時点のノーツをスナップショットとして使う。
    /// </summary>
    public void Play(Song song, int startTick = 0)
    {
        var events = BuildEvents(song, startTick);
        lock (_lock)
        {
            _synth.Reset();
            UpdateAudible(song);
            _song = song;
            _startTick = startTick;
            _events = events;
            _nextEvent = 0;
            _position = 0;
            _endPosition = (events.Count > 0 ? events[^1].Sample : 0) + (long)(ReleaseTailSeconds * SampleRate);
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
                if (!track.IsDrum) _synth.ProcessMidiMessage(channel, 0xC0, track.Program, 0);
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
            if (!isDrum) _synth.ProcessMidiMessage(channel, 0xC0, program, 0);
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
            if (e.Command == 0x90 && !_audible[e.Channel]) continue;
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

    private readonly record struct ScheduledEvent(long Sample, int Channel, int Command, int Data1, int Data2, int Order);

    /// <summary>
    /// 曲のノーツをサンプル位置つきの MIDI メッセージ列に変換する。
    /// 同じ位置ではノートオフ → 設定 → ノートオン の順に並べる。
    /// </summary>
    private List<ScheduledEvent> BuildEvents(Song song, int startTick)
    {
        var events = new List<ScheduledEvent>();
        foreach (var (track, channel) in AssignChannels(song))
        {
            if (!track.IsDrum) events.Add(new ScheduledEvent(0, channel, 0xC0, track.Program, 0, 1));
            events.Add(new ScheduledEvent(0, channel, 0xB0, 7, track.Volume, 1));

            foreach (var note in track.PlaybackNotes)
            {
                if (note.EndTick <= startTick) continue;

                var on = Math.Max(note.Tick, startTick);
                events.Add(new ScheduledEvent(TickToSample(song, on - startTick), channel, 0x90, note.Pitch,
                    note.Velocity, 2));
                events.Add(new ScheduledEvent(TickToSample(song, note.EndTick - startTick), channel, 0x80,
                    note.Pitch, 0, 0));
            }
        }

        return events.OrderBy(e => e.Sample).ThenBy(e => e.Order).ToList();
    }

    private long TickToSample(Song song, int tick) => (long)(tick / (double)song.Ppq * 60.0 / song.Bpm * SampleRate);

    private static double SecondsToTicks(Song song, double seconds) => seconds * song.Bpm / 60.0 * song.Ppq;

    #endregion
}
