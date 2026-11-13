using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// 和音の種類。当面はトライアドのみ。テンション等の表現は保留中。
/// </summary>
public enum ChordQuality
{
    Major,
    Minor,
    Diminished,
    Augmented,
}

/// <summary>
/// コード1つ分。
/// </summary>
/// <param name="Root">根音のピッチクラス（C=0〜B=11）</param>
/// <param name="Quality">和音の種類</param>
/// <param name="Bass">転回形のベース音のピッチクラス。根音と同じなら null</param>
public readonly record struct Chord(int Root, ChordQuality Quality, int? Bass = null)
{
    /// <summary>
    /// 構成音のピッチクラスを根音・3度・5度の順に返す。
    /// </summary>
    public IReadOnlyList<int> GetPitchClasses()
    {
        var (third, fifth) = Quality switch
        {
            ChordQuality.Major => (4, 7),
            ChordQuality.Minor => (3, 7),
            ChordQuality.Diminished => (3, 6),
            ChordQuality.Augmented => (4, 8),
            _ => throw new ArgumentOutOfRangeException(),
        };
        return [Root, (Root + third) % 12, (Root + fifth) % 12];
    }

    /// <summary>転回指定を外した基本形</summary>
    public Chord RootPosition => this with { Bass = null };

    public override string ToString() => GetName(new Key(0, Mode.Major));

    /// <summary>
    /// キーの調号に合わせたコード名を返す（C minor なら "Ab"、C major なら "G#"）。
    /// </summary>
    public string GetName(Key key)
    {
        var suffix = Quality switch
        {
            ChordQuality.Major => "",
            ChordQuality.Minor => "m",
            ChordQuality.Diminished => "dim",
            ChordQuality.Augmented => "aug",
            _ => "?",
        };
        var name = key.GetPitchName(Root) + suffix;
        return Bass is { } bass && bass != Root ? $"{name}/{key.GetPitchName(bass)}" : name;
    }
}
