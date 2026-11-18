using System.Globalization;

namespace HanautaSketch.Services;

/// <summary>
/// 表示言語の設定。<see cref="System"/> は OS の言語に従う（既定）。
/// 言語は起動時に一度だけ決める。変えたら次回起動から反映する。
/// </summary>
public static class AppLanguage
{
    public const string System = "system";
    public const string Japanese = "ja";
    public const string English = "en";

    /// <summary>
    /// 設定の言語を UI の文化情報に反映する。画面を作る前（起動直後）に呼ぶ。
    /// <see cref="System"/> と未知の値では何もせず、OS の言語のままにする。
    /// </summary>
    public static void Apply(string choice)
    {
        if (choice is not (Japanese or English)) return;

        var culture = new CultureInfo(choice);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
