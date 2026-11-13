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

    private const int DrumChannel = 9;

    /// <summary>入力プレビュー専用のチャンネル。曲のトラックには割り当てない</summary>
    private const int PreviewChannel = 15;

    private readonly Synthesizer _synth;
    private readonly object _lock = new();

    private List<ScheduledEvent> _events = [];
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
    /// 入力中の確認用に1音鳴らす。前のプレビュー音は止める。再生とは別のチャンネルを使う。
    /// </summary>
    public void PreviewNoteOn(int program, int pitch, int velocity)
    {
        lock (_lock)
        {
            _synth.NoteOffAll(PreviewChannel, false);
            _synth.ProcessMidiMessage(PreviewChannel, 0xC0, program, 0);
            _synth.NoteOn(PreviewChannel, pitch, velocity);
        }
    }

    /// <summary>
    /// プレビュー音を止める。
    /// </summary>
    public void PreviewNoteOff()
    {
        lock (_lock)
        {
            _synth.NoteOffAll(PreviewChannel, false);
        }
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
            _synth.ProcessMidiMessage(e.Channel, e.Command, e.Data1, e.Data2);
        }
    }

    #region イベント生成

    private readonly record struct ScheduledEvent(long Sample, int Channel, int Command, int Data1, int Data2, int Order);

    /// <summary>
    /// 曲のノーツをサンプル位置つきの MIDI メッセージ列に変換する。
    /// 同じ位置ではノートオフ → 設定 → ノートオン の順に並べる。
    /// </summary>
    private List<ScheduledEvent> BuildEvents(Song song, int startTick)
    {
        var events = new List<ScheduledEvent>();
        var channel = 0;
        foreach (var track in song.Tracks.OfType<NoteTrack>())
        {
            if (channel == DrumChannel) channel++;
            if (channel >= PreviewChannel) break;

            events.Add(new ScheduledEvent(0, channel, 0xC0, track.Program, 0, 1));
            events.Add(new ScheduledEvent(0, channel, 0xB0, 7, track.Volume, 1));

            foreach (var note in track.Notes)
            {
                if (note.EndTick <= startTick) continue;

                var on = Math.Max(note.Tick, startTick);
                events.Add(new ScheduledEvent(TickToSample(song, on - startTick), channel, 0x90, note.Pitch,
                    note.Velocity, 2));
                events.Add(new ScheduledEvent(TickToSample(song, note.EndTick - startTick), channel, 0x80,
                    note.Pitch, 0, 0));
            }

            channel++;
        }

        return events.OrderBy(e => e.Sample).ThenBy(e => e.Order).ToList();
    }

    private long TickToSample(Song song, int tick) => (long)(tick / (double)song.Ppq * 60.0 / song.Bpm * SampleRate);

    private static double SecondsToTicks(Song song, double seconds) => seconds * song.Bpm / 60.0 * song.Ppq;

    #endregion
}
