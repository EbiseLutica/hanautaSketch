using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HanautaSketch.Resources;
using HanautaSketch.Services;
using HanautaSketch.ViewModels;
using HanautaSketch.Views;

namespace HanautaSketch;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppTheme.Apply(AppSettingsStore.Load().Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        if (OperatingSystem.IsMacOS() && NativeMenu.GetMenu(this) is { } appMenu)
            LocalizeStandardMacMenuItems(appMenu);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Avalonia がアプリ名メニューに足す標準の項目（英語で固定）を、表示言語の表記に置き換える。
    /// 項目はメニューが書き出されるときに足されるので、追加を待って置き換える。
    /// </summary>
    private void LocalizeStandardMacMenuItems(NativeMenu menu)
    {
        var name = Name ?? "hanautaSketch";
        var headers = new Dictionary<string, string>
        {
            ["Services"] = Strings.MacServices,
            ["Hide " + name] = Strings.Format(Strings.MacHideApp, name),
            ["Hide Others"] = Strings.MacHideOthers,
            ["Show All"] = Strings.MacShowAll,
            ["Quit"] = Strings.Format(Strings.MacQuitApp, name),
        };

        void Localize()
        {
            foreach (var item in menu.Items.OfType<NativeMenuItem>())
            {
                if (item.Header is { } header && headers.TryGetValue(header, out var ja)) item.Header = ja;
            }
        }

        Localize();
        ((INotifyCollectionChanged)menu.Items).CollectionChanged += (_, _) => Localize();
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
            window.ShowAbout();
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
            window.ShowSettings();
    }
}
