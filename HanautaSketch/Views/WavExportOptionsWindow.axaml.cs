using Avalonia.Controls;
using Avalonia.Interactivity;
using HanautaSketch.Engine.IO;
using HanautaSketch.Resources;

namespace HanautaSketch.Views;

/// <summary>
/// 保存先を選んだあとに出す、WAV 書き出しのオプション画面。
/// 「書き出す」で選んだオプション、キャンセルで null を返す。
/// </summary>
public partial class WavExportOptionsWindow : Window
{
    private static readonly int[] SampleRates = [.. WavExportOptions.SupportedSampleRates];
    private static readonly WavSampleFormat[] Formats = [WavSampleFormat.Pcm16, WavSampleFormat.Pcm24, WavSampleFormat.Float32];

    public WavExportOptionsWindow()
    {
        InitializeComponent();
        SampleRateCombo.ItemsSource = SampleRates.Select(r => $"{r} Hz").ToArray();
        FormatCombo.ItemsSource = Formats.Select(FormatLabel).ToArray();
    }

    /// <param name="initial">初期値（前回の選択）。選べない値なら既定値にする</param>
    public WavExportOptionsWindow(WavExportOptions initial, string fileName) : this()
    {
        var defaults = new WavExportOptions();
        FileNameText.Text = fileName;
        SampleRateCombo.SelectedIndex = IndexOrDefault(SampleRates, initial.SampleRate, defaults.SampleRate);
        FormatCombo.SelectedIndex = IndexOrDefault(Formats, initial.Format, defaults.Format);
        ReleaseTailCheck.IsChecked = initial.IncludeReleaseTail;
    }

    private static int IndexOrDefault<T>(T[] items, T value, T fallback)
    {
        var index = Array.IndexOf(items, value);
        return index >= 0 ? index : Array.IndexOf(items, fallback);
    }

    private static string FormatLabel(WavSampleFormat format) => format switch
    {
        WavSampleFormat.Pcm16 => Strings.WavFormatPcm16,
        WavSampleFormat.Pcm24 => Strings.WavFormatPcm24,
        _ => Strings.WavFormatFloat32,
    };

    private void OnExportClick(object? sender, RoutedEventArgs e) => Close(new WavExportOptions
    {
        SampleRate = SampleRates[SampleRateCombo.SelectedIndex],
        Format = Formats[FormatCombo.SelectedIndex],
        IncludeReleaseTail = ReleaseTailCheck.IsChecked == true,
    });

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
