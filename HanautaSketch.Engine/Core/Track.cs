namespace HanautaSketch.Engine.Core;

/// <summary>
/// トラックの基底。通常トラックとスマートトラックがこれを継承する。
/// </summary>
public abstract class Track
{
    public string Name { get; set; } = "";

    /// <summary>GM音色番号（0〜127）</summary>
    public int Program { get; set; }

    /// <summary>トラック音量（0〜127）</summary>
    public int Volume { get; set; } = 100;
}

/// <summary>
/// ノーツの実体を持つ通常トラック。
/// </summary>
public sealed class NoteTrack : Track
{
    public List<Note> Notes { get; } = [];
}
