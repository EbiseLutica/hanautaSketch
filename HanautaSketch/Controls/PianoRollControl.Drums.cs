using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using HanautaSketch.Engine.Smart.Drums;
using HanautaSketch.Resources;

namespace HanautaSketch.Controls;

/// <summary>
/// スマートドラムトラック表示中の、セクションレーン（コードレーンの位置）と生成ノーツの描画。
/// レーンを小節単位でドラッグするとセクションの作成を依頼し、クリックするとそのセクションを選択する。
/// </summary>
public sealed partial class PianoRollControl
{
    public static readonly StyledProperty<IReadOnlyList<DrumSection>?> DrumSectionsProperty =
        AvaloniaProperty.Register<PianoRollControl, IReadOnlyList<DrumSection>?>(nameof(DrumSections));

    public static readonly StyledProperty<DrumSection?> SelectedDrumSectionProperty =
        AvaloniaProperty.Register<PianoRollControl, DrumSection?>(nameof(SelectedDrumSection),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>この距離（px）以上動かしたら、クリックではなく範囲のドラッグとみなす</summary>
    private const double SectionDragThreshold = 4;

    /// <summary>セクションの端とみなす幅（px）</summary>
    private const double SectionEdgeWidth = 5;

    private double _sectionDragStartX;
    private double _sectionDragCurrentX;

    /// <summary>端のドラッグで伸縮中のセクションと、動かしている端（true なら右端）</summary>
    private (DrumSection Section, bool IsEnd)? _sectionResize;

    /// <summary>表示中のドラムトラックのセクション</summary>
    public IReadOnlyList<DrumSection>? DrumSections
    {
        get => GetValue(DrumSectionsProperty);
        set => SetValue(DrumSectionsProperty, value);
    }

    /// <summary>選択中のセクション。設定パネルの対象になる</summary>
    public DrumSection? SelectedDrumSection
    {
        get => GetValue(SelectedDrumSectionProperty);
        set => SetValue(SelectedDrumSectionProperty, value);
    }

    /// <summary>セクションレーンのドラッグで、小節単位の範囲 [Start, End) のセクション作成を依頼する</summary>
    public event EventHandler<DrumSectionRequestedEventArgs>? DrumSectionRequested;

    /// <summary>セクションの端のドラッグで、小節単位の新しい範囲への伸縮を依頼する</summary>
    public event EventHandler<DrumSectionResizeRequestedEventArgs>? DrumSectionResizeRequested;

    #region 描画

    private void RenderDrumSectionLane(DrawingContext context, Rect area)
    {
        context.FillRectangle(Palette.ChordLane, new Rect(KeyboardWidth, ChordLaneTop, area.Width, ChordLaneHeight));

        foreach (var section in DrumSections ?? [])
        {
            var rect = LaneRect(section.Start, section.End);
            if (rect.Right < area.X || rect.X > area.Right) continue;

            var selected = section == SelectedDrumSection;
            context.DrawRectangle(section.IsFixed ? Palette.ChordOverride : Palette.ChordBlock,
                selected ? Palette.SelectionBorder : Palette.ChordBorder, rect, 3, 3);
            using (context.PushClip(rect))
            {
                DrawText(context, DescribeSection(section), new Point(rect.X + 4, rect.Y + 1), Palette.Text, 12);
            }
        }

        if (_dragMode == DragMode.DrumSection && IsSectionDrag)
        {
            var (start, end) = _sectionResize is { } resize ? ResizedRange(resize.Section, resize.IsEnd) : DraggedBarRange();
            context.DrawRectangle(Palette.SelectionFill, Palette.SelectionBorder, LaneRect(start, end));
        }
    }

    /// <summary>
    /// fix していない区間の生成ノーツ（読み取り専用）。fix した区間の実体ノーツは通常の編集対象として描く。
    /// </summary>
    private void RenderDrumGeneratedNotes(DrawingContext context, SmartDrumTrack drum)
    {
        foreach (var note in drum.PlaybackNotes)
        {
            if (drum.Sections.Any(s => s.IsFixed && s.Contains(note.Tick))) continue;
            var rect = NoteRect(note).Deflate(new Thickness(0, 0.5));
            context.DrawRectangle(Palette.GeneratedNote, Palette.GeneratedNoteBorder, rect, 2, 2);
        }
    }

    private Rect LaneRect(int start, int end) =>
        new Rect(TickToX(start), ChordLaneTop + 2, (end - start) * PixelsPerTick, ChordLaneHeight - 4).Deflate(new Thickness(1, 0));

    private static string DescribeSection(DrumSection section)
    {
        var pattern = section.Pattern switch
        {
            DrumPattern.FourOnTheFloor => Strings.DrumPatternFourOnFloor,
            DrumPattern.EightBeat => Strings.DrumPattern8Beat,
            DrumPattern.SixteenBeat => Strings.DrumPattern16Beat,
            _ => "?",
        };
        var parts = new List<string> { pattern };
        if (section.Swing) parts.Add(Strings.Format(Strings.DrumSwingAmount, (section.SwingAmount * 100).ToString("0")));
        if (section.BassFollow == DrumBassFollow.Fill) parts.Add(Strings.DrumFill);
        if (section.BassFollow == DrumBassFollow.Full) parts.Add(Strings.DrumBassFollow);
        if (section.IsFixed) parts.Add("fix");
        return string.Join(" / ", parts);
    }

    #endregion

    #region 入力

    private bool IsSectionDrag => Math.Abs(_sectionDragCurrentX - _sectionDragStartX) >= SectionDragThreshold;

    private void BeginDrumSectionInput(PointerPressedEventArgs e, Point pos)
    {
        _sectionDragStartX = _sectionDragCurrentX = pos.X;
        _sectionResize = HitSectionEdge(pos);
        BeginDrag(DragMode.DrumSection, e);
    }

    /// <summary>
    /// セクションの端にいるか。隣り合う区間の境目では、選択中のセクションを優先し、なければ左の区間の右端にする。
    /// </summary>
    private (DrumSection Section, bool IsEnd)? HitSectionEdge(Point pos)
    {
        if (Track is not SmartDrumTrack || pos.X < KeyboardWidth || pos.Y < ChordLaneTop || pos.Y >= HeaderHeight) return null;

        (DrumSection, bool)? hit = null;
        foreach (var section in DrumSections ?? [])
        {
            foreach (var (tick, isEnd) in new[] { (section.Start, false), (section.End, true) })
            {
                if (Math.Abs(TickToX(tick) - pos.X) > SectionEdgeWidth) continue;
                if (section == SelectedDrumSection) return (section, isEnd);
                hit ??= (section, isEnd);
            }
        }
        return hit;
    }

    private bool IsOverDrumSectionEdge(Point pos) => HitSectionEdge(pos) is not null;

    /// <summary>
    /// 端をドラッグした後の範囲。動かした端を最寄りの小節線に寄せ、最低1小節は残す。
    /// </summary>
    private (int Start, int End) ResizedRange(DrumSection section, bool isEnd)
    {
        var conductor = Conductor;
        var bar = conductor.RoundToBar(XToTick(_sectionDragCurrentX));
        return isEnd
            ? (section.Start, Math.Max(conductor.CeilToBar(section.Start + 1), bar))
            : (Math.Min(conductor.BarStartAt(section.End - 1), bar), section.End);
    }

    private void UpdateDrumSectionDrag(Point pos)
    {
        _sectionDragCurrentX = pos.X;
        InvalidateVisual();
    }

    /// <summary>
    /// ドラッグならその小節範囲のセクション作成を依頼し、クリックならその位置のセクションを選ぶ。
    /// </summary>
    private void CompleteDrumSectionInput()
    {
        if (_sectionResize is { } resize)
        {
            _sectionResize = null;
            if (!IsSectionDrag) return;

            var (newStart, newEnd) = ResizedRange(resize.Section, resize.IsEnd);
            if (newStart != resize.Section.Start || newEnd != resize.Section.End)
                DrumSectionResizeRequested?.Invoke(this,
                    new DrumSectionResizeRequestedEventArgs(resize.Section, newStart, newEnd));
            return;
        }

        if (IsSectionDrag)
        {
            var (start, end) = DraggedBarRange();
            DrumSectionRequested?.Invoke(this, new DrumSectionRequestedEventArgs(start, end));
            return;
        }

        var tick = XToTick(_sectionDragStartX);
        SelectedDrumSection = DrumSections?.FirstOrDefault(s => s.Contains((int)tick));
    }

    /// <summary>
    /// ドラッグした範囲を、両端を含む小節単位に広げる。
    /// </summary>
    private (int Start, int End) DraggedBarRange()
    {
        var a = Math.Max(0, XToTick(Math.Min(_sectionDragStartX, _sectionDragCurrentX)));
        var b = Math.Max(0, XToTick(Math.Max(_sectionDragStartX, _sectionDragCurrentX)));
        var start = Conductor.BarStartAt((int)a);
        var end = Conductor.CeilToBar((int)b + 1);
        return (start, end);
    }

    #endregion
}

/// <summary>
/// セクションの伸縮の依頼（小節単位の新しい範囲）。
/// </summary>
public sealed class DrumSectionResizeRequestedEventArgs(DrumSection section, int start, int end) : EventArgs
{
    public DrumSection Section { get; } = section;

    public int Start { get; } = start;

    public int End { get; } = end;
}

/// <summary>
/// セクション作成の依頼（小節単位の範囲）。
/// </summary>
public sealed class DrumSectionRequestedEventArgs(int start, int end) : EventArgs
{
    public int Start { get; } = start;

    public int End { get; } = end;
}
