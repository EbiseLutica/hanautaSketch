namespace HanautaSketch.Engine.Core;

/// <summary>
/// 調の旋法。当面はメジャー／マイナーのみ。
/// </summary>
public enum Mode
{
    Major,
    Minor,
}

/// <summary>
/// 調（主音＋旋法）。スケール構成音の取得は必ずこの型のAPI経由で行う。
/// </summary>
/// <param name="Tonic">主音のピッチクラス（C=0〜B=11）</param>
/// <param name="Mode">旋法</param>
public readonly record struct Key(int Tonic, Mode Mode)
{
    private static readonly int[] MajorIntervals = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] MinorIntervals = [0, 2, 3, 5, 7, 8, 10];

    /// <summary>ピッチクラス名（C=0〜B=11）</summary>
    public static IReadOnlyList<string> PitchNames { get; } = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>
    /// スケール構成音のピッチクラスを主音から順に返す。
    /// </summary>
    public IReadOnlyList<int> GetScalePitchClasses()
    {
        var intervals = Mode == Mode.Major ? MajorIntervals : MinorIntervals;
        var tonic = Tonic;
        return intervals.Select(i => (tonic + i) % 12).ToArray();
    }

    private static readonly string[] FlatPitchNames = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];

    /// <summary>フラット系の調号を持つメジャーキーの主音（F, B♭, E♭, A♭, D♭）</summary>
    private static readonly int[] FlatMajorTonics = [5, 10, 3, 8, 1];

    /// <summary>調号がフラット系か</summary>
    public bool PrefersFlats
    {
        get
        {
            var relativeMajor = Mode == Mode.Major ? Tonic : (Tonic + 3) % 12;
            return FlatMajorTonics.Contains(relativeMajor);
        }
    }

    /// <summary>
    /// 調号に合わせた音名を返す（C minor なら A♭ は "Ab"）。
    /// </summary>
    public string GetPitchName(int pitchClass)
    {
        var pc = ((pitchClass % 12) + 12) % 12;
        return PrefersFlats ? FlatPitchNames[pc] : PitchNames[pc];
    }

    /// <summary>
    /// 指定ピッチがスケール内の音か判定する。
    /// </summary>
    public bool Contains(int pitch) => GetScalePitchClasses().Contains(((pitch % 12) + 12) % 12);

    public override string ToString() => $"{PitchNames[Tonic]} {Mode}";
}
