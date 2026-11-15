using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Editing;
using HanautaSketch.Engine.Smart.Drums;

namespace HanautaSketch.Controls;

/// <summary>
/// ピアノロール下部のベロシティレーン。横スクロール・ズーム・表示トラック・選択はピアノロールと共有する。
/// クリックでその高さに、ドラッグでなぞった範囲を軌跡の高さに揃える。選択中は選択ノートだけが対象。
/// </summary>
public sealed partial class PianoRollControl
{
    private const double SplitterHeight = 5;
    private const double MinVelocityLaneHeight = 40;
    private const double MaxVelocityLaneRatio = 0.6;
    private const double VelocityBarWidth = 4;

    /// <summary>クリック1点でも棒に当たるよう、なぞった線分の両端をこのピクセル数だけ広げる</summary>
    private const double VelocityHitTolerance = 4;

    private static readonly Cursor SplitterCursor = new(StandardCursorType.SizeNorthSouth);

    private double _velocityLaneHeight = 100;
    private Point _velocityLast;
    private HashSet<int>? _velocityTargets;

    private double VelocityLaneHeight =>
        Math.Clamp(_velocityLaneHeight, MinVelocityLaneHeight, Math.Max(MinVelocityLaneHeight, Bounds.Height * MaxVelocityLaneRatio));

    private Rect VelocityArea => new(KeyboardWidth, Bounds.Height - VelocityLaneHeight,
        Math.Max(0, Bounds.Width - KeyboardWidth), VelocityLaneHeight);

    /// <summary>ノート編集領域とベロシティレーンの境界（ドラッグで高さを変える）</summary>
    private Rect SplitterRect => new(0, Bounds.Height - VelocityLaneHeight - SplitterHeight, Bounds.Width, SplitterHeight);

    private double VelocityToY(int velocity) =>
        VelocityArea.Bottom - (double)velocity / VelocityEditing.MaxVelocity * VelocityArea.Height;

    private double YToVelocity(double y) =>
        (VelocityArea.Bottom - y) / VelocityArea.Height * VelocityEditing.MaxVelocity;

    #region 描画

    private void RenderVelocityLane(DrawingContext context)
    {
        var area = VelocityArea;
        context.FillRectangle(Palette.ChordLane, SplitterRect);
        context.FillRectangle(Palette.Background, area);
        context.FillRectangle(Palette.Ruler, new Rect(0, area.Y, KeyboardWidth, area.Height));
        DrawText(context, "Vel", new Point(6, area.Y + 4), Palette.Text, 11);

        using (context.PushClip(area))
        {
            RenderGridLines(context, area);

            var middle = Math.Round(VelocityToY(64)) + 0.5;
            context.DrawLine(Palette.BeatLine, new Point(area.X, middle), new Point(area.Right, middle));

            if (Track is INoteEditable track)
            {
                // ドラムの生成区間は読み取り専用で先に描く
                if (Track is SmartDrumTrack drum)
                {
                    foreach (var note in drum.PlaybackNotes)
                    {
                        if (!drum.Sections.Any(s => s.IsFixed && s.Contains(note.Tick)))
                            RenderVelocityBar(context, note, Palette.GeneratedNote);
                    }
                }

                var notes = track.EditableNotes;
                for (var i = 0; i < notes.Count; i++)
                {
                    var brush = _selection.Count == 0 ? Palette.Note
                        : _selection.Contains(i) ? Palette.NoteSelected
                        : Palette.GhostNote;
                    RenderVelocityBar(context, notes[i], brush);
                }
            }
            else if (Track is not null)
            {
                foreach (var note in Track.PlaybackNotes)
                    RenderVelocityBar(context, note, Palette.GeneratedNote);
            }
        }
    }

    private void RenderVelocityBar(DrawingContext context, Note note, IBrush brush)
    {
        var x = TickToX(note.Tick);
        var top = VelocityToY(note.Velocity);
        context.FillRectangle(brush, new Rect(x, top, VelocityBarWidth, VelocityArea.Bottom - top));
    }

    #endregion

    #region 入力

    /// <summary>
    /// 境界のドラッグかベロシティの編集を始める。始めたら true。
    /// </summary>
    private bool TryBeginVelocityInput(PointerPressedEventArgs e, Point pos, PointerPoint point)
    {
        if (!point.Properties.IsLeftButtonPressed) return false;

        if (SplitterRect.Contains(pos))
        {
            BeginDrag(DragMode.VelocityResize, e);
            return true;
        }

        if (!VelocityArea.Contains(pos)) return false;

        e.Handled = true;
        if (EditTrack is null) return true;

        _velocityTargets = _selection.Count > 0 ? [.. _selection] : null;
        _velocityLast = pos;
        EditStarted?.Invoke(this, EventArgs.Empty);
        BeginDrag(DragMode.Velocity, e);
        ApplyVelocityStroke(pos, pos);
        return true;
    }

    private void ContinueVelocityStroke(Point pos)
    {
        ApplyVelocityStroke(_velocityLast, pos);
        _velocityLast = pos;
    }

    /// <summary>
    /// 前回の位置から今回の位置までをなぞった線分として、範囲内のノートのベロシティを揃え、
    /// 今カーソルの下にあるノートを新しいベロシティで鳴らす。
    /// </summary>
    private void ApplyVelocityStroke(Point from, Point to)
    {
        if (EditTrack is not { } track) return;

        var tolerance = VelocityHitTolerance / PixelsPerTick;
        var changed = VelocityEditing.ApplyStroke(track.EditableNotes, _velocityTargets,
            XToTick(from.X), YToVelocity(from.Y), XToTick(to.X), YToVelocity(to.Y), tolerance);
        if (changed) RaiseNotesEdited();

        PreviewNotesUnderCursor(track, to.X, tolerance);
    }

    /// <summary>
    /// カーソルの横位置で発音するノート（和音ならまとめて）を鳴らす。なければ止める。
    /// </summary>
    private void PreviewNotesUnderCursor(INoteEditable track, double x, double tolerance)
    {
        var tick = XToTick(x);
        var notes = track.EditableNotes
            .Where((n, i) => (_velocityTargets is null || _velocityTargets.Contains(i)) && Math.Abs(n.Tick - tick) <= tolerance)
            .Select(n => (n.Pitch, n.Velocity))
            .OrderBy(n => n.Pitch)
            .ToArray();

        if (notes.Length == 0) StopPreview();
        else StartPreview(notes);
    }

    private void ResizeVelocityLane(Point pos)
    {
        _velocityLaneHeight = Bounds.Height - pos.Y - SplitterHeight / 2;
        UpdateScrollExtents();
    }

    #endregion
}
