using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;

namespace HanautaSketch.Engine.Transcription;

/// <summary>
/// 鼻歌の採譜の設定。
/// </summary>
public sealed record TranscriptionOptions
{
    /// <summary>量子化のグリッド（tick）。既定は16分音符。拍の長さ（PPQ）を割り切れる値にする</summary>
    public int GridTicks { get; init; } = Song.DefaultPpq / 4;

    /// <summary>スケールの扱い</summary>
    public PitchSnapMode SnapMode { get; init; } = PitchSnapMode.Lean;

    /// <summary>
    /// 録音の遅れ（秒）。歌声が録音データに入るまでの遅れと、クリックや伴奏が耳に届くまでの遅れの合計。
    /// この分だけ音を前にずらす
    /// </summary>
    public double LatencySeconds { get; init; }

    /// <summary>付けるベロシティ</summary>
    public int Velocity { get; init; } = 100;

    /// <summary>これより短い音は捨てる（秒）</summary>
    public double MinNoteSeconds { get; init; } = NoteSegmenter.DefaultMinDuration;
}

/// <summary>
/// 録音した鼻歌をノーツに起こす。曲のテンポに合わせて歌ってもらう前提で、テンポの推定はしない。
/// </summary>
public static class HummingTranscriber
{
    /// <summary>
    /// 録音を採譜し、範囲内に収まるノーツを返す（単音。重なりはない）。
    /// </summary>
    /// <param name="clip">録音</param>
    /// <param name="conductor">テンポ・キー</param>
    /// <param name="options">設定</param>
    /// <param name="clipStartSeconds">録音の先頭が曲のどの時刻（秒）にあたるか。カウントインの分だけ負になりうる</param>
    /// <param name="rangeStartTick">書き込む範囲の先頭</param>
    /// <param name="rangeEndTick">書き込む範囲の終わり（排他）</param>
    public static IReadOnlyList<Note> Transcribe(
        AudioClip clip, Conductor conductor, TranscriptionOptions options,
        double clipStartSeconds, int rangeStartTick, int rangeEndTick)
    {
        var frames = PitchTracker.Track(clip.Samples, clip.SampleRate);
        var sung = NoteSegmenter.Segment(frames, options.MinNoteSeconds);
        return Quantize(sung, conductor, options, clipStartSeconds, rangeStartTick, rangeEndTick);
    }

    /// <summary>
    /// 切り出した音をチューニング補正・スケール寄せ・グリッド吸着してノーツにする。
    /// </summary>
    internal static IReadOnlyList<Note> Quantize(
        IReadOnlyList<SungNote> sung, Conductor conductor, TranscriptionOptions options,
        double clipStartSeconds, int rangeStartTick, int rangeEndTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.GridTicks);
        var grid = options.GridTicks;
        var tuning = PitchSnapper.EstimateTuningOffset(sung);

        int ToGrid(double seconds)
        {
            var tick = conductor.SecondsToTick(clipStartSeconds + seconds - options.LatencySeconds);
            return (int)Math.Round(tick / grid, MidpointRounding.AwayFromZero) * grid;
        }

        var candidates = sung
            .Select(n =>
            {
                var start = ToGrid(n.Start);
                var end = Math.Max(ToGrid(n.End), start + grid);
                var pitch = PitchSnapper.Snap(n.Midi - tuning, conductor.KeyAt(start), options.SnapMode);
                return (Start: start, End: end, Pitch: pitch, n.Duration);
            })
            .Where(n => n.Start >= rangeStartTick && n.Start < rangeEndTick)
            // 同じ位置に吸着した音は、長く歌った方を残す
            .GroupBy(n => n.Start)
            .Select(g => g.MaxBy(n => n.Duration))
            .OrderBy(n => n.Start)
            .ToList();

        var notes = new List<Note>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            var (start, end, pitch, _) = candidates[i];
            if (i + 1 < candidates.Count) end = Math.Min(end, candidates[i + 1].Start);
            end = Math.Min(end, rangeEndTick);
            notes.Add(new Note(start, end - start, pitch, Math.Clamp(options.Velocity, 1, 127)));
        }
        return notes;
    }

    /// <summary>
    /// 録音した範囲の既存のノーツを、採譜したノーツで置き換える（パンチイン）。
    /// 範囲にかかるノーツは、範囲の外の部分だけ残す。
    /// </summary>
    /// <param name="notes">トラックのノーツ。その場で書き換える</param>
    /// <param name="rangeStartTick">範囲の先頭</param>
    /// <param name="rangeEndTick">範囲の終わり（排他）</param>
    /// <param name="recorded">採譜したノーツ</param>
    public static void ReplaceRange(List<Note> notes, int rangeStartTick, int rangeEndTick, IEnumerable<Note> recorded)
    {
        var result = new List<Note>();
        foreach (var note in notes)
        {
            if (note.EndTick <= rangeStartTick || rangeEndTick <= note.Tick)
            {
                result.Add(note);
                continue;
            }
            if (note.Tick < rangeStartTick) result.Add(note with { Length = rangeStartTick - note.Tick });
            if (rangeEndTick < note.EndTick) result.Add(note with { Tick = rangeEndTick, Length = note.EndTick - rangeEndTick });
        }
        result.AddRange(recorded);
        notes.Clear();
        notes.AddRange(result.OrderBy(n => n.Tick).ThenBy(n => n.Pitch));
    }
}
