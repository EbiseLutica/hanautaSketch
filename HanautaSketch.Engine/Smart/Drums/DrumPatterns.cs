using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Drums;

/// <summary>
/// ビートパターンの種類。
/// </summary>
public enum DrumPattern
{
    FourOnTheFloor,
    EightBeat,
    SixteenBeat,
}

/// <summary>
/// ドラムをベースのリズムにどれだけ合わせるか。
/// </summary>
public enum DrumBassFollow
{
    /// <summary>パターンのまま</summary>
    None,

    /// <summary>フィル小節（区間の最後の小節と、4小節ごとの区切りの直前の小節）の後半だけ、ベースの発音位置でフィルを打つ</summary>
    Fill,

    /// <summary>キックをパターンではなくベースの発音位置に置く</summary>
    Full,
}

/// <summary>
/// GM ドラムの音（ノート番号）。
/// </summary>
public static class GmDrum
{
    public const int Kick = 36;
    public const int Snare = 38;
    public const int ClosedHiHat = 42;
    public const int OpenHiHat = 46;
    public const int Crash = 49;
    public const int HighTom = 50;
    public const int HighMidTom = 48;
    public const int LowMidTom = 47;
    public const int LowTom = 45;
    public const int HighFloorTom = 43;
}

/// <summary>
/// 1小節（4/4、16分音符16ステップ）のパターン。各楽器の打つステップとベロシティ。
/// </summary>
internal sealed record DrumTemplate(IReadOnlyList<(int Pitch, int Step, int Velocity)> Hits)
{
    public const int StepsPerBar = 16;

    /// <summary>1拍あたりのステップ数（16分音符）</summary>
    public const int StepsPerBeat = 4;

    public static DrumTemplate For(DrumPattern pattern) => pattern switch
    {
        DrumPattern.FourOnTheFloor => FourOnTheFloor,
        DrumPattern.EightBeat => EightBeat,
        DrumPattern.SixteenBeat => SixteenBeat,
        _ => EightBeat,
    };

    /// <summary>4つ打ち: キックが毎拍、スネアが2・4拍、オープンハイハットが裏拍</summary>
    private static readonly DrumTemplate FourOnTheFloor = new(
    [
        .. Steps(GmDrum.Kick, 100, 0, 4, 8, 12),
        .. Steps(GmDrum.Snare, 100, 4, 12),
        .. Steps(GmDrum.OpenHiHat, 80, 2, 6, 10, 14),
    ]);

    /// <summary>8ビート: キックが1・3拍、スネアが2・4拍、ハイハットが8分（表拍を強く）</summary>
    private static readonly DrumTemplate EightBeat = new(
    [
        .. Steps(GmDrum.Kick, 100, 0, 8),
        .. Steps(GmDrum.Snare, 100, 4, 12),
        .. Steps(GmDrum.ClosedHiHat, 90, 0, 4, 8, 12),
        .. Steps(GmDrum.ClosedHiHat, 70, 2, 6, 10, 14),
    ]);

    /// <summary>16ビート: キックが1拍・2拍裏・3拍、スネアが2・4拍、ハイハットが16分（拍頭を強く）</summary>
    private static readonly DrumTemplate SixteenBeat = new(
    [
        .. Steps(GmDrum.Kick, 100, 0, 6, 8),
        .. Steps(GmDrum.Snare, 100, 4, 12),
        .. Steps(GmDrum.ClosedHiHat, 90, 0, 4, 8, 12),
        .. Steps(GmDrum.ClosedHiHat, 70, 2, 6, 10, 14),
        .. Steps(GmDrum.ClosedHiHat, 55, 1, 3, 5, 7, 9, 11, 13, 15),
    ]);

    private static IEnumerable<(int, int, int)> Steps(int pitch, int velocity, params int[] steps) =>
        steps.Select(s => (pitch, s, velocity));
}
