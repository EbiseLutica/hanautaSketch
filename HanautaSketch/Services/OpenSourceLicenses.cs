using System.Reflection;

namespace HanautaSketch.Services;

/// <summary>
/// 配布物に含むライブラリの一覧と、アプリ本体のライセンス本文。
/// 内容は THIRD_PARTIES.md と揃える（ライブラリを足したら両方を直す）。
/// </summary>
public static class OpenSourceLicenses
{
    /// <summary>配布物に含むライブラリ（名前・ライセンス・URL）</summary>
    public static readonly (string Name, string License, string Url)[] Libraries =
    [
        ("Avalonia (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent)", "MIT", "https://github.com/AvaloniaUI/Avalonia"),
        ("CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        ("MeltySynth", "MIT", "https://github.com/sinshu/meltysynth"),
        ("Silk.NET (Silk.NET.OpenAL, Silk.NET.OpenAL.Soft.Native)", "MIT / LGPL-2.0 (OpenAL Soft)", "https://github.com/dotnet/Silk.NET"),
    ];

    /// <summary>
    /// ビルドに埋め込んだ LICENSE（MIT）の本文を返す。埋め込みがなければ空。
    /// </summary>
    public static string ReadAppLicense()
    {
        using var stream = typeof(OpenSourceLicenses).Assembly.GetManifestResourceStream("LICENSE");
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
