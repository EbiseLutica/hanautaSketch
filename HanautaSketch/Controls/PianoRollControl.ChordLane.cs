using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Drums;

namespace HanautaSketch.Controls;

/// <summary>コードレーンのドラッグの種類</summary>
public enum ChordLaneDragKind
{
    /// <summary>空いた範囲（判別区間の上でも可）をドラッグして固定区間を作る</summary>
    Create,

    /// <summary>固定区間の内側をドラッグして開始位置を動かす</summary>
    Move,

    /// <summary>固定区間の左端を動かす</summary>
    ResizeStart,

    /// <summary>固定区間の右端を動かす</summary>
    ResizeEnd,
}

/// <summary>
/// コードレーンのドラッグ依頼。Create は [Start, End)、Move は新しい開始位置（Start）、
/// ResizeStart / ResizeEnd は動かした端の位置（それぞれ Start / End）を使う。
/// Segment は押した位置の判別区間（Create では null のことがある）。
/// </summary>
public sealed class ChordLaneDragRequestedEventArgs(ChordLaneDragKind kind, ChordSegment? segment, int start, int end) : EventArgs
{
    public ChordLaneDragKind Kind { get; } = kind;

    public ChordSegment? Segment { get; } = segment;

    public int Start { get; } = start;

    public int End { get; } = end;
}

/// <summary>
/// コードレーンの入力。固定区間の端・内側のドラッグで伸縮・移動、区間のドラッグで新規作成を依頼する。
/// ドラッグせずに離したときは、従来どおりコードの変更メニューを出す。
/// </summary>
public sealed partial class PianoRollControl
{
    /// <summary>この距離（px）以上動かしたら、クリックではなくドラッグとみなす</summary>
    private const double ChordDragThreshold = 4;

    /// <summary>固定区間の端とみなす幅（px）</summary>
    private const double ChordEdgeWidth = 5;

    private double _chordDragStartX;
    private double _chordDragCurrentX;

    /// <summary>押した位置の判別区間（クリックならメニューの対象）</summary>
    private ChordSegment? _chordTarget;

    private ChordLaneDragKind _chordKind;

    /// <summary>移動のとき、押した位置と区間の開始位置の差（tick）</summary>
    private int _chordGrabOffset;

    /// <summary>コードレーンの区間をドラッグで変えたときの依頼</summary>
    public event EventHandler<ChordLaneDragRequestedEventArgs>? ChordLaneDragRequested;

    private bool IsChordDrag => Math.Abs(_chordDragCurrentX - _chordDragStartX) >= ChordDragThreshold;

    /// <summary>スナップの刻み（0 以下にならないように）</summary>
    private int ChordSnap => Math.Max(1, SnapTicks);

    #region 入力

    private void BeginChordLaneInput(PointerPressedEventArgs e, Point pos)
    {
        _chordDragStartX = _chordDragCurrentX = pos.X;
        var tick = (int)Math.Max(0, XToTick(pos.X));

        if (HitChordOverrideEdge(pos) is { } edge)
        {
            _chordTarget = edge.Segment;
            _chordKind = edge.IsEnd ? ChordLaneDragKind.ResizeEnd : ChordLaneDragKind.ResizeStart;
        }
        else
        {
            var block = GetChordBlocks().FirstOrDefault(b => b.Start <= tick && tick < b.End);
            _chordTarget = block;
            _chordKind = block is { IsOverride: true } ? ChordLaneDragKind.Move : ChordLaneDragKind.Create;
            _chordGrabOffset = tick - (block?.Start ?? tick);
        }
        BeginDrag(DragMode.ChordLane, e);
    }

    private void UpdateChordLaneDrag(Point pos)
    {
        _chordDragCurrentX = pos.X;
        InvalidateVisual();
    }

    /// <summary>
    /// ドラッグならその範囲の依頼を出す。クリックなら押した位置の区間のメニューを出す。
    /// </summary>
    private void CompleteChordLaneInput()
    {
        var target = _chordTarget;
        var kind = _chordKind;
        _chordTarget = null;

        if (!IsChordDrag)
        {
            if (target is not null) ChordSegmentClicked?.Invoke(this, new ChordSegmentClickedEventArgs(target));
            return;
        }

        if (ChordLaneDragRange(kind, target) is not { } range) return;
        ChordLaneDragRequested?.Invoke(this, new ChordLaneDragRequestedEventArgs(kind, target, range.Start, range.End));
    }

    /// <summary>
    /// 固定区間の端にいるか。判別区間のうち override のものだけを対象にする。
    /// </summary>
    private (ChordSegment Segment, bool IsEnd)? HitChordOverrideEdge(Point pos)
    {
        if (Track is SmartDrumTrack || pos.X < KeyboardWidth || pos.Y < ChordLaneTop || pos.Y >= HeaderHeight) return null;

        foreach (var block in GetChordBlocks())
        {
            if (!block.IsOverride) continue;
            if (Math.Abs(TickToX(block.Start) - pos.X) <= ChordEdgeWidth) return (block, false);
            if (Math.Abs(TickToX(block.End) - pos.X) <= ChordEdgeWidth) return (block, true);
        }
        return null;
    }

    private bool IsOverChordOverrideEdge(Point pos) => HitChordOverrideEdge(pos) is not null;

    /// <summary>
    /// ドラッグ中の範囲。スナップした tick で決める。
    /// </summary>
    private (int Start, int End)? ChordLaneDragRange(ChordLaneDragKind kind, ChordSegment? target)
    {
        var current = XToTick(_chordDragCurrentX);
        switch (kind)
        {
            case ChordLaneDragKind.Create:
            {
                var from = XToTick(_chordDragStartX);
                var start = Math.Max(0, SnapFloor(Math.Min(from, current)));
                var end = Math.Max(0, SnapRound(Math.Max(from, current)));
                if (end <= start) end = start + ChordSnap;
                return (start, end);
            }
            case ChordLaneDragKind.Move when target is not null:
            {
                var start = Math.Max(0, SnapRound(current - _chordGrabOffset));
                return (start, start + target.Length);
            }
            case ChordLaneDragKind.ResizeStart when target is not null:
            {
                var start = Math.Clamp(SnapRound(current), 0, target.End - ChordSnap);
                return start == target.Start ? null : (start, target.End);
            }
            case ChordLaneDragKind.ResizeEnd when target is not null:
            {
                var end = Math.Max(SnapRound(current), target.Start + ChordSnap);
                return end == target.End ? null : (target.Start, end);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// ドラッグ中の範囲をコードレーン上に薄く描く。
    /// </summary>
    private void RenderChordLaneDragPreview(DrawingContext context)
    {
        if (_dragMode != DragMode.ChordLane || !IsChordDrag) return;
        if (ChordLaneDragRange(_chordKind, _chordTarget) is not { } range) return;

        var rect = new Rect(TickToX(range.Start), ChordLaneTop + 2, (range.End - range.Start) * PixelsPerTick, ChordLaneHeight - 4);
        context.DrawRectangle(Palette.SelectionFill, Palette.SelectionBorder, rect, 3, 3);
    }

    #endregion
}
