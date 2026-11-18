using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using HanautaSketch.Services;

namespace HanautaSketch.Views;

/// <summary>
/// オープンソースライセンスの一覧。左の項目名と右の本文が連動する（scroll-spy）。
/// </summary>
public partial class LicensesWindow : Window
{
    private readonly List<Control> _sections = [];

    /// <summary>左の項目をクリックして移動中の項目。移動が終わるまで、スクロール途中の選択は変えない</summary>
    private int? _movingTo;

    /// <summary>左の選択を変えるのがスクロール側のときに、項目選択の移動処理を止める</summary>
    private bool _syncingSelection;

    /// <summary>XAML ローダー用。実際に開くときは項目を渡すこちらを使う</summary>
    public LicensesWindow() : this([]) { }

    public LicensesWindow(IReadOnlyList<LicenseEntry> entries)
    {
        InitializeComponent();

        foreach (var entry in entries)
        {
            var section = CreateSection(entry);
            _sections.Add(section);
            Sections.Children.Add(section);
            EntryList.Items.Add(entry.Name);
        }

        EntryList.SelectedIndex = entries.Count > 0 ? 0 : -1;
    }

    /// <summary>項目1つ分（名前・版・ライセンス式と、各ライセンス本文）を作る</summary>
    private static Control CreateSection(LicenseEntry entry)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        var meta = string.Join("  ·  ", new[] { entry.Version, entry.License }.Where(s => s.Length > 0));
        if (meta.Length > 0)
            panel.Children.Add(new TextBlock { Text = meta, Classes = { "dimmed" }, TextWrapping = TextWrapping.Wrap });

        foreach (var text in entry.Texts)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text.Title,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 8, 0, 0),
            });
            panel.Children.Add(new SelectableTextBlock { Text = text.Body, TextWrapping = TextWrapping.Wrap });
        }
        return panel;
    }

    /// <summary>左で項目を選ぶと、その項目の先頭へ右をスクロールする</summary>
    private void OnEntryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || EntryList.SelectedIndex < 0 || EntryList.SelectedIndex >= _sections.Count) return;

        _movingTo = EntryList.SelectedIndex;
        Scroller.Offset = new Vector(0, _sections[_movingTo.Value].Bounds.Top);
    }

    /// <summary>右のスクロールに合わせ、画面の先頭にかかっている項目を左で選ぶ</summary>
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_sections.Count == 0) return;

        var offset = Scroller.Offset.Y;
        var atBottom = offset + Scroller.Viewport.Height >= Scroller.Extent.Height - 1;

        int active;
        if (_movingTo is { } target)
        {
            active = target;
            if (atBottom || Math.Abs(offset - _sections[target].Bounds.Top) < 2) _movingTo = null;
        }
        else
        {
            active = 0;
            for (var i = 0; i < _sections.Count; i++)
                if (_sections[i].Bounds.Top <= offset + 1) active = i;
            if (atBottom) active = _sections.Count - 1;
        }

        if (EntryList.SelectedIndex == active) return;
        _syncingSelection = true;
        EntryList.SelectedIndex = active;
        _syncingSelection = false;
    }
}
