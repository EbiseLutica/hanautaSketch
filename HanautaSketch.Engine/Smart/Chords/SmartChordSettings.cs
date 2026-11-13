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
/// スマートコードの判別設定。
/// </summary>
public sealed record SmartChordSettings
{
    /// <summary>自動判別の区間グリッド（tick）。0 以下なら1小節</summary>
    public int SegmentTicks { get; init; }

    public MinorDominantMode MinorDominant { get; init; } = MinorDominantMode.Minor;

    /// <summary>メロディの重み（ベースを 1 としたとき）</summary>
    public double MelodyWeight { get; init; } = 0.4;
}
