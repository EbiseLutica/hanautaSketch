using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// 和音の種類。自動判別は三和音（Major / Minor / Diminished / Augmented）だけを使い、
/// それ以外は override と候補のランキングで使う。構成音と表記は <see cref="ChordQualities"/> の表で持つ。
/// 保存形式に名前で書き出すので、既存の値の名前は変えない。
/// </summary>
public enum ChordQuality
{
    Major,
    Minor,
    Diminished,
    Augmented,
    Sus2,
    Sus4,
    Sixth,
    MinorSixth,
    Dominant7,
    Major7,
    Minor7,
    HalfDiminished7,
    Diminished7,
    MinorMajor7,
    Augmented7,
    Dominant7Sus4,
    Add9,
    MinorAdd9,
    SixNine,
    Dominant9,
    Major9,
    Minor9,
    Dominant7Flat9,
    Dominant7Sharp9,
    Dominant7Sharp11,
    Dominant7Flat13,
    Dominant11,
    Minor11,
    Major7Sharp11,
    Dominant13,
}

/// <summary>和音の種類ごとの、根音からの音程（半音。9th 以上は 12 以上で持つ）と表記</summary>
public sealed record ChordQualityInfo(ChordQuality Quality, IReadOnlyList<int> Intervals, string Suffix, string Group);

/// <summary>
/// 和音の種類の表。種類を増やすときは enum とここに1行足す。
/// </summary>
public static class ChordQualities
{
    public static IReadOnlyList<ChordQualityInfo> All { get; } =
    [
        new(ChordQuality.Major, [0, 4, 7], "", "三和音"),
        new(ChordQuality.Minor, [0, 3, 7], "m", "三和音"),
        new(ChordQuality.Diminished, [0, 3, 6], "dim", "三和音"),
        new(ChordQuality.Augmented, [0, 4, 8], "aug", "三和音"),
        new(ChordQuality.Sus2, [0, 2, 7], "sus2", "sus"),
        new(ChordQuality.Sus4, [0, 5, 7], "sus4", "sus"),
        new(ChordQuality.Dominant7Sus4, [0, 5, 7, 10], "7sus4", "sus"),
        new(ChordQuality.Sixth, [0, 4, 7, 9], "6", "6th・add9"),
        new(ChordQuality.MinorSixth, [0, 3, 7, 9], "m6", "6th・add9"),
        new(ChordQuality.Add9, [0, 4, 7, 14], "add9", "6th・add9"),
        new(ChordQuality.MinorAdd9, [0, 3, 7, 14], "madd9", "6th・add9"),
        new(ChordQuality.SixNine, [0, 4, 7, 9, 14], "6(9)", "6th・add9"),
        new(ChordQuality.Dominant7, [0, 4, 7, 10], "7", "7th"),
        new(ChordQuality.Major7, [0, 4, 7, 11], "maj7", "7th"),
        new(ChordQuality.Minor7, [0, 3, 7, 10], "m7", "7th"),
        new(ChordQuality.HalfDiminished7, [0, 3, 6, 10], "m7♭5", "7th"),
        new(ChordQuality.Diminished7, [0, 3, 6, 9], "dim7", "7th"),
        new(ChordQuality.MinorMajor7, [0, 3, 7, 11], "mM7", "7th"),
        new(ChordQuality.Augmented7, [0, 4, 8, 10], "aug7", "7th"),
        new(ChordQuality.Dominant9, [0, 4, 7, 10, 14], "9", "テンション"),
        new(ChordQuality.Major9, [0, 4, 7, 11, 14], "maj9", "テンション"),
        new(ChordQuality.Minor9, [0, 3, 7, 10, 14], "m9", "テンション"),
        new(ChordQuality.Dominant7Flat9, [0, 4, 7, 10, 13], "7(♭9)", "テンション"),
        new(ChordQuality.Dominant7Sharp9, [0, 4, 7, 10, 15], "7(♯9)", "テンション"),
        new(ChordQuality.Dominant7Sharp11, [0, 4, 7, 10, 18], "7(♯11)", "テンション"),
        new(ChordQuality.Dominant7Flat13, [0, 4, 7, 10, 20], "7(♭13)", "テンション"),
        new(ChordQuality.Dominant11, [0, 7, 10, 14, 17], "11", "テンション"),
        new(ChordQuality.Minor11, [0, 3, 7, 10, 14, 17], "m11", "テンション"),
        new(ChordQuality.Major7Sharp11, [0, 4, 7, 11, 18], "maj7(♯11)", "テンション"),
        new(ChordQuality.Dominant13, [0, 4, 7, 10, 14, 21], "13", "テンション"),
    ];

    private static readonly Dictionary<ChordQuality, ChordQualityInfo> ByQuality = All.ToDictionary(q => q.Quality);

    public static ChordQualityInfo Get(ChordQuality quality) => ByQuality[quality];

    /// <summary>三和音（自動判別で使う種類）か</summary>
    public static bool IsTriad(ChordQuality quality) => quality is ChordQuality.Major or ChordQuality.Minor
        or ChordQuality.Diminished or ChordQuality.Augmented;
}

/// <summary>
/// コード1つ分。
/// </summary>
/// <param name="Root">根音のピッチクラス（C=0〜B=11）</param>
/// <param name="Quality">和音の種類</param>
/// <param name="Bass">転回形・分数コードのベース音のピッチクラス。根音と同じなら null</param>
public readonly record struct Chord(int Root, ChordQuality Quality, int? Bass = null)
{
    /// <summary>
    /// 構成音のピッチクラスを、根音・3度・5度・7度・テンションの順（表の音程の順）に返す。
    /// </summary>
    public IReadOnlyList<int> GetPitchClasses()
    {
        var root = Root;
        return ChordQualities.Get(Quality).Intervals.Select(i => (root + i) % 12).Distinct().ToList();
    }

    /// <summary>
    /// 伴奏で鳴らす構成音。5音以上のコードは、響きが濁らないよう完全5度を省く。
    /// </summary>
    public IReadOnlyList<int> GetVoicingPitchClasses()
    {
        var root = Root;
        var intervals = ChordQualities.Get(Quality).Intervals;
        if (intervals.Count >= 5) intervals = intervals.Where(i => i != 7).ToList();
        return intervals.Select(i => (root + i) % 12).Distinct().ToList();
    }

    /// <summary>転回指定を外した基本形</summary>
    public Chord RootPosition => this with { Bass = null };

    public override string ToString() => GetName(new Key(0, Mode.Major));

    /// <summary>
    /// キーの調号に合わせたコード名を返す（C minor なら "Ab"、C major なら "G#"）。
    /// </summary>
    public string GetName(Key key)
    {
        var name = key.GetPitchName(Root) + ChordQualities.Get(Quality).Suffix;
        return Bass is { } bass && bass != Root ? $"{name}/{key.GetPitchName(bass)}" : name;
    }
}
