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
    };

    public static PianoRollPalette For(ThemeVariant variant) => variant == ThemeVariant.Dark ? Dark : Light;

    private static ImmutableSolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static ImmutablePen Pen(string color) => new(Brush(color), 1);
}
