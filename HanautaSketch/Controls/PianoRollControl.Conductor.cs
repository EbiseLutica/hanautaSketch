using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.Controls;

/// <summary>
/// テンポ・拍子・キーの変化（コンダクター）の表示と操作。
/// ルーラー左端の▸で、ルーラーの下に 拍子・テンポ・キー の3レーンを展開する。
/// 畳んでいるときは、ルーラーに小さな印だけを出す。
/// レーンの空き位置のクリックで追加、変更点のクリックで編集・削除を依頼し、ドラッグで移動を依頼する。
/// </summary>
public sealed partial class PianoRollControl
{
    private const double ConductorLaneHeight = 18;

    /// <summary>この距離（px）以上動かしたら、クリックではなく変更点の移動とみなす</summary>
    private const double ConductorDragThreshold = 4;

    private const double MarkerLabelSize = 11;

    private bool _conductorExpanded;

    /// <summary>押した位置のレーンと変更点（空き位置なら null）、ドラッグの開始・現在の X</summary>
    private (ConductorChangeKind Kind, ConductorMarker? Marker, double StartX, double CurrentX)? _conductorDrag;

    /// <summary>レーンのクリックで、変更点の追加・編集を依頼する</summary>
    public event EventHandler<ConductorLaneClickedEventArgs>? ConductorLaneClicked;

    /// <summary>変更点のドラッグで、移動を依頼する</summary>
    public event EventHandler<ConductorChangeMoveRequestedEventArgs>? ConductorChangeMoveRequested;

    private double ConductorHeight => _conductorExpanded ? ConductorLaneHeight * 3 : 0;

    /// <summary>
    /// 変更点1つ分の表示。Position は拍子なら小節番号、それ以外は tick。
    /// </summary>
    private sealed record ConductorMarker(ConductorChangeKind Kind, int Position, int Tick, string Label);

    private IEnumerable<ConductorMarker> GetConductorMarkers()
    {
        var conductor = Conductor;
        foreach (var meter in conductor.Meters)
            yield return new ConductorMarker(ConductorChangeKind.Meter, meter.Bar, conductor.BarToTick(meter.Bar),
                $"{meter.BeatsPerBar}/4");
        foreach (var tempo in conductor.Tempos)
            yield return new ConductorMarker(ConductorChangeKind.Tempo, tempo.Tick, tempo.Tick,
                tempo.Bpm.ToString("0.##", CultureInfo.InvariantCulture));
        foreach (var key in conductor.Keys)
            yield return new ConductorMarker(ConductorChangeKind.Key, key.Tick, key.Tick, key.Key.ToString());
    }

    private double ConductorLaneTop(ConductorChangeKind kind) => RulerHeight + (int)kind * ConductorLaneHeight;

    private IBrush MarkerBrush(ConductorChangeKind kind) => kind switch
    {
        ConductorChangeKind.Meter => Palette.MeterMarker,
        ConductorChangeKind.Tempo => Palette.TempoMarker,
        _ => Palette.KeyMarker,
    };

    #region 描画

    /// <summary>
    /// 展開したレーン。変更位置に縦棒、その右にラベル。
    /// </summary>
    private void RenderConductorLanes(DrawingContext context, Rect area)
    {
        if (!_conductorExpanded) return;

        context.FillRectangle(Palette.ConductorLane, new Rect(KeyboardWidth, RulerHeight, area.Width, ConductorHeight));
        for (var i = 1; i <= 3; i++)
        {
            var y = RulerHeight + i * ConductorLaneHeight - 0.5;
            context.DrawLine(Palette.RowLine, new Point(KeyboardWidth, y), new Point(KeyboardWidth + area.Width, y));
        }

        foreach (var marker in GetConductorMarkers())
        {
            var x = Math.Round(TickToX(marker.Tick));
            if (x > area.Right) continue;
            var top = ConductorLaneTop(marker.Kind);
            var brush = MarkerBrush(marker.Kind);
            context.FillRectangle(brush, new Rect(x, top + 2, 2, ConductorLaneHeight - 4));
            DrawText(context, marker.Label, new Point(x + 5, top + 1), Palette.Text, MarkerLabelSize);
        }

        // ドラッグ中の移動先
        if (_dragMode == DragMode.Conductor && _conductorDrag is { Marker: { } dragged } drag && IsConductorDrag)
        {
            var target = ConductorPositionToTick(drag.Kind, SnappedConductorPosition(drag.Kind, drag.CurrentX));
            var x = Math.Round(TickToX(target));
            context.FillRectangle(Palette.SelectionFill,
                new Rect(x - 1, ConductorLaneTop(dragged.Kind) + 1, 4, ConductorLaneHeight - 2));
        }
    }

    /// <summary>
    /// 畳んでいるとき、ルーラーの下端に変更点の小さな印を出す（曲頭は除く）。
    /// </summary>
    private void RenderConductorIndicators(DrawingContext context)
    {
        if (_conductorExpanded) return;

        foreach (var marker in GetConductorMarkers().Where(m => m.Tick > 0))
        {
            var x = Math.Round(TickToX(marker.Tick)) + (int)marker.Kind * 4;
            context.FillRectangle(MarkerBrush(marker.Kind), new Rect(x, RulerHeight - 6, 3, 6));
        }
    }

    /// <summary>
    /// 鍵盤の列の上の、展開ボタンとレーン名。
    /// </summary>
    private void RenderConductorHeader(DrawingContext context)
    {
        DrawText(context, _conductorExpanded ? "▾ 変化" : "▸ 変化", new Point(4, 4), Palette.Text, 11);
        if (!_conductorExpanded) return;

        string[] names = ["拍子", "テンポ", "キー"];
        for (var i = 0; i < names.Length; i++)
        {
            var kind = (ConductorChangeKind)i;
            var top = ConductorLaneTop(kind);
            context.FillRectangle(MarkerBrush(kind), new Rect(4, top + 4, 3, ConductorLaneHeight - 8));
            DrawText(context, names[i], new Point(10, top + 2), Palette.Text, 10);
        }
    }

    #endregion

    #region 入力

    private bool IsConductorDrag =>
        _conductorDrag is { } drag && Math.Abs(drag.CurrentX - drag.StartX) >= ConductorDragThreshold;

    /// <summary>
    /// 展開ボタンとコンダクターレーンの入力。扱ったら true。
    /// </summary>
    private bool TryBeginConductorInput(PointerPressedEventArgs e, Point pos, PointerPoint point)
    {
        if (pos.X < KeyboardWidth && pos.Y < RulerHeight)
        {
            if (!point.Properties.IsLeftButtonPressed) return true;
            _conductorExpanded = !_conductorExpanded;
            UpdateScrollExtents();
            InvalidateVisual();
            e.Handled = true;
            return true;
        }

        if (ConductorLaneAt(pos) is not { } kind) return false;
        if (!point.Properties.IsLeftButtonPressed) return true;

        _conductorDrag = (kind, HitConductorMarker(kind, pos.X), pos.X, pos.X);
        BeginDrag(DragMode.Conductor, e);
        return true;
    }

    private void UpdateConductorDrag(Point pos)
    {
        if (_conductorDrag is not { } drag) return;
        _conductorDrag = drag with { CurrentX = pos.X };
        InvalidateVisual();
    }

    /// <summary>
    /// 変更点のドラッグなら移動を、クリックなら追加・編集を依頼する。曲頭の変更点は動かせない。
    /// </summary>
    private void CompleteConductorInput()
    {
        if (_conductorDrag is not { } drag) return;
        _conductorDrag = null;

        var moved = Math.Abs(drag.CurrentX - drag.StartX) >= ConductorDragThreshold;
        if (drag.Marker is { } marker)
        {
            if (!moved)
            {
                ConductorLaneClicked?.Invoke(this, new ConductorLaneClickedEventArgs(marker.Kind, marker.Position, true));
                return;
            }

            var target = SnappedConductorPosition(drag.Kind, drag.CurrentX);
            if (marker.Position > 0 && target > 0 && target != marker.Position)
                ConductorChangeMoveRequested?.Invoke(this,
                    new ConductorChangeMoveRequestedEventArgs(marker.Kind, marker.Position, target));
            return;
        }

        if (!moved)
            ConductorLaneClicked?.Invoke(this,
                new ConductorLaneClickedEventArgs(drag.Kind, SnappedConductorPosition(drag.Kind, drag.StartX), false));
    }

    private ConductorChangeKind? ConductorLaneAt(Point pos)
    {
        if (!_conductorExpanded || pos.X < KeyboardWidth || pos.Y < RulerHeight || pos.Y >= RulerHeight + ConductorHeight)
            return null;
        return (ConductorChangeKind)Math.Clamp((int)((pos.Y - RulerHeight) / ConductorLaneHeight), 0, 2);
    }

    /// <summary>
    /// レーン上の X にある変更点（縦棒からラベルの右端まで）。重なっていれば右にあるものを優先する。
    /// </summary>
    private ConductorMarker? HitConductorMarker(ConductorChangeKind kind, double x) =>
        GetConductorMarkers()
            .Where(m => m.Kind == kind)
            .LastOrDefault(m =>
            {
                var left = TickToX(m.Tick);
                return left - 4 <= x && x <= left + 5 + MeasureText(m.Label, MarkerLabelSize) + 2;
            });

    private bool IsOverMovableConductorMarker(Point pos) =>
        ConductorLaneAt(pos) is { } kind && HitConductorMarker(kind, pos.X) is { Position: > 0 };

    /// <summary>X の位置を変更点の位置にする。拍子は最寄りの小節頭の小節番号、それ以外はスナップした tick</summary>
    private int SnappedConductorPosition(ConductorChangeKind kind, double x)
    {
        var tick = Math.Max(0, XToTick(x));
        return kind == ConductorChangeKind.Meter ? Conductor.BarAt(Conductor.RoundToBar(tick)) : Math.Max(0, SnapRound(tick));
    }

    private int ConductorPositionToTick(ConductorChangeKind kind, int position) =>
        kind == ConductorChangeKind.Meter ? Conductor.BarToTick(position) : position;

    private static double MeasureText(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size,
            Brushes.Black).Width;

    #endregion
}

/// <summary>
/// コンダクターレーンのクリック。Position は拍子なら小節番号、それ以外は tick。
/// IsExisting なら既存の変更点の編集、そうでなければその位置への追加。
/// </summary>
public sealed class ConductorLaneClickedEventArgs(ConductorChangeKind kind, int position, bool isExisting) : EventArgs
{
    public ConductorChangeKind Kind { get; } = kind;

    public int Position { get; } = position;

    public bool IsExisting { get; } = isExisting;
}

/// <summary>
/// 変更点の移動の依頼。From・To は拍子なら小節番号、それ以外は tick。
/// </summary>
public sealed class ConductorChangeMoveRequestedEventArgs(ConductorChangeKind kind, int from, int to) : EventArgs
{
    public ConductorChangeKind Kind { get; } = kind;

    public int From { get; } = from;

    public int To { get; } = to;
}
