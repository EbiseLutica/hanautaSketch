using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;
using Key = HanautaSketch.Engine.Core.Key;

namespace HanautaSketch.Controls;

/// <summary>
/// 選択中トラックのノーツを編集するピアノロール。
/// 左に鍵盤、上にルーラーを持ち、残りがノート編集領域。
/// </summary>
public sealed class PianoRollControl : Control
{
    public static readonly StyledProperty<Song?> SongProperty =
        AvaloniaProperty.Register<PianoRollControl, Song?>(nameof(Song));

    public static readonly StyledProperty<NoteTrack?> TrackProperty =
        AvaloniaProperty.Register<PianoRollControl, NoteTrack?>(nameof(Track));

    public static readonly StyledProperty<Key> KeyProperty =
        AvaloniaProperty.Register<PianoRollControl, Key>(nameof(Key));

    public static readonly StyledProperty<int> SnapTicksProperty =
        AvaloniaProperty.Register<PianoRollControl, int>(nameof(SnapTicks), Song.DefaultPpq / 4);

    public static readonly StyledProperty<NewNoteDragBehavior> NewNoteDragProperty =
        AvaloniaProperty.Register<PianoRollControl, NewNoteDragBehavior>(nameof(NewNoteDrag));

    public static readonly StyledProperty<IReadOnlyList<ChordSegment>?> ChordsProperty =
        AvaloniaProperty.Register<PianoRollControl, IReadOnlyList<ChordSegment>?>(nameof(Chords));

    public static readonly StyledProperty<double> PlayheadTickProperty =
        AvaloniaProperty.Register<PianoRollControl, double>(nameof(PlayheadTick), -1);

    private const double KeyboardWidth = 56;
    private const double RulerHeight = 22;
    private const double ChordLaneHeight = 22;
    private const double HeaderHeight = RulerHeight + ChordLaneHeight;
    private const double NoteHeight = 12;
    private const double ResizeHandleWidth = 6;
    private const int PitchCount = 128;

    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);

    private PianoRollPalette _palette = PianoRollPalette.Light;
    private double _scrollX;
    private double _scrollY = (PitchCount - 1 - 84) * NoteHeight;
    private double _pixelsPerBeat = 80;
    private int _lastNoteLength = Song.DefaultPpq / 2;

    private DragMode _dragMode;
    private int _dragIndex = -1;
    private double _dragTickOffset;

    private enum DragMode
    {
        None,
        Move,
        Resize,
    }

    static PianoRollControl()
    {
        AffectsRender<PianoRollControl>(SongProperty, TrackProperty, KeyProperty, SnapTicksProperty, ChordsProperty,
            PlayheadTickProperty);
        FocusableProperty.OverrideDefaultValue<PianoRollControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<PianoRollControl>(true);
    }

    /// <summary>再生ヘッドの位置（tick）。負なら非表示</summary>
    public double PlayheadTick
    {
        get => GetValue(PlayheadTickProperty);
        set => SetValue(PlayheadTickProperty, value);
    }

    /// <summary>空き領域を押したまま動かしたときの挙動</summary>
    public NewNoteDragBehavior NewNoteDrag
    {
        get => GetValue(NewNoteDragProperty);
        set => SetValue(NewNoteDragProperty, value);
    }

    /// <summary>コードレーンに表示する判別結果</summary>
    public IReadOnlyList<ChordSegment>? Chords
    {
        get => GetValue(ChordsProperty);
        set => SetValue(ChordsProperty, value);
    }

    /// <summary>ノーツが編集されたときに発生する</summary>
    public event EventHandler? NotesEdited;

    public PianoRollControl()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public Song? Song
    {
        get => GetValue(SongProperty);
        set => SetValue(SongProperty, value);
    }

    public NoteTrack? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public Key Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public int SnapTicks
    {
        get => GetValue(SnapTicksProperty);
        set => SetValue(SnapTicksProperty, value);
    }

    private int Ppq => Song?.Ppq ?? Song.DefaultPpq;

    private int TicksPerBar => Song?.TicksPerBar ?? Ppq * 4;

    private double PixelsPerTick => _pixelsPerBeat / Ppq;

    private Rect EditArea => new(KeyboardWidth, HeaderHeight,
        Math.Max(0, Bounds.Width - KeyboardWidth), Math.Max(0, Bounds.Height - HeaderHeight));

    #region 座標変換

    private double TickToX(double tick) => KeyboardWidth + tick * PixelsPerTick - _scrollX;

    private double XToTick(double x) => (x - KeyboardWidth + _scrollX) / PixelsPerTick;

    private double PitchToY(int pitch) => HeaderHeight + (PitchCount - 1 - pitch) * NoteHeight - _scrollY;

    private int YToPitch(double y) => PitchCount - 1 - (int)Math.Floor((y - HeaderHeight + _scrollY) / NoteHeight);

    private Rect NoteRect(Note note) =>
        new(TickToX(note.Tick), PitchToY(note.Pitch), Math.Max(2, note.Length * PixelsPerTick), NoteHeight);

    private int SnapFloor(double tick) => (int)Math.Floor(tick / SnapTicks) * SnapTicks;

    private int SnapRound(double tick) => (int)Math.Round(tick / SnapTicks) * SnapTicks;

    #endregion

    #region 描画

    public override void Render(DrawingContext context)
    {
        _palette = PianoRollPalette.For(ActualThemeVariant);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(_palette.Background, bounds);

        var area = EditArea;
        using (context.PushClip(area))
        {
            RenderRows(context, area);
            RenderGridLines(context, area);
            RenderGhostNotes(context);
            RenderNotes(context);
        }

        if (PlayheadTick >= 0)
        {
            var x = Math.Round(TickToX(PlayheadTick)) + 0.5;
            if (x >= area.X && x <= area.Right)
                context.DrawLine(_palette.Playhead, new Point(x, RulerHeight), new Point(x, area.Bottom));
        }

        using (context.PushClip(new Rect(0, HeaderHeight, KeyboardWidth, area.Height)))
        {
            RenderKeyboard(context);
        }

        using (context.PushClip(new Rect(KeyboardWidth, 0, area.Width, RulerHeight)))
        {
            RenderRuler(context, area);
        }

        using (context.PushClip(new Rect(KeyboardWidth, RulerHeight, area.Width, ChordLaneHeight)))
        {
            RenderChordLane(context, area);
        }

        context.FillRectangle(_palette.Ruler, new Rect(0, 0, KeyboardWidth, HeaderHeight));
    }

    /// <summary>
    /// 行背景。スケール内の音を明るく、主音をさらに強調する。
    /// </summary>
    private void RenderRows(DrawingContext context, Rect area)
    {
        var scale = Key.GetScalePitchClasses();
        var (top, bottom) = VisiblePitchRange(area);
        for (var pitch = bottom; pitch <= top; pitch++)
        {
            var y = PitchToY(pitch);
            var pc = pitch % 12;
            var row = new Rect(area.X, y, area.Width, NoteHeight);
            if (pc == Key.Tonic)
                context.FillRectangle(_palette.TonicRow, row);
            else if (scale.Contains(pc))
                context.FillRectangle(_palette.InScaleRow, row);

            context.DrawLine(_palette.RowLine, new Point(area.X, y + NoteHeight), new Point(area.Right, y + NoteHeight));
        }
    }

    /// <summary>
    /// 縦のグリッド線。スナップ単位・拍・小節で濃さを変える。
    /// </summary>
    private void RenderGridLines(DrawingContext context, Rect area)
    {
        var snap = Math.Max(1, SnapTicks);
        var startTick = SnapFloor(Math.Max(0, XToTick(area.X)));
        var endTick = XToTick(area.Right);
        var drawSnapLines = snap * PixelsPerTick >= 6;

        for (var tick = startTick; tick <= endTick; tick += snap)
        {
            IPen pen;
            if (tick % TicksPerBar == 0) pen = _palette.BarLine;
            else if (tick % Ppq == 0) pen = _palette.BeatLine;
            else if (drawSnapLines) pen = _palette.SnapLine;
            else continue;

            var x = Math.Round(TickToX(tick)) + 0.5;
            context.DrawLine(pen, new Point(x, area.Y), new Point(x, area.Bottom));
        }
    }

    /// <summary>
    /// 選択中以外のトラックのノーツを薄く表示する。
    /// </summary>
    private void RenderGhostNotes(DrawingContext context)
    {
        if (Song is null) return;
        foreach (var track in Song.Tracks.OfType<NoteTrack>())
        {
            if (ReferenceEquals(track, Track)) continue;
            foreach (var note in track.Notes)
                context.FillRectangle(_palette.GhostNote, NoteRect(note));
        }
    }

    private void RenderNotes(DrawingContext context)
    {
        if (Track is null) return;
        foreach (var note in Track.Notes)
        {
            var rect = NoteRect(note).Deflate(new Thickness(0, 0.5));
            var opacity = 0.45 + 0.55 * note.Velocity / 127.0;
            using (context.PushOpacity(opacity))
            {
                context.DrawRectangle(_palette.Note, _palette.NoteBorder, rect, 2, 2);
            }
        }
    }

    private void RenderKeyboard(DrawingContext context)
    {
        var area = EditArea;
        var (top, bottom) = VisiblePitchRange(area);
        for (var pitch = bottom; pitch <= top; pitch++)
        {
            var y = PitchToY(pitch);
            var isBlack = IsBlackKey(pitch);
            var rect = new Rect(0, y, KeyboardWidth, NoteHeight);
            context.FillRectangle(isBlack ? _palette.BlackKey : _palette.WhiteKey, rect);
            context.DrawLine(_palette.KeySeparator, new Point(0, y + NoteHeight), new Point(KeyboardWidth, y + NoteHeight));

            if (pitch % 12 == 0)
                DrawText(context, $"C{pitch / 12 - 1}", new Point(KeyboardWidth - 22, y), _palette.KeyLabel, 9);
        }
    }

    private void RenderRuler(DrawingContext context, Rect area)
    {
        context.FillRectangle(_palette.Ruler, new Rect(KeyboardWidth, 0, area.Width, RulerHeight));
        var startBar = (int)Math.Max(0, Math.Floor(XToTick(area.X) / TicksPerBar));
        var endBar = (int)Math.Ceiling(XToTick(area.Right) / TicksPerBar);
        for (var bar = startBar; bar <= endBar; bar++)
        {
            var x = Math.Round(TickToX(bar * TicksPerBar)) + 0.5;
            context.DrawLine(_palette.BarLine, new Point(x, 4), new Point(x, RulerHeight));
            DrawText(context, (bar + 1).ToString(CultureInfo.InvariantCulture), new Point(x + 4, 3), _palette.Text, 11);
        }
    }

    /// <summary>
    /// 判別されたコードを区間ごとに表示する。同じコードが続く区間はまとめて1つにする。
    /// </summary>
    private void RenderChordLane(DrawingContext context, Rect area)
    {
        context.FillRectangle(_palette.ChordLane, new Rect(KeyboardWidth, RulerHeight, area.Width, ChordLaneHeight));
        if (Chords is not { Count: > 0 } chords) return;

        var i = 0;
        while (i < chords.Count)
        {
            var head = chords[i];
            var end = head.End;
            var j = i + 1;
            while (j < chords.Count && chords[j].Chord == head.Chord && chords[j].IsOverride == head.IsOverride)
                end = chords[j++].End;

            var rect = new Rect(TickToX(head.Start), RulerHeight + 2, (end - head.Start) * PixelsPerTick, ChordLaneHeight - 4)
                .Deflate(new Thickness(1, 0));
            if (rect.Right >= area.X && rect.X <= area.Right)
            {
                context.DrawRectangle(head.IsOverride ? _palette.ChordOverride : _palette.ChordBlock,
                    _palette.ChordBorder, rect, 3, 3);
                using (context.PushClip(rect))
                {
                    DrawText(context, head.Chord.GetName(Key), new Point(rect.X + 4, rect.Y + 1), _palette.Text, 12);
                }
            }

            i = j;
        }
    }

    private (int Top, int Bottom) VisiblePitchRange(Rect area)
    {
        var top = Math.Clamp(YToPitch(area.Y), 0, PitchCount - 1);
        var bottom = Math.Clamp(YToPitch(area.Bottom), 0, PitchCount - 1);
        return (top, bottom);
    }

    private static void DrawText(DrawingContext context, string text, Point origin, IBrush brush, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush);
        context.DrawText(formatted, origin);
    }

    private static bool IsBlackKey(int pitch) => (pitch % 12) is 1 or 3 or 6 or 8 or 10;


    #endregion

    #region 入力

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Track is null) return;

        var pos = e.GetPosition(this);
        if (!EditArea.Contains(pos)) return;

        var props = e.GetCurrentPoint(this).Properties;
        var hit = HitTest(pos, out var onResizeHandle);

        if (props.IsRightButtonPressed)
        {
            // 右クリックで削除
            if (hit >= 0)
            {
                Track.Notes.RemoveAt(hit);
                NotesEdited?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        if (hit >= 0)
        {
            var note = Track.Notes[hit];
            _dragIndex = hit;
            _dragMode = onResizeHandle ? DragMode.Resize : DragMode.Move;
            _dragTickOffset = XToTick(pos.X) - note.Tick;
        }
        else
        {
            // 空き領域クリックで新規配置し、そのままドラッグで移動（設定により伸縮）
            var pitch = Math.Clamp(YToPitch(pos.Y), 0, PitchCount - 1);
            var tick = SnapFloor(Math.Max(0, XToTick(pos.X)));
            Track.Notes.Add(new Note(tick, _lastNoteLength, pitch, 100));
            NotesEdited?.Invoke(this, EventArgs.Empty);
            _dragIndex = Track.Notes.Count - 1;
            _dragMode = NewNoteDrag == NewNoteDragBehavior.Resize ? DragMode.Resize : DragMode.Move;
            _dragTickOffset = XToTick(pos.X) - tick;
            InvalidateVisual();
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Track is null) return;

        var pos = e.GetPosition(this);

        if (_dragMode == DragMode.None)
        {
            var hit = HitTest(pos, out var onResizeHandle);
            Cursor = hit < 0 ? Cursor.Default : onResizeHandle ? ResizeCursor : MoveCursor;
            return;
        }

        var note = Track.Notes[_dragIndex];
        switch (_dragMode)
        {
            case DragMode.Move:
            {
                var tick = Math.Max(0, SnapRound(XToTick(pos.X) - _dragTickOffset));
                var pitch = Math.Clamp(YToPitch(pos.Y), 0, PitchCount - 1);
                note = note with { Tick = tick, Pitch = pitch };
                break;
            }
            case DragMode.Resize:
            {
                var end = SnapRound(XToTick(pos.X));
                note = note with { Length = Math.Max(SnapTicks, end - note.Tick) };
                break;
            }
        }

        if (note != Track.Notes[_dragIndex])
        {
            Track.Notes[_dragIndex] = note;
            NotesEdited?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragMode == DragMode.None) return;

        if (Track is not null && _dragIndex >= 0 && _dragIndex < Track.Notes.Count)
            _lastNoteLength = Track.Notes[_dragIndex].Length;

        _dragMode = DragMode.None;
        _dragIndex = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos = e.GetPosition(this);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl+ホイール：カーソル位置を基準に横ズーム
            var anchorTick = XToTick(pos.X);
            var factor = e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
            _pixelsPerBeat = Math.Clamp(_pixelsPerBeat * factor, 8, 600);
            _scrollX = anchorTick * PixelsPerTick - (pos.X - KeyboardWidth);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _scrollX -= e.Delta.Y * 80;
        }
        else
        {
            _scrollX -= e.Delta.X * 80;
            _scrollY -= e.Delta.Y * NoteHeight * 3;
        }

        ClampScroll();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ClampScroll();
    }

    private void ClampScroll()
    {
        var maxY = Math.Max(0, PitchCount * NoteHeight - EditArea.Height);
        _scrollY = Math.Clamp(_scrollY, 0, maxY);
        _scrollX = Math.Max(0, _scrollX);
    }

    /// <summary>
    /// 座標にあるノーツのインデックスを返す。前面（後に追加された方）を優先。
    /// </summary>
    private int HitTest(Point pos, out bool onResizeHandle)
    {
        onResizeHandle = false;
        if (Track is null || !EditArea.Contains(pos)) return -1;

        for (var i = Track.Notes.Count - 1; i >= 0; i--)
        {
            var rect = NoteRect(Track.Notes[i]);
            if (!rect.Contains(pos)) continue;

            var handle = Math.Min(ResizeHandleWidth, rect.Width / 3);
            onResizeHandle = pos.X >= rect.Right - handle;
            return i;
        }

        return -1;
    }

    #endregion
}

/// <summary>
/// ピアノロールの空き領域を押したまま動かしたときの挙動。
/// </summary>
public enum NewNoteDragBehavior
{
    /// <summary>配置したノートをカーソルに追従させる</summary>
    Move,

    /// <summary>配置したノートの長さを伸縮する</summary>
    Resize,
}
