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

    /// <summary>
    /// 採譜したノーツ全体をオクターブ単位でずらし、<see cref="OctaveRangeLow"/>〜<see cref="OctaveRangeHigh"/> に収めるか。
    /// 低い声・高い声で歌っても、メロディらしい高さで起こす
    /// </summary>
    public bool FitOctave { get; init; } = true;

    /// <summary>オクターブを合わせる範囲の下端（MIDI ノート番号。既定は C4）</summary>
    public int OctaveRangeLow { get; init; } = 60;

    /// <summary>オクターブを合わせる範囲の上端（MIDI ノート番号。既定は C6）</summary>
    public int OctaveRangeHigh { get; init; } = 84;
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
        var grid = options.GridTicks;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(grid);
        if (conductor.Ppq % grid != 0) throw new ArgumentException($"Grid ({grid}) must divide PPQ ({conductor.Ppq}).", nameof(options));
        if (rangeEndTick <= rangeStartTick) throw new ArgumentException("Range end must be after range start.", nameof(rangeEndTick));

        int ToGrid(double seconds)
        {
            var tick = conductor.SecondsToTick(clipStartSeconds + seconds - options.LatencySeconds);
            return (int)Math.Round(tick / grid, MidpointRounding.AwayFromZero) * grid;
        }

        var placed = sung
            .Select(n =>
            {
                var start = ToGrid(n.Start);
                return (Start: start, End: Math.Max(ToGrid(n.End), start + grid), n.Midi, n.Duration);
            })
            .ToList();
        var tuning = PitchSnapper.ResolveTuningOffset(sung, i => conductor.KeyAt(placed[i].Start));

        var candidates = placed
            // 範囲の先頭より少し早く歌い出した音は、範囲に十分かかっていれば先頭に寄せて残す
            .Select(n => n.Start < rangeStartTick && n.End - rangeStartTick >= grid ? n with { Start = rangeStartTick } : n)
            .Where(n => n.Start >= rangeStartTick && n.Start < rangeEndTick)
            // 同じ位置に吸着した音は、長く歌った方を残す
            .GroupBy(n => n.Start)
            .Select(g => g.MaxBy(n => n.Duration))
            .OrderBy(n => n.Start)
            .ToList();

        var notes = new List<Note>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            var (start, end, midi, _) = candidates[i];
            if (i + 1 < candidates.Count) end = Math.Min(end, candidates[i + 1].Start);
            // 範囲の終わりで切れて、グリッドの半分に満たなくなった音は捨てる
            if (rangeEndTick < end)
            {
                end = rangeEndTick;
                if (end - start < grid / 2) continue;
            }
            var pitch = PitchSnapper.Snap(midi - tuning, conductor.KeyAt(start), options.SnapMode);
            notes.Add(new Note(start, end - start, pitch, Math.Clamp(options.Velocity, 1, 127)));
        }
        return options.FitOctave ? FitOctave(notes, options.OctaveRangeLow, options.OctaveRangeHigh) : notes;
    }

    /// <summary>
    /// ノーツ全体を同じだけオクターブ単位でずらし、範囲 [low, high] からはみ出す長さが最も短くなるようにする。
    /// 同じなら動かす量の少ない方を選ぶ。音程の関係は保つ。
    /// </summary>
    internal static List<Note> FitOctave(List<Note> notes, int low, int high)
    {
        if (notes.Count == 0) return notes;

        long Outside(int shift) => notes
            .Where(n => n.Pitch + shift < low || n.Pitch + shift > high)
            .Sum(n => (long)n.Length);

        var best = Enumerable.Range(-4, 9)
            .Select(octaves => octaves * 12)
            .Where(shift => notes.All(n => n.Pitch + shift is >= 0 and <= 127))
            .OrderBy(Outside)
            .ThenBy(Math.Abs)
            .DefaultIfEmpty(0)
            .First();
        return best == 0 ? notes : notes.Select(n => n with { Pitch = n.Pitch + best }).ToList();
    }

    /// <summary>
    /// 録音した範囲の既存のノーツを、採譜したノーツで置き換える（パンチイン）。
    /// 範囲にかかるノーツは、範囲の外の部分だけ残す。範囲外のノーツの並びは保ち、採譜したノーツは末尾に足す。
    /// </summary>
    /// <param name="notes">トラックのノーツ。その場で書き換える</param>
    /// <param name="rangeStartTick">範囲の先頭</param>
    /// <param name="rangeEndTick">範囲の終わり（排他）</param>
    /// <param name="recorded">採譜したノーツ</param>
    public static void ReplaceRange(List<Note> notes, int rangeStartTick, int rangeEndTick, IEnumerable<Note> recorded)
    {
        if (rangeEndTick < rangeStartTick) throw new ArgumentException("Range end must not be before range start.", nameof(rangeEndTick));
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
        notes.AddRange(result);
    }
}
