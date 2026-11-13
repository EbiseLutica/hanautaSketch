namespace HanautaSketch.Engine.Core;

/// <summary>
/// MIDIノート1つ分。時間はすべてtick単位。
/// </summary>
/// <param name="Tick">発音開始位置</param>
/// <param name="Length">長さ</param>
/// <param name="Pitch">MIDIノート番号（0〜127）</param>
/// <param name="Velocity">ベロシティ（1〜127）</param>
public readonly record struct Note(int Tick, int Length, int Pitch, int Velocity)
{
    /// <summary>発音終了位置（排他）</summary>
    public int EndTick => Tick + Length;

    /// <summary>ピッチクラス（C=0〜B=11）</summary>
    public int PitchClass => Pitch % 12;
}
