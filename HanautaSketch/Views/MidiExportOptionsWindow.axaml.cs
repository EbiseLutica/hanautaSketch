using Avalonia.Controls;
using Avalonia.Interactivity;
using HanautaSketch.Engine.IO;

namespace HanautaSketch.Views;

/// <summary>
/// 保存先を選んだあとに出す、MIDI 書き出しのオプション画面。
/// 「書き出す」で選んだオプション、キャンセルで null を返す。
/// </summary>
public partial class MidiExportOptionsWindow : Window
{
    public MidiExportOptionsWindow()
    {
        InitializeComponent();
    }

    public MidiExportOptionsWindow(MidiExportOptions initial, string fileName) : this()
    {
        FileNameText.Text = fileName;
        ProgramChangeCheck.IsChecked = initial.IncludeProgramChange;
        ControlChangeCheck.IsChecked = initial.IncludeControlChange;
        ChordMarkersCheck.IsChecked = initial.IncludeChordMarkers;
        KeySignatureCheck.IsChecked = initial.IncludeKeySignature;
    }

    private void OnExportClick(object? sender, RoutedEventArgs e) => Close(new MidiExportOptions
    {
        IncludeProgramChange = ProgramChangeCheck.IsChecked == true,
        IncludeControlChange = ControlChangeCheck.IsChecked == true,
        IncludeChordMarkers = ChordMarkersCheck.IsChecked == true,
        IncludeKeySignature = KeySignatureCheck.IsChecked == true,
    });

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
