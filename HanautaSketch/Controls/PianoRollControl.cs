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

    public static readonly StyledProperty<int> ConductorRevisionProperty =
        AvaloniaProperty.Register<PianoRollControl, int>(nameof(ConductorRevision));

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
        AvaloniaProperty.Register<PianoRollControl, double>(nameof(VerticalOffset), (PitchCount - 1 - 84) * DefaultNoteHeight,
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
    private const double DefaultNoteHeight = 12;
    private const double MinNoteHeight = 4;
    private const double MaxNoteHeight = 40;
    private const int PitchCount = 128;

    /// <summary>最後のノートの後ろに確保するスクロール余白（小節数）</summary>
    private const int TrailingBars = 8;

    private PianoRollPalette? _palette;

    /// <summary>今のテーマの配色。テーマかリソースが変わるまで使い回す</summary>
    private PianoRollPalette Palette => _palette ??= PianoRollPalette.Load(this, ActualThemeVariant);
    private double _pixelsPerBeat = 80;

    /// <summary>1音の行の高さ（px）。Ctrl+Shift+ホイールで縦ズームする</summary>
    private double _noteHeight = DefaultNoteHeight;

    /// <summary>再生ヘッドに合わせて横スクロールするか。手動で横にスクロールすると止まり、次の再生開始で再開する</summary>
    private bool _followPlayhead;

    /// <summary>追従による横スクロール中かどうか。中は手動操作とみなさない</summary>
    private bool _autoScrolling;

    static PianoRollControl()
    {
        AffectsRender<PianoRollControl>(SongProperty, TrackProperty, ConductorRevisionProperty, SnapTicksProperty, ChordsProperty,
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
        // 配色はテーマのリソースから読む。テーマやリソースが変わったら読み直す
        ActualThemeVariantChanged += (_, _) => InvalidatePalette();
        ResourcesChanged += (_, _) => InvalidatePalette();
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

    /// <summary>テンポ・拍子・キーの変化を外部で変えたときに増やす。描き直す</summary>
    public int ConductorRevision
    {
        get => GetValue(ConductorRevisionProperty);
        set => SetValue(ConductorRevisionProperty, value);
    }

    public int SnapTicks
    {
        get => GetValue(SnapTicksProperty);
        set => SetValue(SnapTicksProperty, value);
    }

    private int Ppq => Song?.Ppq ?? Song.DefaultPpq;

    private static readonly Conductor FallbackConductor = new();

    /// <summary>表示中の曲のテンポ・拍子・キー</summary>
    private Conductor Conductor => Song?.Conductor ?? FallbackConductor;

    /// <summary>コードレーン（ドラムではセクションレーン）の上端。展開したコンダクターレーンの下</summary>
    private double ChordLaneTop => RulerHeight + ConductorHeight;

    /// <summary>ルーラー・コンダクターレーン・コードレーンを合わせた、ノート編集領域より上の高さ</summary>
    private double HeaderHeight => ChordLaneTop + ChordLaneHeight;

    private double PixelsPerTick => _pixelsPerBeat / Ppq;

    /// <summary>ノート編集領域。下にベロシティレーン（と境界）を空ける</summary>
    private Rect EditArea => new(KeyboardWidth, HeaderHeight,
        Math.Max(0, Bounds.Width - KeyboardWidth),
        Math.Max(0, Bounds.Height - HeaderHeight - VelocityLaneHeight - SplitterHeight));

    #region 再生追従

    /// <summary>
    /// 再生ヘッドの移動に合わせて横スクロールする。開始時は画面外なら表示し、再生中は中央を超えたら中央に保つ。
    /// </summary>
    private void OnPlayheadMoved(double oldTick, double newTick)
    {
        if (newTick < 0)
        {
            _followPlayhead = false;
            return;
        }

        if (oldTick < 0) _followPlayhead = true;
        if (!_followPlayhead) return;

        var viewCenter = EditArea.Width / 2;
        var headX = newTick * PixelsPerTick - HorizontalOffset;
        if (headX >= 0 && headX < viewCenter) return;

        ScrollHorizontalTo(newTick * PixelsPerTick - viewCenter);
    }

    /// <summary>追従による横スクロール。手動操作として扱わないよう印を付けて設定する</summary>
    private void ScrollHorizontalTo(double offset)
    {
        _autoScrolling = true;
        try
        {
            HorizontalOffset = Math.Clamp(offset, 0, HorizontalMaximum);
        }
        finally
        {
            _autoScrolling = false;
        }
    }

    #endregion

    #region 座標変換

    private double TickToX(double tick) => KeyboardWidth + tick * PixelsPerTick - HorizontalOffset;

    private double XToTick(double x) => (x - KeyboardWidth + HorizontalOffset) / PixelsPerTick;

    private double PitchToY(int pitch) => HeaderHeight + (PitchCount - 1 - pitch) * _noteHeight - VerticalOffset;

    private int YToPitch(double y) => PitchCount - 1 - (int)Math.Floor((y - HeaderHeight + VerticalOffset) / _noteHeight);

    private Rect NoteRect(Note note) =>
        new(TickToX(note.Tick), PitchToY(note.Pitch), Math.Max(2, note.Length * PixelsPerTick), _noteHeight);

    private int SnapFloor(double tick) => (int)Math.Floor(tick / SnapTicks) * SnapTicks;

    private int SnapRound(double tick) => (int)Math.Round(tick / SnapTicks) * SnapTicks;

    #endregion

    #region 描画

    private void InvalidatePalette()
    {
        _palette = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Palette.Background, bounds);

        var area = EditArea;
        using (context.PushClip(area))
        {
            RenderRows(context, area);
            RenderGridLines(context, area);
            RenderGhostNotes(context);
            RenderNotes(context);
            RenderSelectionBox(context);

            var startX = Math.Round(TickToX(PlayStartTick)) + 0.5;
            context.DrawLine(Palette.PlayStartLine, new Point(startX, area.Y), new Point(startX, area.Bottom));
        }

        if (PlayheadTick >= 0)
        {
            var x = Math.Round(TickToX(PlayheadTick)) + 0.5;
            if (x >= area.X && x <= area.Right)
                context.DrawLine(Palette.Playhead, new Point(x, RulerHeight), new Point(x, area.Bottom));
        }

        using (context.PushClip(new Rect(0, HeaderHeight, KeyboardWidth, area.Height)))
        {
            RenderKeyboard(context);
        }

        using (context.PushClip(new Rect(KeyboardWidth, 0, area.Width, RulerHeight)))
        {
            RenderRuler(context, area);
            RenderConductorIndicators(context);
            RenderPlayStartMarker(context);
        }

        using (context.PushClip(new Rect(KeyboardWidth, RulerHeight, area.Width, ConductorHeight)))
        {
            RenderConductorLanes(context, area);
        }

        using (context.PushClip(new Rect(KeyboardWidth, ChordLaneTop, area.Width, ChordLaneHeight)))
        {
            if (Track is SmartDrumTrack)
                RenderDrumSectionLane(context, area);
            else
                RenderChordLane(context, area);
        }

        context.FillRectangle(Palette.Ruler, new Rect(0, 0, KeyboardWidth, HeaderHeight));
        RenderConductorHeader(context);

        RenderVelocityLane(context);
    }

    /// <summary>
    /// 行背景。キーの区間ごとに、スケール内の音を明るく、主音をさらに強調する。
    /// </summary>
    private void RenderRows(DrawingContext context, Rect area)
    {
        var (top, bottom) = VisiblePitchRange(area);
        var visibleEnd = (int)Math.Ceiling(XToTick(area.Right)) + 1;
        foreach (var (start, end, key) in Conductor.KeyRegions(visibleEnd))
        {
            var left = Math.Max(area.X, TickToX(start));
            var right = Math.Min(area.Right, TickToX(end));
            if (right <= left) continue;

            var scale = key.GetScalePitchClasses();
            for (var pitch = bottom; pitch <= top; pitch++)
            {
                var pc = pitch % 12;
                var row = new Rect(left, PitchToY(pitch), right - left, _noteHeight);
                if (pc == key.Tonic)
                    context.FillRectangle(Palette.TonicRow, row);
                else if (scale.Contains(pc))
                    context.FillRectangle(Palette.InScaleRow, row);
            }
        }

        for (var pitch = bottom; pitch <= top; pitch++)
        {
            var y = PitchToY(pitch) + _noteHeight;
            context.DrawLine(Palette.RowLine, new Point(area.X, y), new Point(area.Right, y));
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
            if (Conductor.PositionInBar(tick) == 0) pen = Palette.BarLine;
            else if (tick % Ppq == 0) pen = Palette.BeatLine;
            else if (drawSnapLines) pen = Palette.SnapLine;
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
                context.FillRectangle(Palette.GhostNote, NoteRect(note));
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
                context.DrawRectangle(selected ? Palette.NoteSelected : Palette.Note,
                    selected ? Palette.NoteSelectedBorder : Palette.NoteBorder, rect, 2, 2);
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
            context.DrawRectangle(Palette.GeneratedNote, Palette.GeneratedNoteBorder, rect, 2, 2);
        }
    }

    /// <summary>
    /// Ctrl+ドラッグ中の矩形選択範囲。
    /// </summary>
    private void RenderSelectionBox(DrawingContext context)
    {
        if (_dragMode != DragMode.Select) return;
        context.DrawRectangle(Palette.SelectionFill, Palette.SelectionBorder, SelectionBox);
    }

    /// <summary>
    /// ルーラー上の再生開始位置マーカー（下向きの三角）。
    /// </summary>
    private void RenderPlayStartMarker(DrawingContext context)
    {
        var x = TickToX(PlayStartTick);
        var geometry = new PolylineGeometry([new Point(x - 6, 2), new Point(x + 6, 2), new Point(x, RulerHeight - 4)], true);
        context.DrawGeometry(Palette.PlayStartMarker, null, geometry);
    }

    private void RenderKeyboard(DrawingContext context)
    {
        var area = EditArea;
        var (top, bottom) = VisiblePitchRange(area);
        for (var pitch = bottom; pitch <= top; pitch++)
        {
            var y = PitchToY(pitch);
            var isBlack = IsBlackKey(pitch);
            var rect = new Rect(0, y, KeyboardWidth, _noteHeight);
            context.FillRectangle(isBlack ? Palette.BlackKey : Palette.WhiteKey, rect);
            context.DrawLine(Palette.KeySeparator, new Point(0, y + _noteHeight), new Point(KeyboardWidth, y + _noteHeight));

            if (pitch % 12 == 0)
                DrawText(context, $"C{pitch / 12 - 1}", new Point(KeyboardWidth - 22, y), Palette.KeyLabel, 9);
        }
    }

    private void RenderRuler(DrawingContext context, Rect area)
    {
        context.FillRectangle(Palette.Ruler, new Rect(KeyboardWidth, 0, area.Width, RulerHeight));
        var conductor = Conductor;
        var startBar = conductor.BarAt((int)Math.Max(0, XToTick(area.X)));
        var endTick = XToTick(area.Right);
        for (var bar = startBar; conductor.BarToTick(bar) <= endTick; bar++)
        {
            var x = Math.Round(TickToX(conductor.BarToTick(bar))) + 0.5;
            context.DrawLine(Palette.BarLine, new Point(x, 4), new Point(x, RulerHeight));
            DrawText(context, (bar + 1).ToString(CultureInfo.InvariantCulture), new Point(x + 4, 3), Palette.Text, 11);
        }
    }

    /// <summary>
    /// 判別されたコードを区間ごとに表示する。
    /// </summary>
    private void RenderChordLane(DrawingContext context, Rect area)
    {
        context.FillRectangle(Palette.ChordLane, new Rect(KeyboardWidth, ChordLaneTop, area.Width, ChordLaneHeight));

        foreach (var block in GetChordBlocks())
        {
            var rect = ChordBlockRect(block);
            if (rect.Right < area.X || rect.X > area.Right) continue;

            context.DrawRectangle(block.IsOverride ? Palette.ChordOverride : Palette.ChordBlock,
                Palette.ChordBorder, rect, 3, 3);
            using (context.PushClip(rect))
            {
                DrawText(context, block.GetName(Conductor.KeyAt(block.Start)), new Point(rect.X + 4, rect.Y + 1), Palette.Text, 12);
            }
        }
    }

    /// <summary>
    /// 表示上のコードブロック。判別結果の区間をそのまま使う（ベースの休みで分けた区間を別々に指定できるように）。
    /// </summary>
    private IReadOnlyList<ChordSegment> GetChordBlocks() => Chords ?? [];

    private Rect ChordBlockRect(ChordSegment block) =>
        new Rect(TickToX(block.Start), ChordLaneTop + 2, block.Length * PixelsPerTick, ChordLaneHeight - 4)
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
