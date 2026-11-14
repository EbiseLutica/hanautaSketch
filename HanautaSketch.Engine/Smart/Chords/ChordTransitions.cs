using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// 判別（Viterbi）で使うコード間の遷移スコア。和音機能から決める。
/// override の候補の並びは、実際の曲から集計した <see cref="ChordTransitionTable"/> を使う。
/// </summary>
public static class ChordTransitions
{
    /// <summary>
    /// from から to へ移るときのスコアを返す。from が null なら曲頭。
    /// </summary>
    public static double Score(Key key, Chord? from, Chord to)
    {
        var toDegree = DiatonicChords.GetDegree(key, to);
        if (from is not { } prev)
            return toDegree == 0 ? 0.3 : 0;

        if (prev.RootPosition == to.RootPosition)
            return 0.3;

        var fromDegree = DiatonicChords.GetDegree(key, prev);
        if (fromDegree is not { } fd || toDegree is not { } td)
            return 0;

        var score = (DiatonicChords.GetFunction(key, fd), DiatonicChords.GetFunction(key, td)) switch
        {
            (HarmonicFunction.Tonic, HarmonicFunction.Subdominant) => 0.5,
            (HarmonicFunction.Subdominant, HarmonicFunction.Dominant) => 0.6,
            (HarmonicFunction.Dominant, HarmonicFunction.Tonic) => 0.8,
            (HarmonicFunction.Tonic, HarmonicFunction.Dominant) => 0.3,
            (HarmonicFunction.Subdominant, HarmonicFunction.Tonic) => 0.2,
            (HarmonicFunction.Dominant, HarmonicFunction.Subdominant) => -0.3,
            _ => 0.1,
        };

        // V→I の解決は特に強い
        if (fd == 4 && td == 0) score += 0.3;
        return score;
    }
}
