using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;

namespace HanautaSketch.Services;

/// <summary>トーストの種類。アイコンと色が変わる</summary>
public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// ウィンドウ右下に出す短い通知。VM からウィンドウを直接触らないための窓口。
/// </summary>
public interface IToastService
{
    void Show(string message, ToastKind kind = ToastKind.Info);
}

/// <summary>
/// Avalonia の <see cref="WindowNotificationManager"/> でトーストを出す。見た目と出入りの動きは
/// LuticaTheme.axaml の NotificationCard のテーマ。新しいものが下に積まれ、
/// <see cref="MaxItems"/> を超えると古いものから閉じる。クリックでも閉じる。
/// </summary>
public sealed class ToastService(TopLevel host) : IToastService
{
    /// <summary>同時に出しておく数</summary>
    public const int MaxItems = 3;

    private static readonly TimeSpan ShortDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LongDuration = TimeSpan.FromSeconds(8);

    private readonly WindowNotificationManager _manager = new(host)
    {
        Position = NotificationPosition.BottomRight,
        MaxItems = MaxItems,
        // ステータスバーにかからないよう、右下から少し離して積む
        Margin = new Thickness(0, 0, 12, 36),
    };

    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        var (type, duration) = kind switch
        {
            ToastKind.Success => (NotificationType.Success, ShortDuration),
            ToastKind.Warning => (NotificationType.Warning, LongDuration),
            ToastKind.Error => (NotificationType.Error, LongDuration),
            _ => (NotificationType.Information, ShortDuration),
        };
        _manager.Show(message, type, duration);
    }
}
