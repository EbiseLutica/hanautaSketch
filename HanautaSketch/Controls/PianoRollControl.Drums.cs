using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using HanautaSketch.Engine.Smart.Drums;

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

    private double _sectionDragStartX;
    private double _sectionDragCurrentX;

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

    #region 描画

    private void RenderDrumSectionLane(DrawingContext context, Rect area)
    {
        context.FillRectangle(_palette.ChordLane, new Rect(KeyboardWidth, RulerHeight, area.Width, ChordLaneHeight));

        foreach (var section in DrumSections ?? [])
        {
            var rect = LaneRect(section.Start, section.End);
            if (rect.Right < area.X || rect.X > area.Right) continue;

            var selected = section == SelectedDrumSection;
            context.DrawRectangle(section.IsFixed ? _palette.ChordOverride : _palette.ChordBlock,
                selected ? _palette.SelectionBorder : _palette.ChordBorder, rect, 3, 3);
            using (context.PushClip(rect))
            {
                DrawText(context, DescribeSection(section), new Point(rect.X + 4, rect.Y + 1), _palette.Text, 12);
            }
        }

        if (_dragMode == DragMode.DrumSection && IsSectionDrag)
        {
            var (start, end) = DraggedBarRange();
            context.DrawRectangle(_palette.SelectionFill, _palette.SelectionBorder, LaneRect(start, end));
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
            context.DrawRectangle(_palette.GeneratedNote, _palette.GeneratedNoteBorder, rect, 2, 2);
        }
    }

    private Rect LaneRect(int start, int end) =>
        new Rect(TickToX(start), RulerHeight + 2, (end - start) * PixelsPerTick, ChordLaneHeight - 4).Deflate(new Thickness(1, 0));

    private static string DescribeSection(DrumSection section)
    {
        var pattern = section.Pattern switch
        {
            DrumPattern.FourOnTheFloor => "4つ打ち",
            DrumPattern.EightBeat => "8ビート",
            DrumPattern.SixteenBeat => "16ビート",
            _ => "?",
        };
        var parts = new List<string> { pattern };
        if (section.Swing) parts.Add($"Swing {section.SwingAmount * 100:0}%");
        if (section.FollowBass) parts.Add("ベース追従");
        if (section.IsFixed) parts.Add("fix");
        return string.Join(" / ", parts);
    }

    #endregion

    #region 入力

    private bool IsSectionDrag => Math.Abs(_sectionDragCurrentX - _sectionDragStartX) >= SectionDragThreshold;

    private void BeginDrumSectionInput(PointerPressedEventArgs e, Point pos)
    {
        _sectionDragStartX = _sectionDragCurrentX = pos.X;
        BeginDrag(DragMode.DrumSection, e);
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
        var start = (int)Math.Floor(a / TicksPerBar) * TicksPerBar;
        var end = ((int)Math.Floor(b / TicksPerBar) + 1) * TicksPerBar;
        return (start, end);
    }

    #endregion
}

/// <summary>
/// セクション作成の依頼（小節単位の範囲）。
/// </summary>
public sealed class DrumSectionRequestedEventArgs(int start, int end) : EventArgs
{
    public int Start { get; } = start;

    public int End { get; } = end;
}
