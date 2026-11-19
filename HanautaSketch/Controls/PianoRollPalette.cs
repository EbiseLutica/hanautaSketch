using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace HanautaSketch.Controls;

/// <summary>
/// ピアノロールの配色。色の値は持たず、テーマ（Themes/LuticaTheme.axaml）の PianoRoll*Brush から読む。
/// ペンの太さなど描き方に関わる値だけをここで決める。
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
    public required IBrush RecordingLevel { get; init; }

    /// <summary>
    /// テーマ辞書の <c>PianoRoll{名前}Brush</c> から、指定したテーマバリアントの配色を組み立てる。
    /// 見つからないキーは透明になる（Debug ビルドでは assert）。
    /// </summary>
    public static PianoRollPalette Load(IResourceHost host, ThemeVariant variant)
    {
        IImmutableBrush B(string name)
        {
            var key = $"PianoRoll{name}Brush";
            if (host.TryFindResource(key, variant, out var value) && value is IBrush brush)
                return brush.ToImmutable();
            Debug.Fail($"ピアノロールの配色 {key} がテーマにない");
            return Brushes.Transparent.ToImmutable();
        }

        ImmutablePen P(string name, double thickness = 1) => new(B(name), thickness);

        return new PianoRollPalette
        {
            Background = B(nameof(Background)),
            InScaleRow = B(nameof(InScaleRow)),
            TonicRow = B(nameof(TonicRow)),
            WhiteKey = B(nameof(WhiteKey)),
            BlackKey = B(nameof(BlackKey)),
            Ruler = B(nameof(Ruler)),
            Text = B(nameof(Text)),
            KeyLabel = B(nameof(KeyLabel)),
            Note = B(nameof(Note)),
            GhostNote = B(nameof(GhostNote)),
            NoteBorder = P(nameof(NoteBorder)),
            SnapLine = P(nameof(SnapLine)),
            BeatLine = P(nameof(BeatLine)),
            BarLine = P(nameof(BarLine)),
            RowLine = P(nameof(RowLine)),
            KeySeparator = P(nameof(KeySeparator)),
            ChordLane = B(nameof(ChordLane)),
            ChordBlock = B(nameof(ChordBlock)),
            ChordOverride = B(nameof(ChordOverride)),
            ChordBorder = P(nameof(ChordBorder)),
            Playhead = P(nameof(Playhead), 2),
            NoteSelected = B(nameof(NoteSelected)),
            NoteSelectedBorder = P(nameof(NoteSelectedBorder)),
            SelectionFill = B(nameof(SelectionFill)),
            SelectionBorder = P(nameof(SelectionBorder)),
            PlayStartLine = P(nameof(PlayStartLine)),
            PlayStartMarker = B(nameof(PlayStartMarker)),
            GeneratedNote = B(nameof(GeneratedNote)),
            GeneratedNoteBorder = P(nameof(GeneratedNoteBorder)),
            ConductorLane = B(nameof(ConductorLane)),
            MeterMarker = B(nameof(MeterMarker)),
            TempoMarker = B(nameof(TempoMarker)),
            KeyMarker = B(nameof(KeyMarker)),
            RecordingLevel = B(nameof(RecordingLevel)),
        };
    }
}
