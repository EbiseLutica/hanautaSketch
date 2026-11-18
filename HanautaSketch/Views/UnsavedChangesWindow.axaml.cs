using Avalonia.Controls;
using Avalonia.Interactivity;
using HanautaSketch.Resources;
using HanautaSketch.Services;

namespace HanautaSketch.Views;

/// <summary>
/// 未保存の変更があるときの確認画面。閉じるボタンで閉じた場合はキャンセル扱い。
/// </summary>
public partial class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow()
    {
        InitializeComponent();
    }

    public UnsavedChangesWindow(string documentName) : this()
    {
        MessageText.Text = Strings.Format(Strings.UnsavedMessage, documentName);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Save);

    private void OnDiscardClick(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Discard);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Cancel);
}
