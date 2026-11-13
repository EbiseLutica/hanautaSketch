using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Rhythm;

/// <summary>
/// ベースラインのリズムを読み取る。コードの区切り判定と、スマートドラムの FollowBass で共通に使う。
/// </summary>
public static class BassRhythmAnalyzer
{
    private const double BarHeadStrength = 2.0;
    private const double HalfBarStrength = 1.2;
    private const double BeatStrength = 0.25;
    private const double SustainPenalty = -0.5;
    private const double SyncopationStrength = 3.75;

    /// <summary>
    /// 指定位置での「区切れやすさ」。拍節の強さ・ベースの発音の強さ・シンコペーションを足し、
    /// 持続音をまたぐ位置は減点する。
    /// </summary>
    public static double BoundaryStrength(IReadOnlyList<Note> bass, int tick, int ppq, int ticksPerBar)
    {
        var onset = OnsetStrength(bass, tick, ppq);
        var strength = MetricStrength(tick, ppq, ticksPerBar) + onset;
        if (onset == 0 && bass.Any(n => n.Tick < tick && tick < n.EndTick))
            strength += SustainPenalty;
        if (IsSyncopated(bass, tick, ppq))
            strength += SyncopationStrength;
        return strength;
    }

    /// <summary>
    /// 拍節上の強さ。小節頭 &gt; 半小節 &gt; 拍 &gt; それ以外。
    /// </summary>
    public static double MetricStrength(int tick, int ppq, int ticksPerBar)
    {
        if (tick % ticksPerBar == 0) return BarHeadStrength;
        if (ticksPerBar % 2 == 0 && tick % (ticksPerBar / 2) == 0) return HalfBarStrength;
        if (tick % ppq == 0) return BeatStrength;
        return 0;
    }

    /// <summary>
    /// 指定位置で発音するベース音の強さ（0〜1）。発音がなければ 0、長い音ほど強い。
    /// </summary>
    public static double OnsetStrength(IReadOnlyList<Note> bass, int tick, int ppq)
    {
        var strength = 0.0;
        foreach (var note in bass)
        {
            if (note.Tick != tick) continue;
            strength = Math.Max(strength, 0.5 + 0.5 * Math.Min(1.0, (double)note.Length / ppq));
        }
        return strength;
    }

    /// <summary>
    /// 拍の外で発音し、次の拍まで新しい発音がない（次の拍を先取りしている）か。
    /// トレシーロ（3+3+2）や小節末の食いのような「キメ」を拾うための判定。
    /// 後に続く発音がない（フレーズの末尾）場合は先取りとみなさない。
    /// </summary>
    public static bool IsSyncopated(IReadOnlyList<Note> bass, int tick, int ppq)
    {
        if (tick % ppq == 0) return false;
        if (!bass.Any(n => n.Tick == tick)) return false;

        var nextBeat = (tick / ppq + 1) * ppq;
        return !bass.Any(n => n.Tick > tick && n.Tick <= nextBeat) && bass.Any(n => n.Tick > nextBeat);
    }

    /// <summary>
    /// ベースの発音位置を重複なく昇順で返す。
    /// </summary>
    public static IReadOnlyList<int> GetOnsets(IReadOnlyList<Note> bass) =>
        bass.Select(n => n.Tick).Distinct().Order().ToList();
}
