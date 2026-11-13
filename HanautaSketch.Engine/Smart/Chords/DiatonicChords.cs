using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// キーに対する和音機能。
/// </summary>
internal enum HarmonicFunction
{
    Tonic,
    Subdominant,
    Dominant,
}

/// <summary>
/// 判別候補となるコード。
/// </summary>
/// <param name="Chord">コード</param>
/// <param name="Degree">音階度数（0=I）</param>
/// <param name="Prior">候補自体の事前スコア（借用和音のペナルティなど）</param>
/// <param name="IsBorrowedDominant">マイナーキーで和声的短音階から借用した V か</param>
internal readonly record struct ChordCandidate(Chord Chord, int Degree, double Prior, bool IsBorrowedDominant = false);

/// <summary>
/// キーからダイアトニックコードの候補と、和音機能を求める。
/// </summary>
internal static class DiatonicChords
{
    /// <summary>Auto 時に V へ課すペナルティ。メロディに導音がなければ v が勝つ程度</summary>
    private const double BorrowedDominantPenalty = -0.6;

    private static readonly HarmonicFunction[] MajorFunctions =
    [
        HarmonicFunction.Tonic, HarmonicFunction.Subdominant, HarmonicFunction.Tonic, HarmonicFunction.Subdominant,
        HarmonicFunction.Dominant, HarmonicFunction.Tonic, HarmonicFunction.Dominant,
    ];

    private static readonly HarmonicFunction[] MinorFunctions =
    [
        HarmonicFunction.Tonic, HarmonicFunction.Subdominant, HarmonicFunction.Tonic, HarmonicFunction.Subdominant,
        HarmonicFunction.Dominant, HarmonicFunction.Subdominant, HarmonicFunction.Dominant,
    ];

    /// <summary>
    /// 判別候補を返す。マイナーキーでは設定に応じて v / V を差し替え・追加する。
    /// </summary>
    public static IReadOnlyList<ChordCandidate> GetCandidates(Key key, MinorDominantMode minorDominant)
    {
        var scale = key.GetScalePitchClasses();
        var result = new List<ChordCandidate>();
        for (var degree = 0; degree < 7; degree++)
        {
            var chord = Triad(scale, degree);
            var isMinorDominant = key.Mode == Mode.Minor && degree == 4;
            if (!isMinorDominant)
            {
                result.Add(new ChordCandidate(chord, degree, 0));
                continue;
            }

            var major = chord with { Quality = ChordQuality.Major };
            switch (minorDominant)
            {
                case MinorDominantMode.Minor:
                    result.Add(new ChordCandidate(chord, degree, 0));
                    break;
                case MinorDominantMode.Major:
                    result.Add(new ChordCandidate(major, degree, 0, true));
                    break;
                case MinorDominantMode.Auto:
                    result.Add(new ChordCandidate(chord, degree, 0));
                    result.Add(new ChordCandidate(major, degree, BorrowedDominantPenalty, true));
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// コードの根音からキー上の度数を求める。スケール外なら null。
    /// </summary>
    public static int? GetDegree(Key key, Chord chord)
    {
        var scale = key.GetScalePitchClasses();
        for (var i = 0; i < scale.Count; i++)
        {
            if (scale[i] == chord.Root) return i;
        }
        return null;
    }

    public static HarmonicFunction GetFunction(Key key, int degree) =>
        (key.Mode == Mode.Major ? MajorFunctions : MinorFunctions)[degree];

    private static Chord Triad(IReadOnlyList<int> scale, int degree)
    {
        var root = scale[degree];
        var third = (scale[(degree + 2) % 7] - root + 12) % 12;
        var fifth = (scale[(degree + 4) % 7] - root + 12) % 12;
        var quality = (third, fifth) switch
        {
            (4, 7) => ChordQuality.Major,
            (3, 7) => ChordQuality.Minor,
            (3, 6) => ChordQuality.Diminished,
            (4, 8) => ChordQuality.Augmented,
            _ => throw new InvalidOperationException($"ダイアトニックトライアドとして不正な音程: {third}, {fifth}"),
        };
        return new Chord(root, quality);
    }
}
