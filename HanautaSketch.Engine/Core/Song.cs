namespace HanautaSketch.Engine.Core;

/// <summary>
/// 曲全体。試作段階のためテンポ・拍子・キーは曲全体で固定。後でConductorに置き換える。
/// </summary>
public sealed class Song
{
    public const int DefaultPpq = 480;

    /// <summary>4分音符あたりのtick数</summary>
    public int Ppq { get; init; } = DefaultPpq;

    public double Bpm { get; set; } = 120;

    public int BeatsPerBar { get; set; } = 4;

    public Key Key { get; set; } = new(0, Mode.Major);

    public List<Track> Tracks { get; } = [];

    /// <summary>1小節あたりのtick数（4分音符基準の拍子を前提）</summary>
    public int TicksPerBar => Ppq * BeatsPerBar;
}
