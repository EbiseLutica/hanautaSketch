using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace HanautaSketch.Controls;

/// <summary>
/// ピアノロールの配色。テーマバリアントごとに1セット持つ。
/// 面・罫線・文字は Lutica Design System の色（background / panel / panel-button / divider / border / foreground / dimmed）に寄せ、
/// 再生位置は primary。ノーツなど意味を持つ色は区別しやすさを優先して別に持つ。
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
        Background = Brush("#F1E2E1"),
        InScaleRow = Brush("#FFFAFA"),
        TonicRow = Brush("#FFE5E4"),
        WhiteKey = Brush("#FFFAFA"),
        BlackKey = Brush("#393535"),
        Ruler = Brush("#F8EEED"),
        Text = Brush("#393535"),
        KeyLabel = Brush("#495A6B"),
        Note = Brush("#4F7BFF"),
        GhostNote = Brush("#38505A70"),
        NoteBorder = Pen("#2747B0"),
        SnapLine = Pen("#EEDEDD"),
        BeatLine = Pen("#DCBCBC"),
        BarLine = Pen("#9A7A7B"),
        RowLine = Pen("#EFE1E0"),
        KeySeparator = Pen("#DCBCBC"),
        ChordLane = Brush("#F8EEED"),
        ChordBlock = Brush("#FFFAFA"),
        ChordOverride = Brush("#FFE9B8"),
        ChordBorder = Pen("#DCBCBC"),
        Playhead = new ImmutablePen(Brush("#D30E17"), 2),
        NoteSelected = Brush("#FF9F43"),
        NoteSelectedBorder = Pen("#B35F00"),
        SelectionFill = Brush("#264F7BFF"),
        SelectionBorder = Pen("#4F7BFF"),
        PlayStartLine = Pen("#5530A46C"),
        PlayStartMarker = Brush("#30A46C"),
        GeneratedNote = Brush("#9BD7B5"),
        GeneratedNoteBorder = Pen("#3D8B60"),
        ConductorLane = Brush("#F8EEED"),
        MeterMarker = Brush("#8E5BD6"),
        TempoMarker = Brush("#D9622B"),
        KeyMarker = Brush("#2F8FB8"),
    };

    public static PianoRollPalette Dark { get; } = new()
    {
        Background = Brush("#12121F"),
        InScaleRow = Brush("#1B1B2C"),
        TonicRow = Brush("#26243A"),
        WhiteKey = Brush("#E1E1E1"),
        BlackKey = Brush("#22223A"),
        Ruler = Brush("#161625"),
        Text = Brush("#C9BABA"),
        KeyLabel = Brush("#6B6580"),
        Note = Brush("#7C9CFF"),
        GhostNote = Brush("#40FFFFFF"),
        NoteBorder = Pen("#2C3E80"),
        SnapLine = Pen("#1A1A2B"),
        BeatLine = Pen("#262638"),
        BarLine = Pen("#3D3A52"),
        RowLine = Pen("#151524"),
        KeySeparator = Pen("#6B6580"),
        ChordLane = Brush("#161625"),
        ChordBlock = Brush("#2E2E45"),
        ChordOverride = Brush("#5A4A22"),
        ChordBorder = Pen("#3D3A52"),
        Playhead = new ImmutablePen(Brush("#F24149"), 2),
        NoteSelected = Brush("#FFB86B"),
        NoteSelectedBorder = Pen("#8A4B00"),
        SelectionFill = Brush("#337C9CFF"),
        SelectionBorder = Pen("#7C9CFF"),
        PlayStartLine = Pen("#664CC38A"),
        PlayStartMarker = Brush("#4CC38A"),
        GeneratedNote = Brush("#3E7A5A"),
        GeneratedNoteBorder = Pen("#6FD3A0"),
        ConductorLane = Brush("#161625"),
        MeterMarker = Brush("#B38AF0"),
        TempoMarker = Brush("#F08A5B"),
        KeyMarker = Brush("#5CB8E0"),
    };

    public static PianoRollPalette For(ThemeVariant variant) => variant == ThemeVariant.Dark ? Dark : Light;

    private static ImmutableSolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static ImmutablePen Pen(string color) => new(Brush(color), 1);
}
