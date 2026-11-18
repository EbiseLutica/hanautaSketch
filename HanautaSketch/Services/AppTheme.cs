using Avalonia;
using Avalonia.Styling;

namespace HanautaSketch.Services;

/// <summary>
/// 配色テーマの設定。<see cref="System"/> は OS の設定に従う（既定）。
/// 変えると画面にすぐ反映する（Lutica のライト / ダークのテーマ辞書を切り替える）。
/// </summary>
public static class AppTheme
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    /// <summary>
    /// 設定のテーマを Application に反映する。未知の値は <see cref="System"/> として扱う。
    /// </summary>
    public static void Apply(string choice)
    {
        if (Application.Current is not { } app) return;

        app.RequestedThemeVariant = choice switch
        {
            Light => ThemeVariant.Light,
            Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
