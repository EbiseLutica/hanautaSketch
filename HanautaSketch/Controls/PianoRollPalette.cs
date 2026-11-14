using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace HanautaSketch.Controls;

/// <summary>
/// ピアノロールの配色。テーマバリアントごとに1セット持つ。
/// </summary>
public sealed class PianoRollPalette
{
    public required IBrush Background { get; init; }
    public required IBrush InScaleRow { get; init; }
    public required IBrush TonicRow { get; init; }
    public required IBrush WhiteKey { get; init; }
    public required IBrush BlackKey { get; init; }
    public required IBrush Ruler { get; init; }
    public required IBrush Text { get; init; }
    public required IBrush KeyLabel { get; init; }
    public required IBrush Note { get; init; }
    public required IBrush GhostNote { get; init; }
    public required IPen NoteBorder { get; init; }
    public required IPen SnapLine { get; init; }
    public required IPen BeatLine { get; init; }
    public required IPen BarLine { get; init; }
    public required IPen RowLine { get; init; }
    public required IPen KeySeparator { get; init; }
    public required IBrush ChordLane { get; init; }
    public required IBrush ChordBlock { get; init; }
    public required IBrush ChordOverride { get; init; }
    public required IPen ChordBorder { get; init; }
    public required IPen Playhead { get; init; }
    public required IBrush NoteSelected { get; init; }
    public required IPen NoteSelectedBorder { get; init; }
    public required IBrush SelectionFill { get; init; }
    public required IPen SelectionBorder { get; init; }
    public required IPen PlayStartLine { get; init; }
    public required IBrush PlayStartMarker { get; init; }
    public required IBrush GeneratedNote { get; init; }
    public required IPen GeneratedNoteBorder { get; init; }
    public required IBrush ConductorLane { get; init; }
    public required IBrush MeterMarker { get; init; }
    public required IBrush TempoMarker { get; init; }
    public required IBrush KeyMarker { get; init; }

    public static PianoRollPalette Light { get; } = new()
    {
        Background = Brush("#E3E5EA"),
        InScaleRow = Brush("#F8F9FB"),
        TonicRow = Brush("#E9E3FB"),
        WhiteKey = Brush("#FFFFFF"),
        BlackKey = Brush("#3A3B40"),
        Ruler = Brush("#EEEFF2"),
        Text = Brush("#4A4D57"),
        KeyLabel = Brush("#80838C"),
        Note = Brush("#4F7BFF"),
        GhostNote = Brush("#38505A70"),
        NoteBorder = Pen("#2747B0"),
        SnapLine = Pen("#D9DCE2"),
        BeatLine = Pen("#BCC0C9"),
        BarLine = Pen("#8C919C"),
        RowLine = Pen("#DCDEE3"),
        KeySeparator = Pen("#C0C2C8"),
        ChordLane = Brush("#E6E8EC"),
        ChordBlock = Brush("#FFFFFF"),
        ChordOverride = Brush("#FFE9B8"),
        ChordBorder = Pen("#B5B9C2"),
        Playhead = new ImmutablePen(Brush("#E5484D"), 2),
        NoteSelected = Brush("#FF9F43"),
        NoteSelectedBorder = Pen("#B35F00"),
        SelectionFill = Brush("#264F7BFF"),
        SelectionBorder = Pen("#4F7BFF"),
        PlayStartLine = Pen("#5530A46C"),
        PlayStartMarker = Brush("#30A46C"),
        GeneratedNote = Brush("#9BD7B5"),
        GeneratedNoteBorder = Pen("#3D8B60"),
        ConductorLane = Brush("#E9EBEF"),
        MeterMarker = Brush("#8E5BD6"),
        TempoMarker = Brush("#D9622B"),
        KeyMarker = Brush("#2F8FB8"),
    };

    public static PianoRollPalette Dark { get; } = new()
    {
        Background = Brush("#1E1F24"),
        InScaleRow = Brush("#2A2C33"),
        TonicRow = Brush("#33304A"),
        WhiteKey = Brush("#E8E8EC"),
        BlackKey = Brush("#2B2B30"),
        Ruler = Brush("#26272D"),
        Text = Brush("#B8BAC4"),
        KeyLabel = Brush("#55565E"),
        Note = Brush("#7C9CFF"),
        GhostNote = Brush("#40FFFFFF"),
        NoteBorder = Pen("#2C3E80"),
        SnapLine = Pen("#24262C"),
        BeatLine = Pen("#34363F"),
        BarLine = Pen("#4A4D59"),
        RowLine = Pen("#202126"),
        KeySeparator = Pen("#9A9AA2"),
        ChordLane = Brush("#222328"),
        ChordBlock = Brush("#30323A"),
        ChordOverride = Brush("#5A4A22"),
        ChordBorder = Pen("#4A4D59"),
        Playhead = new ImmutablePen(Brush("#FF6B6F"), 2),
        NoteSelected = Brush("#FFB86B"),
        NoteSelectedBorder = Pen("#8A4B00"),
        SelectionFill = Brush("#337C9CFF"),
        SelectionBorder = Pen("#7C9CFF"),
        PlayStartLine = Pen("#664CC38A"),
        PlayStartMarker = Brush("#4CC38A"),
        GeneratedNote = Brush("#3E7A5A"),
        GeneratedNoteBorder = Pen("#6FD3A0"),
        ConductorLane = Brush("#23242A"),
        MeterMarker = Brush("#B38AF0"),
        TempoMarker = Brush("#F08A5B"),
        KeyMarker = Brush("#5CB8E0"),
    };

    public static PianoRollPalette For(ThemeVariant variant) => variant == ThemeVariant.Dark ? Dark : Light;

    private static ImmutableSolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static ImmutablePen Pen(string color) => new(Brush(color), 1);
}
