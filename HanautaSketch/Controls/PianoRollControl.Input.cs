using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Editing;
using HanautaSketch.Services;
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

    /// <summary>選択中のノート（EditTrack.EditableNotes のインデックス）</summary>
    private readonly HashSet<int> _selection = [];

    private int _lastNoteLength = Song.DefaultPpq / 2;

    private DragMode _dragMode;
    private int _dragAnchor = -1;
    private double _dragTickOffset;
    private Dictionary<int, Note> _dragOrigins = [];
    /// <summary>鳴らしているプレビュー音。同じ組み合わせなら鳴らし直さない</summary>
    private (int Pitch, int Velocity)[] _previewNotes = [];
    private Point _selectStart;
    private Point _selectCurrent;
    private HashSet<int> _selectionBeforeBox = [];

    /// <summary>
    /// 現在の選択が、配置や未選択ノートの移動のために一時的に付けたものか。そうなら操作の終了時に解除する。
    /// 矩形選択・Shift+クリック・Ctrl+A・貼り付けなど、意図して選んだ選択は残す。
    /// </summary>
    private bool _temporarySelection;
    private Point _panStart;
    private Vector _panStartOffset;

    private enum DragMode
    {
        None,
        Move,
        Resize,
        Select,
        Ruler,
        Keyboard,
        Pan,
        Velocity,
        VelocityResize,
        DrumSection,
        Conductor,
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TrackProperty || change.Property == NotesRevisionProperty)
        {
            _selection.Clear();
            UpdateScrollExtents();
        }
        else if (change.Property == SongProperty)
        {
            UpdateScrollExtents();
        }
        else if (change.Property == HorizontalOffsetProperty || change.Property == VerticalOffsetProperty)
        {
            ClampOffsets();
        }
    }

    #region マウス

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var pos = e.GetPosition(this);
        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsMiddleButtonPressed)
        {
            // 中ボタンのドラッグでスクロール
            _panStart = pos;
            _panStartOffset = new Vector(HorizontalOffset, VerticalOffset);
            BeginDrag(DragMode.Pan, e);
            return;
        }

        if (TryBeginVelocityInput(e, pos, point)) return;

        if (TryBeginConductorInput(e, pos, point)) return;

        if (pos.Y >= ChordLaneTop && pos.Y < HeaderHeight && pos.X >= KeyboardWidth)
        {
            if (!point.Properties.IsLeftButtonPressed) return;

            // ドラムトラックではセクションレーン
            if (Track is HanautaSketch.Engine.Smart.Drums.SmartDrumTrack)
            {
                BeginDrumSectionInput(e, pos);
                return;
            }

            // コードレーンのクリックで、そのコードの変更メニューを出してもらう
            var laneTick = XToTick(pos.X);
            var block = GetChordBlocks().FirstOrDefault(b => b.Start <= laneTick && laneTick < b.End);
            if (block is not null) ChordSegmentClicked?.Invoke(this, new ChordSegmentClickedEventArgs(block));
            e.Handled = true;
            return;
        }

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

        if (EditTrack is null || !EditArea.Contains(pos)) return;

        var hit = HitTest(pos, out var onResizeHandle);

        if (point.Properties.IsRightButtonPressed)
        {
            // 右クリック: そのノートの長さを、新しく置くノートの既定長にする
            if (hit >= 0) _lastNoteLength = EditTrack.EditableNotes[hit].Length;
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2 && hit >= 0)
        {
            EditStarted?.Invoke(this, EventArgs.Empty);
            DeleteNotes([hit]);
            EditCompleted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        var modifiers = e.KeyModifiers;
        if (PlatformKeys.IsCommand(modifiers))
        {
            // Ctrl（macOS は ⌘）+ドラッグで矩形選択。押した位置を再生開始位置（貼り付け先）にする。Shift も押していれば既存の選択に追加
            SetPlayStartFromX(pos.X);
            _selectionBeforeBox = modifiers.HasFlag(KeyModifiers.Shift) ? [.. _selection] : [];
            _selectStart = _selectCurrent = pos;
            BeginDrag(DragMode.Select, e);
            UpdateBoxSelection();
            return;
        }

        if (hit >= 0)
        {
            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                if (!_selection.Remove(hit)) _selection.Add(hit);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // 未選択のノートを掴んだときの選択は、操作のための一時的なもの（離したら解除）
            _temporarySelection = !_selection.Contains(hit);
            if (_temporarySelection)
            {
                _selection.Clear();
                _selection.Add(hit);
            }

            EditStarted?.Invoke(this, EventArgs.Empty);
            BeginNoteDrag(hit, onResizeHandle ? DragMode.Resize : DragMode.Move, pos);
            StartPreview(EditTrack.EditableNotes[hit].Pitch);
            BeginDrag(_dragMode, e);
            return;
        }

        // 空き領域クリックで新規配置し、そのままドラッグで移動（設定により伸縮）
        var pitch = Math.Clamp(YToPitch(pos.Y), 0, PitchCount - 1);
        var tick = SnapFloor(Math.Max(0, XToTick(pos.X)));
        if (!EditTrack.CanPlaceAt(tick))
        {
            // ドラムの生成区間など、ノートを置けない位置
            e.Handled = true;
            return;
        }

        EditStarted?.Invoke(this, EventArgs.Empty);
        EditTrack.EditableNotes.Add(new Note(tick, _lastNoteLength, pitch, DefaultVelocity));
        var index = EditTrack.EditableNotes.Count - 1;
        _selection.Clear();
        _selection.Add(index);
        _temporarySelection = true;
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
            case DragMode.Velocity:
                ContinueVelocityStroke(pos);
                break;
            case DragMode.VelocityResize:
                ResizeVelocityLane(pos);
                break;
            case DragMode.DrumSection:
                UpdateDrumSectionDrag(pos);
                break;
            case DragMode.Conductor:
                UpdateConductorDrag(pos);
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
            case DragMode.Pan:
                HorizontalOffset = _panStartOffset.X - (pos.X - _panStart.X);
                VerticalOffset = _panStartOffset.Y - (pos.Y - _panStart.Y);
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragMode == DragMode.None) return;

        if (_dragMode == DragMode.Resize && EditTrack is not null && _dragAnchor >= 0 && _dragAnchor < EditTrack.EditableNotes.Count)
            _lastNoteLength = EditTrack.EditableNotes[_dragAnchor].Length;

        if (_dragMode is DragMode.Move or DragMode.Resize)
            ResolveOverlaps();

        if (_dragMode is DragMode.Move or DragMode.Resize or DragMode.Velocity)
        {
            EditCompleted?.Invoke(this, EventArgs.Empty);
            UpdateScrollExtents();
        }

        if (_dragMode is DragMode.Move or DragMode.Resize && _temporarySelection)
            _selection.Clear();
        _temporarySelection = false;

        if (_dragMode == DragMode.DrumSection)
            CompleteDrumSectionInput();
        if (_dragMode == DragMode.Conductor)
            CompleteConductorInput();

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
        if (_dragMode is DragMode.Move or DragMode.Resize)
            ResolveOverlaps();
        if (_dragMode is DragMode.Move or DragMode.Resize or DragMode.Velocity)
            EditCompleted?.Invoke(this, EventArgs.Empty);

        if (_dragMode is DragMode.Move or DragMode.Resize && _temporarySelection)
            _selection.Clear();
        _temporarySelection = false;

        if (_dragMode == DragMode.DrumSection)
            CompleteDrumSectionInput();
        if (_dragMode == DragMode.Conductor)
            CompleteConductorInput();

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
        if (EditTrack is null) return;
        _dragMode = mode;
        _dragAnchor = anchor;
        _dragTickOffset = XToTick(pos.X) - EditTrack.EditableNotes[anchor].Tick;
        _dragOrigins = _selection.ToDictionary(i => i, i => EditTrack.EditableNotes[i]);
    }

    /// <summary>
    /// 掴んだノートをカーソルに合わせ、選択中のノーツを同じだけ動かす。
    /// </summary>
    private void DragMove(Point pos)
    {
        if (EditTrack is null || !_dragOrigins.TryGetValue(_dragAnchor, out var anchor)) return;

        var deltaTick = SnapRound(XToTick(pos.X) - _dragTickOffset) - anchor.Tick;
        var deltaPitch = Math.Clamp(YToPitch(pos.Y), 0, PitchCount - 1) - anchor.Pitch;

        // 選択全体がはみ出さない範囲に収める
        deltaTick = Math.Max(deltaTick, -_dragOrigins.Values.Min(n => n.Tick));
        deltaPitch = Math.Clamp(deltaPitch, -_dragOrigins.Values.Min(n => n.Pitch),
            PitchCount - 1 - _dragOrigins.Values.Max(n => n.Pitch));

        if (EditTrack.EditableNotes[_dragAnchor] == anchor with { Tick = anchor.Tick + deltaTick, Pitch = anchor.Pitch + deltaPitch })
            return;

        foreach (var (index, origin) in _dragOrigins)
            EditTrack.EditableNotes[index] = origin with { Tick = origin.Tick + deltaTick, Pitch = origin.Pitch + deltaPitch };

        StartPreview(anchor.Pitch + deltaPitch);
        RaiseNotesEdited();
    }

    /// <summary>
    /// 掴んだノートの終端をカーソルに合わせ、選択中のノーツの長さを同じだけ変える。
    /// </summary>
    private void DragResize(Point pos)
    {
        if (EditTrack is null || !_dragOrigins.TryGetValue(_dragAnchor, out var anchor)) return;

        var deltaLength = SnapRound(XToTick(pos.X)) - anchor.EndTick;
        if (EditTrack.EditableNotes[_dragAnchor].Length == Math.Max(SnapTicks, anchor.Length + deltaLength)) return;

        foreach (var (index, origin) in _dragOrigins)
            EditTrack.EditableNotes[index] = origin with { Length = Math.Max(SnapTicks, origin.Length + deltaLength) };

        RaiseNotesEdited();
    }

    /// <summary>
    /// 矩形選択の範囲。どの向きにドラッグしても正の幅・高さにする。
    /// </summary>
    private Rect SelectionBox => new(
        new Point(Math.Min(_selectStart.X, _selectCurrent.X), Math.Min(_selectStart.Y, _selectCurrent.Y)),
        new Point(Math.Max(_selectStart.X, _selectCurrent.X), Math.Max(_selectStart.Y, _selectCurrent.Y)));

    private void UpdateBoxSelection()
    {
        if (EditTrack is null) return;

        var box = SelectionBox;
        _selection.Clear();
        _selection.UnionWith(_selectionBeforeBox);
        for (var i = 0; i < EditTrack.EditableNotes.Count; i++)
        {
            if (NoteRect(EditTrack.EditableNotes[i]).Intersects(box)) _selection.Add(i);
        }
        InvalidateVisual();
    }

    private void UpdateHoverCursor(Point pos)
    {
        if (IsOverDrumSectionEdge(pos) || IsOverMovableConductorMarker(pos))
        {
            Cursor = ResizeCursor;
            return;
        }

        if (SplitterRect.Contains(pos))
        {
            Cursor = SplitterCursor;
            return;
        }

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
        if (EditTrack is null || !EditArea.Contains(pos)) return -1;

        for (var i = EditTrack.EditableNotes.Count - 1; i >= 0; i--)
        {
            var rect = NoteRect(EditTrack.EditableNotes[i]);
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

        if (PlatformKeys.IsCommand(e.KeyModifiers))
        {
            // Ctrl（macOS は ⌘）+ホイール：カーソル位置を基準に横ズーム
            var anchorTick = XToTick(pos.X);
            var factor = e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
            _pixelsPerBeat = Math.Clamp(_pixelsPerBeat * factor, 8, 600);
            HorizontalOffset = anchorTick * PixelsPerTick - (pos.X - KeyboardWidth);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            HorizontalOffset -= e.Delta.Y * 80;
        }
        else
        {
            HorizontalOffset -= e.Delta.X * 80;
            VerticalOffset -= e.Delta.Y * NoteHeight * 3;
        }

        UpdateScrollExtents();
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateScrollExtents();
    }

    /// <summary>
    /// スクロールバー用の最大値・表示幅を更新する。横は「最後のノート＋余白の小節」までスクロールできる。
    /// </summary>
    private void UpdateScrollExtents()
    {
        var area = EditArea;
        var lastTick = Song?.Tracks.OfType<NoteTrack>().SelectMany(t => t.Notes).Select(n => n.EndTick)
            .DefaultIfEmpty(0).Max() ?? 0;
        var contentTicks = lastTick + Conductor.TicksPerBarAt(lastTick) * TrailingBars;

        HorizontalViewport = area.Width;
        VerticalViewport = area.Height;
        HorizontalMaximum = Math.Max(0, contentTicks * PixelsPerTick - area.Width);
        VerticalMaximum = Math.Max(0, PitchCount * NoteHeight - area.Height);
        ClampOffsets();
        InvalidateVisual();
    }

    private void ClampOffsets()
    {
        // ズーム直後などは横の最大値より先にいることがあるので、横は最大値で切らず 0 未満だけ防ぐ
        if (HorizontalOffset < 0) HorizontalOffset = 0;
        if (VerticalOffset < 0) VerticalOffset = 0;
        else if (VerticalMaximum > 0 && VerticalOffset > VerticalMaximum) VerticalOffset = VerticalMaximum;
    }

    #endregion

    #region キーボード・クリップボード

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (EditTrack is null || _dragMode != DragMode.None) return;

        var ctrl = PlatformKeys.IsCommand(e.KeyModifiers);
        switch (e.Key)
        {
            case AvaloniaKey.Delete or AvaloniaKey.Back:
                EditStarted?.Invoke(this, EventArgs.Empty);
                DeleteNotes(_selection.ToList());
                EditCompleted?.Invoke(this, EventArgs.Empty);
                break;
            case AvaloniaKey.Escape:
                _selection.Clear();
                InvalidateVisual();
                break;
            case AvaloniaKey.A when ctrl:
                _selection.Clear();
                _selection.UnionWith(Enumerable.Range(0, EditTrack.EditableNotes.Count));
                InvalidateVisual();
                break;
            case AvaloniaKey.C when ctrl:
                CopySelection();
                break;
            case AvaloniaKey.X when ctrl:
                CopySelection();
                EditStarted?.Invoke(this, EventArgs.Empty);
                DeleteNotes(_selection.ToList());
                EditCompleted?.Invoke(this, EventArgs.Empty);
                break;
            case AvaloniaKey.V when ctrl:
                EditStarted?.Invoke(this, EventArgs.Empty);
                Paste();
                EditCompleted?.Invoke(this, EventArgs.Empty);
                UpdateScrollExtents();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void CopySelection()
    {
        if (EditTrack is null || _selection.Count == 0) return;

        var notes = _selection.Select(i => EditTrack.EditableNotes[i]).ToList();
        var origin = notes.Min(n => n.Tick);
        _clipboard = notes.Select(n => n with { Tick = n.Tick - origin }).OrderBy(n => n.Tick).ToList();
    }

    /// <summary>
    /// コピーしたノーツを再生開始位置に貼り付け、貼り付けたノーツを選択状態にする。
    /// </summary>
    private void Paste()
    {
        if (EditTrack is null || _clipboard.Count == 0) return;

        _selection.Clear();
        foreach (var note in _clipboard)
        {
            EditTrack.EditableNotes.Add(note with { Tick = note.Tick + PlayStartTick });
            _selection.Add(EditTrack.EditableNotes.Count - 1);
        }
        ResolveOverlaps();
        RaiseNotesEdited();
    }

    /// <summary>
    /// 選択中（今編集した）ノーツと重なる、同じ音高の他のノーツを削る。選択はそのまま保つ。
    /// </summary>
    private void ResolveOverlaps()
    {
        if (EditTrack is null || _selection.Count == 0) return;

        var notes = EditTrack.EditableNotes;
        var before = notes.ToList();
        var edited = NoteOverlap.Resolve(notes, _selection);
        _selection.Clear();
        _selection.UnionWith(edited);
        if (!before.SequenceEqual(notes)) RaiseNotesEdited();
    }

    /// <summary>
    /// 指定インデックスのノーツを削除し、選択を解除する。
    /// </summary>
    private void DeleteNotes(IReadOnlyCollection<int> indices)
    {
        if (EditTrack is null || indices.Count == 0) return;

        foreach (var index in indices.OrderDescending())
            EditTrack.EditableNotes.RemoveAt(index);

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
    /// 確認音を1音鳴らす。同じ音高が鳴っていれば何もしない。
    /// </summary>
    private void StartPreview(int pitch) => StartPreview([(pitch, DefaultVelocity)]);

    /// <summary>
    /// 確認音（和音も可）を鳴らす。同じ組み合わせが鳴っていれば何もしない。
    /// </summary>
    private void StartPreview((int Pitch, int Velocity)[] notes)
    {
        if (notes.AsSpan().SequenceEqual(_previewNotes)) return;
        _previewNotes = notes;
        NotePreview?.Invoke(this, new NotePreviewEventArgs(notes));
    }

    private void StopPreview()
    {
        if (_previewNotes.Length == 0) return;
        _previewNotes = [];
        NotePreview?.Invoke(this, new NotePreviewEventArgs([]));
    }

    #endregion
}

/// <summary>
/// 確認音の発音・停止の通知。Notes が空なら停止。
/// </summary>
public sealed class NotePreviewEventArgs(IReadOnlyList<(int Pitch, int Velocity)> notes) : EventArgs
{
    public IReadOnlyList<(int Pitch, int Velocity)> Notes { get; } = notes;
}

/// <summary>
/// コードレーンでクリックされたコード区間。
/// </summary>
public sealed class ChordSegmentClickedEventArgs(HanautaSketch.Engine.Smart.Chords.ChordSegment segment) : EventArgs
{
    public HanautaSketch.Engine.Smart.Chords.ChordSegment Segment { get; } = segment;
}
