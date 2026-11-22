using Avalonia;
using HanautaSketch.Services;

namespace HanautaSketch;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 文字列リソースは画面を作るときに読まれるので、起動直後に言語を決める
        AppLanguage.Apply(AppSettingsStore.Load().Language);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(UiFonts.CreateFontManagerOptions())
            .LogToTrace();
}
