using Avalonia.Controls;
using FluentIcons.Common;

namespace HanautaSketch.Views;

/// <summary>一覧の1つ分（アイコンと表示するバリアント）</summary>
public sealed record IconEntry(Icon Icon, IconVariant Variant)
{
    public string Name => Icon.ToString();
}

/// <summary>一覧の1行分。行の中のアイコンは横に並ぶ</summary>
public sealed record IconRow(IReadOnlyList<IconEntry> Icons);

/// <summary>
/// 開発用の Fluent アイコン一覧。名前の語（空白区切りで全部含む）で絞り込み、バリアントを切り替える。
/// 1行に並べる数は窓の幅から決め、行を縦の VirtualizingStackPanel で仮想化する。
/// </summary>
public partial class IconBrowserWindow : Window
{
    /// <summary>アイコン1つ分の幅（<see cref="IconGrid"/> の StackPanel の Width と Margin の合計）</summary>
    private const double CellOuterWidth = 108;

    private static readonly Icon[] AllIcons = Enum.GetValues<Icon>();

    /// <summary>XAML の初期化中に呼ばれるイベントで一覧を作らないための印</summary>
    private bool _ready;

    /// <summary>1行に並べるアイコンの数（窓の幅から決まる）</summary>
    private int _columns = 6;

    private List<IconEntry> _matches = [];

    public IconBrowserWindow()
    {
        InitializeComponent();

        VariantBox.ItemsSource = Enum.GetValues<IconVariant>();
        VariantBox.SelectedItem = IconVariant.Regular;
        _ready = true;
        Refresh();
    }

    private void OnFilterChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) => Refresh();

    private void OnVariantChanged(object? sender, SelectionChangedEventArgs e) => Refresh();

    /// <summary>幅が変わったら1行の数を直し、行を組み直す</summary>
    private void OnScrollerSizeChanged(object? sender, Avalonia.Controls.SizeChangedEventArgs e)
    {
        var columns = Math.Max(1, (int)(e.NewSize.Width / CellOuterWidth));
        if (columns == _columns) return;
        _columns = columns;
        ShowRows();
    }

    private void Refresh()
    {
        if (!_ready) return;

        var tokens = (FilterBox.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var variant = VariantBox.SelectedItem is IconVariant v ? v : IconVariant.Regular;

        _matches = AllIcons
            .Where(icon => tokens.All(t => icon.ToString().Contains(t, StringComparison.OrdinalIgnoreCase)))
            .Select(icon => new IconEntry(icon, variant))
            .ToList();

        ShowRows();
    }

    /// <summary>一致したアイコンを <see cref="_columns"/> 個ずつの行にして表示する</summary>
    private void ShowRows()
    {
        if (!_ready) return;

        IconGrid.ItemsSource = _matches
            .Chunk(_columns)
            .Select(chunk => new IconRow(chunk))
            .ToList();
        CountText.Text = $"{_matches.Count} / {AllIcons.Length}";
    }
}
