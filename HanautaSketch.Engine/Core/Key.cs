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

    /// <summary>
    /// 指定ピッチがスケール内の音か判定する。
    /// </summary>
    public bool Contains(int pitch) => GetScalePitchClasses().Contains(((pitch % 12) + 12) % 12);

    public override string ToString() => $"{PitchNames[Tonic]} {Mode}";
}
