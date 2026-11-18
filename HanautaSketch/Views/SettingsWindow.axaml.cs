using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HanautaSketch.Views;

/// <summary>
/// 設定画面。左のサイドバーで観点（サウンド・表示・コードテーブル）を選び、右にその項目を出す。
/// DataContext はメインの VM で、変更はすぐ反映される。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Control[]? _pages;

    public SettingsWindow()
    {
        InitializeComponent();
        _pages = [SoundPage, AppearancePage, ChordTablePage];
    }

    /// <summary>サイドバーで選んだ観点のページだけを出す。InitializeComponent 中の呼び出しは無視する</summary>
    private void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_pages is null || SectionList.SelectedIndex < 0) return;

        for (var i = 0; i < _pages.Length; i++)
            _pages[i].IsVisible = i == SectionList.SelectedIndex;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
