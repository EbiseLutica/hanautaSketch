using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HanautaSketch.Engine.Core;
using AvaloniaKey = Avalonia.Input.Key;

namespace HanautaSketch.Controls;

/// <summary>
/// ピアノロールのマウス・キーボード操作。
/// </summary>
public sealed partial class PianoRollControl
{
    private const double ResizeHandleWidth = 6;
    private const int DefaultVelocity = 100;

    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);

    /// <summary>コピーしたノーツ。先頭ノートを tick 0 とした相対位置で持つ。トラックをまたいで貼り付けられるよう共有する</summary>
    private static List<Note> _clipboard = [];

    /// <summary>選択中のノート（Track.Notes のインデックス）</summary>
    private readonly HashSet<int> _selection = [];

    private int _lastNoteLength = Song.DefaultPpq / 2;

    private DragMode _dragMode;
    private int _dragAnchor = -1;
    private double _dragTickOffset;
    private Dictionary<int, Note> _dragOrigins = [];
    private int? _previewPitch;
    private Point _selectStart;
    private Point _selectCurrent;
    private HashSet<int> _selectionBeforeBox = [];

    private enum DragMode
    {
        None,
        Move,
        Resize,
        Select,
        Ruler,
        Keyboard,
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TrackProperty)
            _selection.Clear();
    }

    #region マウス

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var pos = e.GetPosition(this);
        var point = e.GetCurrentPoint(this);

        if (pos.Y < RulerHeight && pos.X >= KeyboardWidth)
        {
            if (!point.Properties.IsLeftButtonPressed) return;
            SetPlayStartFromX(pos.X);
            BeginDrag(DragMode.Ruler, e);
            return;
        }

        if (pos.X < KeyboardWidth && pos.Y >= HeaderHeight)
        {
            if (!point.Properties.IsLeftButtonPressed) return;
            StartPreview(Math.Clamp(YToPitch(pos.Y), 0, PitchCount - 1));
            BeginDrag(DragMode.Keyboard, e);
            return;
        }

        if (Track is null || !EditArea.Contains(pos)) return;

        var hit = HitTest(pos, out var onResizeHandle);

        if (point.Properties.IsRightButtonPressed)
        {
            // 右クリック: そのノートの長さを、新しく置くノートの既定長にする
            if (hit >= 0) _lastNoteLength = Track.Notes[hit].Length;
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2 && hit >= 0)
        {
            DeleteNotes([hit]);
            e.Handled = true;
            return;
        }

        var modifiers = e.KeyModifiers;
        if (hit >= 0)
        {
            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                if (!_selection.Remove(hit)) _selection.Add(hit);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (!_selection.Contains(hit))
            {
                _selection.Clear();
                _selection.Add(hit);
            }

            BeginNoteDrag(hit, onResizeHandle ? DragMode.Resize : DragMode.Move, pos);
            StartPreview(Track.Notes[hit].Pitch);
            BeginDrag(_dragMode, e);
            return;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl+ドラッグで矩形選択。Shift も押していれば既存の選択に追加
            _selectionBeforeBox = modifiers.HasFlag(KeyModifiers.Shift) ? [.. _selection] : [];
            _selectStart = _selectCurrent = pos;
            BeginDrag(DragMode.Select, e);
            UpdateBoxSelection();
            return;
        }

        // 空き領域クリックで新規配置し、そのままドラッグで移動（設定により伸縮）
        var pitch = Math.Clamp(YToPitch(pos.Y), 0, PitchCount - 1);
        var tick = SnapFloor(Math.Max(0, XToTick(pos.X)));
        Track.Notes.Add(new Note(tick, _lastNoteLength, pitch, DefaultVelocity));
        var index = Track.Notes.Count - 1;
        _selection.Clear();
        _selection.Add(index);
        RaiseNotesEdited();

        BeginNoteDrag(index, NewNoteDrag == NewNoteDragBehavior.Resize ? DragMode.Resize : DragMode.Move, pos);
        StartPreview(pitch);
        BeginDrag(_dragMode, e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        switch (_dragMode)
        {
            case DragMode.None:
                UpdateHoverCursor(pos);
                break;
            case DragMode.Ruler:
                SetPlayStartFromX(pos.X);
                break;
            case DragMode.Keyboard:
                StartPreview(Math.Clamp(YToPitch(pos.Y), 0, PitchCount - 1));
                break;
            case DragMode.Select:
                _selectCurrent = pos;
                UpdateBoxSelection();
                break;
            case DragMode.Move:
                DragMove(pos);
                break;
            case DragMode.Resize:
                DragResize(pos);
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragMode == DragMode.None) return;

        if (_dragMode == DragMode.Resize && Track is not null && _dragAnchor >= 0 && _dragAnchor < Track.Notes.Count)
            _lastNoteLength = Track.Notes[_dragAnchor].Length;

        StopPreview();
        _dragMode = DragMode.None;
        _dragAnchor = -1;
        _dragOrigins = [];
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        StopPreview();
        _dragMode = DragMode.None;
        InvalidateVisual();
    }

    private void BeginDrag(DragMode mode, PointerPressedEventArgs e)
    {
        _dragMode = mode;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>
    /// ノートの移動・伸縮を始める。選択中のノーツ全体の元位置を控えておく。
    /// </summary>
    private void BeginNoteDrag(int anchor, DragMode mode, Point pos)
    {
        if (Track is null) return;
        _dragMode = mode;
        _dragAnchor = anchor;
        _dragTickOffset = XToTick(pos.X) - Track.Notes[anchor].Tick;
        _dragOrigins = _selection.ToDictionary(i => i, i => Track.Notes[i]);
    }

    /// <summary>
    /// 掴んだノートをカーソルに合わせ、選択中のノーツを同じだけ動かす。
    /// </summary>
    private void DragMove(Point pos)
    {
        if (Track is null || !_dragOrigins.TryGetValue(_dragAnchor, out var anchor)) return;

        var deltaTick = SnapRound(XToTick(pos.X) - _dragTickOffset) - anchor.Tick;
        var deltaPitch = Math.Clamp(YToPitch(pos.Y), 0, PitchCount - 1) - anchor.Pitch;

        // 選択全体がはみ出さない範囲に収める
        deltaTick = Math.Max(deltaTick, -_dragOrigins.Values.Min(n => n.Tick));
        deltaPitch = Math.Clamp(deltaPitch, -_dragOrigins.Values.Min(n => n.Pitch),
            PitchCount - 1 - _dragOrigins.Values.Max(n => n.Pitch));

        if (Track.Notes[_dragAnchor] == anchor with { Tick = anchor.Tick + deltaTick, Pitch = anchor.Pitch + deltaPitch })
            return;

        foreach (var (index, origin) in _dragOrigins)
            Track.Notes[index] = origin with { Tick = origin.Tick + deltaTick, Pitch = origin.Pitch + deltaPitch };

        StartPreview(anchor.Pitch + deltaPitch);
        RaiseNotesEdited();
    }

    /// <summary>
    /// 掴んだノートの終端をカーソルに合わせ、選択中のノーツの長さを同じだけ変える。
    /// </summary>
    private void DragResize(Point pos)
    {
        if (Track is null || !_dragOrigins.TryGetValue(_dragAnchor, out var anchor)) return;

        var deltaLength = SnapRound(XToTick(pos.X)) - anchor.EndTick;
        if (Track.Notes[_dragAnchor].Length == Math.Max(SnapTicks, anchor.Length + deltaLength)) return;

        foreach (var (index, origin) in _dragOrigins)
            Track.Notes[index] = origin with { Length = Math.Max(SnapTicks, origin.Length + deltaLength) };

        RaiseNotesEdited();
    }

    private void UpdateBoxSelection()
    {
        if (Track is null) return;

        var box = new Rect(_selectStart, _selectCurrent);
        _selection.Clear();
        _selection.UnionWith(_selectionBeforeBox);
        for (var i = 0; i < Track.Notes.Count; i++)
        {
            if (NoteRect(Track.Notes[i]).Intersects(box)) _selection.Add(i);
        }
        InvalidateVisual();
    }

    private void UpdateHoverCursor(Point pos)
    {
        var hit = HitTest(pos, out var onResizeHandle);
        Cursor = hit < 0 ? Cursor.Default : onResizeHandle ? ResizeCursor : MoveCursor;
    }

    private void SetPlayStartFromX(double x) => PlayStartTick = Math.Max(0, SnapRound(XToTick(x)));

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

    #region スクロール・ズーム

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

    #endregion

    #region キーボード・クリップボード

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Track is null || _dragMode != DragMode.None) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case AvaloniaKey.Delete or AvaloniaKey.Back:
                DeleteNotes(_selection.ToList());
                break;
            case AvaloniaKey.Escape:
                _selection.Clear();
                InvalidateVisual();
                break;
            case AvaloniaKey.A when ctrl:
                _selection.Clear();
                _selection.UnionWith(Enumerable.Range(0, Track.Notes.Count));
                InvalidateVisual();
                break;
            case AvaloniaKey.C when ctrl:
                CopySelection();
                break;
            case AvaloniaKey.X when ctrl:
                CopySelection();
                DeleteNotes(_selection.ToList());
                break;
            case AvaloniaKey.V when ctrl:
                Paste();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void CopySelection()
    {
        if (Track is null || _selection.Count == 0) return;

        var notes = _selection.Select(i => Track.Notes[i]).ToList();
        var origin = notes.Min(n => n.Tick);
        _clipboard = notes.Select(n => n with { Tick = n.Tick - origin }).OrderBy(n => n.Tick).ToList();
    }

    /// <summary>
    /// コピーしたノーツを再生開始位置に貼り付け、貼り付けたノーツを選択状態にする。
    /// </summary>
    private void Paste()
    {
        if (Track is null || _clipboard.Count == 0) return;

        _selection.Clear();
        foreach (var note in _clipboard)
        {
            Track.Notes.Add(note with { Tick = note.Tick + PlayStartTick });
            _selection.Add(Track.Notes.Count - 1);
        }
        RaiseNotesEdited();
    }

    /// <summary>
    /// 指定インデックスのノーツを削除し、選択を解除する。
    /// </summary>
    private void DeleteNotes(IReadOnlyCollection<int> indices)
    {
        if (Track is null || indices.Count == 0) return;

        foreach (var index in indices.OrderDescending())
            Track.Notes.RemoveAt(index);

        _selection.Clear();
        RaiseNotesEdited();
    }

    #endregion

    #region 通知

    private void RaiseNotesEdited()
    {
        NotesEdited?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// 確認音を鳴らす。同じ音高が鳴っていれば何もしない。
    /// </summary>
    private void StartPreview(int pitch)
    {
        if (_previewPitch == pitch) return;
        _previewPitch = pitch;
        NotePreview?.Invoke(this, new NotePreviewEventArgs(pitch, DefaultVelocity));
    }

    private void StopPreview()
    {
        if (_previewPitch is null) return;
        _previewPitch = null;
        NotePreview?.Invoke(this, new NotePreviewEventArgs(null, 0));
    }

    #endregion
}

/// <summary>
/// 確認音の発音・停止の通知。Pitch が null なら停止。
/// </summary>
public sealed class NotePreviewEventArgs(int? pitch, int velocity) : EventArgs
{
    public int? Pitch { get; } = pitch;

    public int Velocity { get; } = velocity;
}
