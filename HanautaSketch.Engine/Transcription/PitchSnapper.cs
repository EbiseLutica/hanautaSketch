using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Transcription;

/// <summary>
/// 歌った音高を半音に丸めるときの、スケールの扱い。
/// </summary>
public enum PitchSnapMode
{
    /// <summary>スケールを見ず、いちばん近い半音にする</summary>
    Chromatic,

    /// <summary>半音の境界付近で迷ったときだけスケールの音に倒す。はっきり歌った臨時記号は残す</summary>
    Lean,

    /// <summary>必ずいちばん近いスケールの音にする</summary>
    Scale,
}

/// <summary>
/// 歌った音高（小数の MIDI ノート番号）を MIDI ノート番号に丸める。
/// </summary>
public static class PitchSnapper
{
    /// <summary>
    /// <see cref="PitchSnapMode.Lean"/> で、最寄りの半音からこれ以上離れていればスケール側に倒す（半音）。
    /// 0.25 なら、境界の ±0.25 半音の範囲がスケール寄りになる
    /// </summary>
    public const double LeanThreshold = 0.25;

    public static int Snap(double midi, Key key, PitchSnapMode mode)
    {
        var nearest = (int)Math.Round(midi, MidpointRounding.AwayFromZero);
        switch (mode)
        {
            case PitchSnapMode.Chromatic:
                return Clamp(nearest);
            case PitchSnapMode.Lean:
                if (key.Contains(nearest)) return Clamp(nearest);
                var offset = midi - nearest;
                var other = nearest + (offset >= 0 ? 1 : -1);
                return Clamp(Math.Abs(offset) >= LeanThreshold && key.Contains(other) ? other : nearest);
            case PitchSnapMode.Scale:
                var below = nearest;
                while (!key.Contains(below)) below--;
                var above = nearest;
                while (!key.Contains(above)) above++;
                return Clamp(midi - below <= above - midi ? below : above);
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    /// <summary>チューニングの推定で、1音の重みにする長さの上限（秒）。長く伸ばした1音のずれに引っ張られないようにする</summary>
    private const double MaxTuningWeightSeconds = 0.5;

    /// <summary>推定したずれの大きさがこれを超えたら、半音反対側のずれとも比べる</summary>
    private const double AmbiguousTuning = 0.35;

    /// <summary>
    /// 歌い手全体のチューニングのずれ（半音、-0.5〜0.5）を、各音の音高の端数から長さで重み付けして推定する。
    /// 端数は円周上の値なので、角度にして平均する。
    /// </summary>
    public static double EstimateTuningOffset(IReadOnlyList<SungNote> notes)
    {
        double x = 0, y = 0;
        foreach (var note in notes)
        {
            var angle = 2 * Math.PI * (note.Midi - Math.Round(note.Midi));
            var weight = Math.Min(note.Duration, MaxTuningWeightSeconds);
            x += weight * Math.Cos(angle);
            y += weight * Math.Sin(angle);
        }
        return x == 0 && y == 0 ? 0 : Math.Atan2(y, x) / (2 * Math.PI);
    }

    /// <summary>
    /// チューニングのずれを推定する。ずれが半音の半分近くあると +0.5 と -0.5 の区別がつかないので、
    /// そのときは半音反対側のずれと比べ、補正後にスケールの音へ多く収まる方を選ぶ。
    /// </summary>
    /// <param name="notes">歌った音</param>
    /// <param name="keyOf">各音（インデックス）の位置のキー</param>
    public static double ResolveTuningOffset(IReadOnlyList<SungNote> notes, Func<int, Key> keyOf)
    {
        var tuning = EstimateTuningOffset(notes);
        if (Math.Abs(tuning) <= AmbiguousTuning) return tuning;

        var alternative = tuning - Math.Sign(tuning);
        double Fit(double offset) => notes
            .Select((n, i) => keyOf(i).Contains((int)Math.Round(n.Midi - offset)) ? Math.Min(n.Duration, MaxTuningWeightSeconds) : 0)
            .Sum();
        return Fit(alternative) > Fit(tuning) ? alternative : tuning;
    }

    private static int Clamp(int pitch) => Math.Clamp(pitch, 0, 127);
}
