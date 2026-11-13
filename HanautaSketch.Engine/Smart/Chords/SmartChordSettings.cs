namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// マイナーキーの5度の扱い。
/// </summary>
public enum MinorDominantMode
{
    /// <summary>自然短音階どおり v（Am なら Em）</summary>
    Minor,

    /// <summary>和声的短音階の V（Am なら E）</summary>
    Major,

    /// <summary>v と V を両方候補にし、メロディの導音などからスコアで決める</summary>
    Auto,
}

/// <summary>
/// スマートコードの判別設定。tick 指定の項目は 0 以下なら既定値を使う。
/// </summary>
public sealed record SmartChordSettings
{
    public MinorDominantMode MinorDominant { get; init; } = MinorDominantMode.Minor;

    /// <summary>メロディの重み（ベースを 1 としたとき）</summary>
    public double MelodyWeight { get; init; } = 0.4;

    /// <summary>コードの区切り候補の間隔（既定: 1拍）</summary>
    public int BoundaryUnitTicks { get; init; }

    /// <summary>コード区間の最小長（既定: 2拍）</summary>
    public int MinSegmentTicks { get; init; }

    /// <summary>コード区間の最大長（既定: 2小節）。同じコードが続く場合は結果上で連結される</summary>
    public int MaxSegmentTicks { get; init; }
}
