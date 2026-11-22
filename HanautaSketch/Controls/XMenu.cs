using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Metadata;
using HanautaSketch.Services;

namespace HanautaSketch.Controls;

/// <summary>メニュー項目を出す OS</summary>
public enum XMenuPlatform
{
    All,
    MacOS,
    /// <summary>macOS 以外（Windows・Linux）</summary>
    Others,
}

/// <summary><see cref="XMenu"/> の項目・区切り線の共通部分</summary>
public abstract class XMenuNode : AvaloniaObject
{
    /// <summary>この項目を出す OS。既定はすべて</summary>
    public XMenuPlatform Platform { get; set; }

    internal bool IsShown => Platform switch
    {
        XMenuPlatform.MacOS => OperatingSystem.IsMacOS(),
        XMenuPlatform.Others => !OperatingSystem.IsMacOS(),
        _ => true,
    };
}

public sealed class XMenuSeparator : XMenuNode;

/// <summary>
/// <see cref="XMenu"/> の項目。表記とショートカットは Windows の形で書き、macOS では
/// アクセスキーを外す・Ctrl を ⌘ にする。慣例が違うところだけ Mac* で上書きする。
/// </summary>
public sealed class XMenuItem : XMenuNode
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<XMenuItem, ICommand?>(nameof(Command));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>表記（「ファイル(_F)」のようにアクセスキー付きで書く）</summary>
    public string Header { get; set; } = "";

    /// <summary>macOS での表記（言い回しが違うときだけ）。アクセスキーの除去・「…」への置き換えはこちらにもかかる</summary>
    public string? MacHeader { get; set; }

    /// <summary>ショートカット（Ctrl で書く。macOS では ⌘ になる）</summary>
    public KeyGesture? Gesture { get; set; }

    /// <summary>macOS でのショートカット（Ctrl で書く）。省くと <see cref="Gesture"/></summary>
    public KeyGesture? MacGesture { get; set; }

    /// <summary>サブメニュー</summary>
    [Content]
    public AvaloniaList<XMenuNode> Items { get; } = [];

    public event EventHandler? Click;

    internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// メニューバーを OS に合わせて出し分ける。macOS は画面上端の <see cref="NativeMenu"/>（置いたウィンドウのメニューにする）、
/// それ以外はこのコントロールの位置にウィンドウ内の <see cref="Menu"/> を出す。
/// ショートカットは、macOS ではメニューが受け持ち、それ以外では表示だけ（実際に効かせるのはウィンドウの KeyBinding）。
/// </summary>
public sealed partial class XMenu : Control
{
    private Menu? _menu;

    [Content]
    public AvaloniaList<XMenuNode> Items { get; } = [];

    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (OperatingSystem.IsMacOS())
        {
            IsVisible = false;
            ExportNativeMenu();
            return;
        }

        _menu = new Menu();
        AddItems(_menu.Items, Items);
        LogicalChildren.Add(_menu);
        VisualChildren.Add(_menu);
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        if (OperatingSystem.IsMacOS()) ExportNativeMenu();
    }

    /// <summary>
    /// 置いたウィンドウのメニューにする。ウィンドウを開いた後に付けると画面上端に出ないので、
    /// ビジュアルツリーへの接続（最初のレイアウト）は待たず、項目がそろって論理ツリーでウィンドウに入った時点で付ける。
    /// </summary>
    private void ExportNativeMenu()
    {
        if (IsInitialized && this.FindLogicalAncestorOfType<Window>() is { } window && NativeMenu.GetMenu(window) is null)
            NativeMenu.SetMenu(window, ToNativeMenu(Items));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_menu is null) return default;
        _menu.Measure(availableSize);
        return _menu.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _menu?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private static void AddItems(ItemCollection target, IEnumerable<XMenuNode> nodes)
    {
        foreach (var node in nodes.Where(n => n.IsShown))
        {
            if (node is not XMenuItem item)
            {
                target.Add(new Separator());
                continue;
            }

            var menuItem = new MenuItem { Header = item.Header, InputGesture = item.Gesture };
            menuItem.Bind(MenuItem.CommandProperty, item.GetObservable(XMenuItem.CommandProperty));
            menuItem.Click += (_, _) => item.RaiseClick();
            AddItems(menuItem.Items, item.Items);
            target.Add(menuItem);
        }
    }

    private static NativeMenu ToNativeMenu(IEnumerable<XMenuNode> nodes)
    {
        var menu = new NativeMenu();
        foreach (var node in nodes.Where(n => n.IsShown))
        {
            if (node is not XMenuItem item)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }

            var nativeItem = new NativeMenuItem
            {
                Header = ToMacHeader(item.MacHeader ?? item.Header),
                Gesture = (item.MacGesture ?? item.Gesture) is { } g ? PlatformKeys.ToPlatform(g) : null,
            };
            nativeItem.Bind(NativeMenuItem.CommandProperty, item.GetObservable(XMenuItem.CommandProperty));
            nativeItem.Click += (_, _) => item.RaiseClick();
            if (item.Items.Count > 0) nativeItem.Menu = ToNativeMenu(item.Items);
            menu.Items.Add(nativeItem);
        }
        return menu;
    }

    /// <summary>
    /// Windows 向けの表記を macOS の慣例に直す：アクセスキー（「(_F)」「_File」）を外す。
    /// </summary>
    public static string ToMacHeader(string header)
    {
        var s = ParenthesizedAccessKey().Replace(header, "");
        s = AccessKeyMarker().Replace(s, "$1");
        return s;
    }

    [GeneratedRegex(@"\(_[^)]\)")]
    private static partial Regex ParenthesizedAccessKey();

    /// <summary>「_x」の「_」（「__」は「_」1つ）</summary>
    [GeneratedRegex("_(.)")]
    private static partial Regex AccessKeyMarker();
}
