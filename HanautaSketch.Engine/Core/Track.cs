namespace HanautaSketch.Engine.Core;

/// <summary>
/// トラックの基底。通常トラックとスマートトラックがこれを継承する。
/// </summary>
public abstract class Track
{
    public string Name { get; set; } = "";

    /// <summary>GM音色番号（0〜127）。ドラムトラックではドラムキットの番号（0 = Standard）</summary>
    public int Program { get; set; }

    /// <summary>トラック音量（0〜127）</summary>
    public int Volume { get; set; } = 100;

    /// <summary>再生時に鳴らさない。書き出しや判別には影響しない</summary>
    public bool Mute { get; set; }

    /// <summary>ソロのトラックが1つでもあれば、ソロ以外は再生時に鳴らさない</summary>
    public bool Solo { get; set; }

    /// <summary>GM ドラム（10ch）で鳴らすトラックか</summary>
    public virtual bool IsDrum => false;

    /// <summary>
    /// 再生・書き出しに使うノーツ。通常トラックは実体、スマートトラックは生成結果。
    /// </summary>
    public abstract IReadOnlyList<Note> PlaybackNotes { get; }
}

/// <summary>
/// ピアノロールで直接編集できるノーツを持つトラック。
/// </summary>
public interface INoteEditable
{
    /// <summary>編集対象のノーツ（その場で書き換える）</summary>
    List<Note> EditableNotes { get; }

    /// <summary>この位置に新しいノートを置けるか</summary>
    bool CanPlaceAt(int tick);
}

/// <summary>
/// ノーツの実体を持つ通常トラック。
/// </summary>
public sealed class NoteTrack : Track, INoteEditable
{
    public List<Note> Notes { get; } = [];

    public override IReadOnlyList<Note> PlaybackNotes => Notes;

    List<Note> INoteEditable.EditableNotes => Notes;

    public bool CanPlaceAt(int tick) => true;
}
