using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Drums;
using Key = HanautaSketch.Engine.Core.Key;

namespace HanautaSketch.Controls;

/// <summary>
/// 選択中トラックのノーツを編集するピアノロール。
/// 左に鍵盤、上にルーラーを持ち、残りがノート編集領域。
/// </summary>
public sealed partial class PianoRollControl : Control
{
    public static readonly StyledProperty<Song?> SongProperty =
        AvaloniaProperty.Register<PianoRollControl, Song?>(nameof(Song));

    public static readonly StyledProperty<Track?> TrackProperty =
        AvaloniaProperty.Register<PianoRollControl, Track?>(nameof(Track));

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

    public static readonly StyledProperty<int> PlayStartTickProperty =
        AvaloniaProperty.Register<PianoRollControl, int>(nameof(PlayStartTick),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> NotesRevisionProperty =
        AvaloniaProperty.Register<PianoRollControl, int>(nameof(NotesRevision));

    public static readonly StyledProperty<double> HorizontalOffsetProperty =
        AvaloniaProperty.Register<PianoRollControl, double>(nameof(HorizontalOffset),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> VerticalOffsetProperty =
        AvaloniaProperty.Register<PianoRollControl, double>(nameof(VerticalOffset), (PitchCount - 1 - 84) * NoteHeight,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> HorizontalMaximumProperty =
        AvaloniaProperty.Register<PianoRollControl, double>(nameof(HorizontalMaximum));

    public static readonly StyledProperty<double> VerticalMaximumProperty =
        AvaloniaProperty.Register<PianoRollControl, double>(nameof(VerticalMaximum));

    public static readonly StyledProperty<double> HorizontalViewportProperty =
        AvaloniaProperty.Register<PianoRollControl, double>(nameof(HorizontalViewport));

    public static readonly StyledProperty<double> VerticalViewportProperty =
        AvaloniaProperty.Register<PianoRollControl, double>(nameof(VerticalViewport));

    private const double KeyboardWidth = 56;
    private const double RulerHeight = 22;
    private const double ChordLaneHeight = 22;
    private const double HeaderHeight = RulerHeight + ChordLaneHeight;
    private const double NoteHeight = 12;
    private const int PitchCount = 128;

    /// <summary>最後のノートの後ろに確保するスクロール余白（小節数）</summary>
    private const int TrailingBars = 8;

    private PianoRollPalette _palette = PianoRollPalette.Light;
    private double _pixelsPerBeat = 80;

    static PianoRollControl()
    {
        AffectsRender<PianoRollControl>(SongProperty, TrackProperty, KeyProperty, SnapTicksProperty, ChordsProperty,
            PlayheadTickProperty, PlayStartTickProperty, NotesRevisionProperty, HorizontalOffsetProperty,
            VerticalOffsetProperty, DrumSectionsProperty, SelectedDrumSectionProperty);
        FocusableProperty.OverrideDefaultValue<PianoRollControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<PianoRollControl>(true);
    }

    /// <summary>再生ヘッドの位置（tick）。負なら非表示</summary>
    public double PlayheadTick
    {
        get => GetValue(PlayheadTickProperty);
        set => SetValue(PlayheadTickProperty, value);
    }

    /// <summary>再生開始位置（tick）。ルーラーのクリックで変わり、貼り付け先にもなる</summary>
    public int PlayStartTick
    {
        get => GetValue(PlayStartTickProperty);
        set => SetValue(PlayStartTickProperty, value);
    }

    /// <summary>
    /// 外部（Undo・読み込みなど）でノーツを差し替えたときに増やす。選択を解除して描き直す
    /// </summary>
    public int NotesRevision
    {
        get => GetValue(NotesRevisionProperty);
        set => SetValue(NotesRevisionProperty, value);
    }

    /// <summary>横スクロール位置（px）。スクロールバーと双方向に連動する</summary>
    public double HorizontalOffset
    {
        get => GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>縦スクロール位置（px）。スクロールバーと双方向に連動する</summary>
    public double VerticalOffset
    {
        get => GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>横スクロールの最大値（スクロールバー用、内部で更新する）</summary>
    public double HorizontalMaximum
    {
        get => GetValue(HorizontalMaximumProperty);
        private set => SetValue(HorizontalMaximumProperty, value);
    }

    /// <summary>縦スクロールの最大値（スクロールバー用、内部で更新する）</summary>
    public double VerticalMaximum
    {
        get => GetValue(VerticalMaximumProperty);
        private set => SetValue(VerticalMaximumProperty, value);
    }

    /// <summary>横方向の表示幅（スクロールバーのつまみの大きさ）</summary>
    public double HorizontalViewport
    {
        get => GetValue(HorizontalViewportProperty);
        private set => SetValue(HorizontalViewportProperty, value);
    }

    /// <summary>縦方向の表示高さ（スクロールバーのつまみの大きさ）</summary>
    public double VerticalViewport
    {
        get => GetValue(VerticalViewportProperty);
        private set => SetValue(VerticalViewportProperty, value);
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

    /// <summary>入力中の確認音を鳴らす・止めるときに発生する</summary>
    public event EventHandler<NotePreviewEventArgs>? NotePreview;

    /// <summary>一連の編集（ドラッグ・削除・貼り付けなど）を始める直前に発生する。Undo の区切りに使う</summary>
    public event EventHandler? EditStarted;

    /// <summary>一連の編集が終わったときに発生する</summary>
    public event EventHandler? EditCompleted;

    /// <summary>コードレーンのコードがクリックされたときに発生する</summary>
    public event EventHandler<ChordSegmentClickedEventArgs>? ChordSegmentClicked;

    /// <summary>編集できるノーツを持つトラック（ノートトラック、ドラムの fix 区間）。それ以外のスマートトラックでは null</summary>
    private INoteEditable? EditTrack => Track as INoteEditable;

    public PianoRollControl()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public Song? Song
    {
        get => GetValue(SongProperty);
        set => SetValue(SongProperty, value);
    }

    /// <summary>表示中のトラック。ノートトラックなら編集でき、スマートトラックなら生成結果を読み取り専用で表示する</summary>
    public Track? Track
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

    /// <summary>ノート編集領域。下にベロシティレーン（と境界）を空ける</summary>
    private Rect EditArea => new(KeyboardWidth, HeaderHeight,
        Math.Max(0, Bounds.Width - KeyboardWidth),
        Math.Max(0, Bounds.Height - HeaderHeight - VelocityLaneHeight - SplitterHeight));

    #region 座標変換

    private double TickToX(double tick) => KeyboardWidth + tick * PixelsPerTick - HorizontalOffset;

    private double XToTick(double x) => (x - KeyboardWidth + HorizontalOffset) / PixelsPerTick;

    private double PitchToY(int pitch) => HeaderHeight + (PitchCount - 1 - pitch) * NoteHeight - VerticalOffset;

    private int YToPitch(double y) => PitchCount - 1 - (int)Math.Floor((y - HeaderHeight + VerticalOffset) / NoteHeight);

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
            RenderSelectionBox(context);

            var startX = Math.Round(TickToX(PlayStartTick)) + 0.5;
            context.DrawLine(_palette.PlayStartLine, new Point(startX, area.Y), new Point(startX, area.Bottom));
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
            RenderPlayStartMarker(context);
        }

        using (context.PushClip(new Rect(KeyboardWidth, RulerHeight, area.Width, ChordLaneHeight)))
        {
            if (Track is SmartDrumTrack)
                RenderDrumSectionLane(context, area);
            else
                RenderChordLane(context, area);
        }

        context.FillRectangle(_palette.Ruler, new Rect(0, 0, KeyboardWidth, HeaderHeight));

        RenderVelocityLane(context);
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
    /// 表示中以外のノートトラックのノーツを薄く表示する。
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
        if (Track is SmartDrumTrack drum)
            RenderDrumGeneratedNotes(context, drum);

        if (Track is not INoteEditable track)
        {
            RenderGeneratedNotes(context);
            return;
        }

        var notes = track.EditableNotes;
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var rect = NoteRect(note).Deflate(new Thickness(0, 0.5));
            var selected = _selection.Contains(i);
            var opacity = 0.45 + 0.55 * note.Velocity / 127.0;
            using (context.PushOpacity(opacity))
            {
                context.DrawRectangle(selected ? _palette.NoteSelected : _palette.Note,
                    selected ? _palette.NoteSelectedBorder : _palette.NoteBorder, rect, 2, 2);
            }
        }
    }

    /// <summary>
    /// スマートトラックが生成したノーツ。編集できないことが分かるよう色を変える。
    /// </summary>
    private void RenderGeneratedNotes(DrawingContext context)
    {
        if (Track is null) return;
        foreach (var note in Track.PlaybackNotes)
        {
            var rect = NoteRect(note).Deflate(new Thickness(0, 0.5));
            context.DrawRectangle(_palette.GeneratedNote, _palette.GeneratedNoteBorder, rect, 2, 2);
        }
    }

    /// <summary>
    /// Ctrl+ドラッグ中の矩形選択範囲。
    /// </summary>
    private void RenderSelectionBox(DrawingContext context)
    {
        if (_dragMode != DragMode.Select) return;
        context.DrawRectangle(_palette.SelectionFill, _palette.SelectionBorder, new Rect(_selectStart, _selectCurrent));
    }

    /// <summary>
    /// ルーラー上の再生開始位置マーカー（下向きの三角）。
    /// </summary>
    private void RenderPlayStartMarker(DrawingContext context)
    {
        var x = TickToX(PlayStartTick);
        var geometry = new PolylineGeometry([new Point(x - 6, 2), new Point(x + 6, 2), new Point(x, RulerHeight - 4)], true);
        context.DrawGeometry(_palette.PlayStartMarker, null, geometry);
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
    /// 判別されたコードを区間ごとに表示する。
    /// </summary>
    private void RenderChordLane(DrawingContext context, Rect area)
    {
        context.FillRectangle(_palette.ChordLane, new Rect(KeyboardWidth, RulerHeight, area.Width, ChordLaneHeight));

        foreach (var block in GetChordBlocks())
        {
            var rect = ChordBlockRect(block);
            if (rect.Right < area.X || rect.X > area.Right) continue;

            context.DrawRectangle(block.IsOverride ? _palette.ChordOverride : _palette.ChordBlock,
                _palette.ChordBorder, rect, 3, 3);
            using (context.PushClip(rect))
            {
                DrawText(context, block.GetName(Key), new Point(rect.X + 4, rect.Y + 1), _palette.Text, 12);
            }
        }
    }

    /// <summary>
    /// 表示上のコードブロック。判別結果の区間をそのまま使う（ベースの休みで分けた区間を別々に指定できるように）。
    /// </summary>
    private IReadOnlyList<ChordSegment> GetChordBlocks() => Chords ?? [];

    private Rect ChordBlockRect(ChordSegment block) =>
        new Rect(TickToX(block.Start), RulerHeight + 2, block.Length * PixelsPerTick, ChordLaneHeight - 4)
            .Deflate(new Thickness(1, 0));

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
