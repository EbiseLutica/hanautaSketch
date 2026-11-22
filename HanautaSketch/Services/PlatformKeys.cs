using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace HanautaSketch.Services;

/// <summary>
/// ショートカットの「コマンドキー」を OS に合わせる（Windows は Ctrl、macOS は ⌘）。
/// </summary>
public static class PlatformKeys
{
    /// <summary>ショートカットの主修飾キー</summary>
    public static KeyModifiers Command { get; } =
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>メッセージ表示用のコマンドキー名</summary>
    public static string CommandName { get; } = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";

    public static bool IsCommand(KeyModifiers modifiers) => modifiers.HasFlag(Command);

    /// <summary>
    /// XAML に Ctrl で書いたショートカット（KeyBinding とメニューの表示）を、macOS では ⌘ に置き換える。
    /// </summary>
    public static void Adapt(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;

        foreach (var binding in window.KeyBindings)
        {
            if (binding.Gesture is { } g) binding.Gesture = ToPlatform(g);
        }

        foreach (var item in window.GetLogicalDescendants().OfType<MenuItem>())
        {
            if (item.InputGesture is { } g) item.InputGesture = ToPlatform(g);
        }
    }

    /// <summary>Ctrl で書いたショートカットを、macOS では ⌘ に置き換える</summary>
    public static KeyGesture ToPlatform(KeyGesture g) =>
        OperatingSystem.IsMacOS() && g.KeyModifiers.HasFlag(KeyModifiers.Control)
            ? new KeyGesture(g.Key, (g.KeyModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta)
            : g;
}
