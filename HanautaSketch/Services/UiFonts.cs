using Avalonia.Media;

namespace HanautaSketch.Services;

/// <summary>
/// 画面の書体。OS ごとに日本語向けの書体を先頭に、Latin 向けの書体を次に並べる。
/// 先頭を既定の書体にし、残りは先頭に字が無いときのフォールバックにする（書体は同梱しない）。
/// </summary>
internal static class UiFonts
{
    public static IReadOnlyList<string> Families()
    {
        if (OperatingSystem.IsWindows()) return ["Meiryo UI", "Verdana"];
        if (OperatingSystem.IsMacOS()) return ["Hiragino Sans", "San Francisco"];
        return ["Noto Sans CJK JP", "Noto Sans"];
    }

    public static FontManagerOptions CreateFontManagerOptions()
    {
        var families = Families();
        return new FontManagerOptions
        {
            DefaultFamilyName = families[0],
            FontFallbacks = families.Skip(1).Select(name => new FontFallback { FontFamily = new FontFamily(name) }).ToArray(),
        };
    }
}
