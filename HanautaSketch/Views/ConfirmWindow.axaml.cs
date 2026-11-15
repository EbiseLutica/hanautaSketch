using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HanautaSketch.Views;

/// <summary>
/// はい／いいえの確認画面。誤って進めないよう、既定のボタン（Enter）はキャンセル側にする。
/// 閉じるボタンで閉じた場合はキャンセル扱い。
/// </summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string message, string okText, string cancelText) : this()
    {
        Title = title;
        MessageText.Text = message;
        OkButton.Content = okText;
        CancelButton.Content = cancelText;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
