using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Controls;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Resources;
using HanautaSketch.Services;
using HanautaSketch.ViewModels;

namespace HanautaSketch.Views;

public partial class MainWindow : Window
{
    /// <summary>この距離（px）以上動かしたら、トラック一覧のドラッグとみなす</summary>
    private const double DragThreshold = 6;

    private bool _closeConfirmed;
    private TrackItemViewModel? _dragItem;
    private Point _dragStart;
    private bool _isDragging;

    public MainWindow()
    {
        InitializeComponent();
        PlatformKeys.Adapt(this);

        // macOS は画面上端のメニューバー（NativeMenu）を使う。ショートカットもそちらが持つので、二重に効かないよう KeyBinding は外す
        if (OperatingSystem.IsMacOS())
        {
            WindowMenu.IsVisible = false;
            KeyBindings.Clear();
        }
        else
        {
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Control),
                Command = new RelayCommand(ShowSettings),
            });
        }

        // 文字の入力欄にフォーカスがある間は、取り消す・やり直すをその入力欄に向ける
        AddHandler(GotFocusEvent, OnFocusChanged, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, OnFocusChanged, RoutingStrategies.Bubble, handledEventsToo: true);

        // Space はボタンやトグルより先に受け取り、テキスト入力中以外は再生の切り替えに使う
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // スウィングのスライダーは、離した・キー操作を終えた・フォーカスが外れたときに Undo の1ステップとして確定する
        SwingSlider.AddHandler(PointerReleasedEvent, OnContinuousEditFinished, RoutingStrategies.Bubble, handledEventsToo: true);
        SwingSlider.AddHandler(PointerCaptureLostEvent, OnContinuousEditFinished, RoutingStrategies.Bubble, handledEventsToo: true);
        SwingSlider.AddHandler(KeyUpEvent, OnContinuousEditFinished, RoutingStrategies.Bubble, handledEventsToo: true);
        SwingSlider.LostFocus += OnContinuousEditFinished;

        TrackList.AddHandler(PointerPressedEvent, OnTrackListPointerPressed, RoutingStrategies.Tunnel);
        TrackList.AddHandler(PointerMovedEvent, OnTrackListPointerMoved, RoutingStrategies.Tunnel);
        TrackList.AddHandler(PointerReleasedEvent, OnTrackListPointerReleased, RoutingStrategies.Tunnel);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnContinuousEditFinished(object? sender, RoutedEventArgs e) => ViewModel?.EndContinuousEdit();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
            vm.Dialogs = new DialogService(this);
    }

    /// <summary>
    /// 開いたら、SoundFont が未設定・見つからない場合に選ぶよう促す。
    /// </summary>
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ViewModel is { } vm)
            await vm.PromptSoundFontSetupAsync();
    }

    #region 設定

    private void OnShowSettingsMenuItem(object? sender, RoutedEventArgs e) => ShowSettings();

    /// <summary>
    /// 設定画面をモーダルで開く（macOS のアプリ名メニュー、Windows のツールメニューから）。
    /// 開いている間は、確認やファイル選択のダイアログを設定画面の上に出す。
    /// </summary>
    public async void ShowSettings()
    {
        if (ViewModel is not { } vm || OwnedWindows.OfType<SettingsWindow>().Any()) return;

        var window = new SettingsWindow { DataContext = vm };
        var previous = vm.Dialogs;
        vm.Dialogs = new DialogService(window);
        try
        {
            await window.ShowDialog(this);
        }
        finally
        {
            vm.Dialogs = previous;
        }
    }

    #endregion

    /// <summary>
    /// 未保存の変更があれば、閉じる前に確認する。確認は非同期なので一度キャンセルし、続行なら改めて閉じる。
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || ViewModel is not { } vm) return;
        if (!vm.IsDirty && !vm.IsExtractingChordData) return;

        e.Cancel = true;
        if (vm.IsDirty && !await vm.ConfirmDiscardChangesAsync()) return;
        if (!await vm.ConfirmQuitDuringChordExtractionAsync()) return;

        _closeConfirmed = true;
        Close();
    }

    private TextBox? _focusedTextBox;

    private void OnFocusChanged(object? sender, RoutedEventArgs e)
    {
        var textBox = FocusManager?.GetFocusedElement() as TextBox;
        if (textBox == _focusedTextBox) return;

        if (_focusedTextBox is not null) _focusedTextBox.PropertyChanged -= OnFocusedTextBoxPropertyChanged;
        _focusedTextBox = textBox;
        if (textBox is not null) textBox.PropertyChanged += OnFocusedTextBoxPropertyChanged;

        if (ViewModel is { } vm)
            vm.TextEditTarget = textBox is null ? null : new TextBoxEditTarget(textBox);
    }

    private void OnFocusedTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CanUndoProperty || e.Property == TextBox.CanRedoProperty)
            ViewModel?.NotifyHistoryChanged();
    }

    private sealed class TextBoxEditTarget(TextBox textBox) : ITextEditTarget
    {
        public bool CanUndo => textBox.CanUndo;
        public bool CanRedo => textBox.CanRedo;
        public void Undo() => textBox.Undo();
        public void Redo() => textBox.Redo();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None) return;
        if (FocusManager?.GetFocusedElement() is TextBox) return;

        ViewModel?.TogglePlaybackCommand.Execute(null);
        e.Handled = true;
    }

    #region ピアノロール

    private void OnNotesEdited(object? sender, EventArgs e) => ViewModel?.OnNotesEdited();

    private void OnEditStarted(object? sender, EventArgs e) => ViewModel?.OnEditStarted();

    private void OnEditCompleted(object? sender, EventArgs e) => ViewModel?.OnEditCompleted();

    private void OnNotePreview(object? sender, NotePreviewEventArgs e) => ViewModel?.PreviewNotes(e.Notes);

    private void OnDrumSectionRequested(object? sender, DrumSectionRequestedEventArgs e) =>
        ViewModel?.CreateDrumSection(e.Start, e.End);

    private void OnDrumSectionResizeRequested(object? sender, DrumSectionResizeRequestedEventArgs e) =>
        ViewModel?.ResizeDrumSection(e.Section, e.Start, e.End);

    /// <summary>種類を変える候補として直下に並べる数</summary>
    private const int QualitySuggestionCount = 10;

    /// <summary>「基音を変える」に並べる数</summary>
    private const int RootSuggestionCount = 12;

    /// <summary>
    /// コードレーンのクリックで、コードを変えるメニューをカーソル位置に出す。
    /// 直下は今の根音のまま種類を変える候補、根音を変える候補はサブメニュー。どちらも前のコードからの来やすさ順。
    /// ベースとぶつかる候補には印を付ける。
    /// </summary>
    private void OnChordSegmentClicked(object? sender, ChordSegmentClickedEventArgs e)
    {
        if (ViewModel is not { } vm || sender is not Control target) return;
        if (vm.GetChordSuggestions(e.Segment) is not { } suggestions) return;

        var segment = e.Segment;
        var key = vm.Song.Conductor.KeyAt(segment.Start);
        var current = segment.Chord?.RootPosition;
        var currentRoot = current?.Root ?? key.Tonic;
        var flyout = new MenuFlyout();

        MenuItem ChordItem(ChordSuggestion suggestion)
        {
            var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new TextBlock
            {
                Text = (suggestion.Chord == current ? "● " : "　") + suggestion.Chord.GetName(key),
            });
            if (suggestion.ClashesWithBass)
                header.Children.Add(new TextBlock { Text = Strings.ChordOutOfBass, Opacity = 0.5, FontSize = 11 });
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => vm.SetChordOverride(segment, suggestion.Chord);
            return item;
        }

        foreach (var suggestion in suggestions.QualitiesOf(currentRoot, QualitySuggestionCount))
            flyout.Items.Add(ChordItem(suggestion));

        flyout.Items.Add(new Separator());

        var otherRoots = new MenuItem { Header = Strings.ChordChangeRoot };
        foreach (var suggestion in suggestions.OtherRoots(currentRoot, RootSuggestionCount))
            otherRoots.Items.Add(ChordItem(suggestion));
        flyout.Items.Add(otherRoots);

        // その他: 根音 → 種類（グループごと）で全部から選ぶ
        var all = new MenuItem { Header = Strings.ChordOther };
        for (var root = 0; root < 12; root++)
        {
            var rootItem = new MenuItem { Header = key.GetPitchName(root) };
            foreach (var group in ChordQualities.All.GroupBy(q => q.Group))
            {
                var groupItem = new MenuItem { Header = ChordGroupName(group.Key) };
                foreach (var quality in group)
                    groupItem.Items.Add(ChordItem(suggestions.Evaluate(new Chord(root, quality.Quality))));
                rootItem.Items.Add(groupItem);
            }
            all.Items.Add(rootItem);
        }
        flyout.Items.Add(all);

        // 追加のデータがまだなら、取得の入口を案内する
        if (!vm.HasExtendedChordData && !vm.IsExtractingChordData)
        {
            var acquire = new MenuItem { Header = Strings.ChordImprove };
            acquire.Click += (_, _) => vm.AcquireChordDataCommand.Execute(null);
            flyout.Items.Add(acquire);
        }

        var noChord = new MenuItem { Header = (segment.Chord is null ? "● " : "　") + Strings.ChordNoChord };
        noChord.Click += (_, _) => vm.SetChordOverride(segment, null);
        flyout.Items.Add(noChord);

        flyout.Items.Add(new Separator());
        if (segment.IsOverride)
        {
            var reset = new MenuItem { Header = Strings.ChordResetAuto };
            reset.Click += (_, _) => vm.ClearChordOverride(segment);
            flyout.Items.Add(reset);
        }
        else
        {
            var fix = new MenuItem { Header = Strings.ChordFixThis };
            fix.Click += (_, _) => vm.SetChordOverride(segment, segment.Chord?.RootPosition);
            flyout.Items.Add(fix);
        }

        flyout.ShowAt(target, true);
    }

    /// <summary>コードの種類のグループを、表示言語の名前にする</summary>
    private static string ChordGroupName(ChordQualityGroup group) => group switch
    {
        ChordQualityGroup.Triad => Strings.ChordGroupTriad,
        ChordQualityGroup.Sus => Strings.ChordGroupSus,
        ChordQualityGroup.SixthAdd9 => Strings.ChordGroupSixth,
        ChordQualityGroup.Seventh => Strings.ChordGroupSeventh,
        ChordQualityGroup.Tension => Strings.ChordGroupTension,
        _ => group.ToString(),
    };

    #endregion

    #region ヘルプ

    /// <summary>
    /// Windows のヘルプメニューから、オープンソースライセンスを開く。
    /// </summary>
    private void OnShowLicensesMenuItem(object? sender, RoutedEventArgs e) => ShowLicenses();

    /// <summary>
    /// macOS のヘルプメニューから、オープンソースライセンスを開く。
    /// </summary>
    private void OnShowLicenses(object? sender, EventArgs e) => ShowLicenses();

    /// <summary>
    /// 本体（MIT）・同梱ライブラリのライセンスと、コード候補に使ったデータの出典をまとめて表示する。
    /// </summary>
    /// <param name="owner">親にする窓（省略時はメインウィンドウ）</param>
    public void ShowLicenses(Window? owner = null)
    {
        var text = string.Join("\n", [
            "■ " + Strings.LicensesAppHeader,
            OpenSourceLicenses.ReadAppLicense().TrimEnd(),
            "",
            "■ " + Strings.LicensesDataHeader,
            Strings.DataSourcesIntro,
            "",
            .. (ViewModel?.ChordTable ?? ChordTransitionTable.Default).Sources.Select(s =>
                Strings.Format(Strings.DataSourceEntry, s.Name, s.License, s.Songs)),
            "",
            "McGill Billboard Project: J. A. Burgoyne, J. Wild, I. Fujinaga,",
            "  \"An Expert Ground Truth Set for Audio Chord Recognition and Music Analysis\", ISMIR 2011.",
            "Chordonomicon: S. Kantarelis et al., \"CHORDONOMICON: A Dataset of 666,000 Songs and their",
            "  Chord Progressions\", arXiv:2410.22046, 2024. https://huggingface.co/datasets/ailsntua/Chordonomicon",
            "",
            Strings.DataSourcesBundled,
            "",
            "■ " + Strings.LicensesLibrariesHeader,
            .. OpenSourceLicenses.Libraries.Select(l => $"{l.Name}: {l.License}\n  {l.Url}"),
        ]);
        var window = new Window
        {
            Title = Strings.LicensesTitle,
            Width = 640,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Content = new SelectableTextBlock { Text = text, Margin = new Thickness(16), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            },
        };
        window.ShowDialog(owner ?? this);
    }

    /// <summary>
    /// Windows のヘルプメニューから「について」を開く。
    /// </summary>
    private void OnShowAboutMenuItem(object? sender, RoutedEventArgs e) => ShowAbout();

    /// <summary>
    /// アプリの紹介を表示する（macOS のアプリ名メニュー、Windows のヘルプメニューから）。
    /// </summary>
    public void ShowAbout()
    {
        var panel = new StackPanel { Margin = new Thickness(32, 24), Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "hanautaSketch", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = Strings.AboutTagline, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        var window = new Window
        {
            Title = Strings.AboutApp,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = panel,
        };
        var licensesLink = new Button { Content = Strings.AboutLicensesLink, Classes = { "ghost" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        licensesLink.Click += (_, _) => ShowLicenses(window);
        panel.Children.Add(licensesLink);
        window.ShowDialog(this);
    }

    #endregion

    #region ウィンドウ（macOS のメニューから）

    private void OnCloseWindow(object? sender, EventArgs e) => Close();

    private void OnMinimizeWindow(object? sender, EventArgs e) => WindowState = WindowState.Minimized;

    private void OnZoomWindow(object? sender, EventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    #endregion

    #region トラック名の編集

    /// <summary>
    /// 行のダブルクリックで名前の編集を始め、入力欄を全選択状態にする。
    /// </summary>
    private void OnTrackItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control row || row.DataContext is not TrackItemViewModel item) return;
        if (e.Source is Visual source && source.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null) return;

        item.BeginRename();
        var textBox = row.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (textBox is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TrackItemViewModel item }) return;

        switch (e.Key)
        {
            case Key.Enter:
                item.CommitRename();
                TrackList.Focus();
                e.Handled = true;
                break;
            case Key.Escape:
                item.CancelRename();
                TrackList.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TrackItemViewModel item }) item.CommitRename();
    }

    #endregion

    #region トラックの並び替え

    private void OnTrackListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragItem = null;
        _isDragging = false;
        if (!e.GetCurrentPoint(TrackList).Properties.IsLeftButtonPressed) return;

        // M/S や名前の入力欄から始まった操作はドラッグにしない
        if (e.Source is Visual source && (source.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null
                                          || source.FindAncestorOfType<TextBox>(includeSelf: true) is not null))
            return;

        _dragItem = FindTrackItem(e.Source);
        _dragStart = e.GetPosition(TrackList);
    }

    private void OnTrackListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null || !e.GetCurrentPoint(TrackList).Properties.IsLeftButtonPressed) return;

        var delta = e.GetPosition(TrackList) - _dragStart;
        if (!_isDragging && Math.Abs(delta.Y) < DragThreshold) return;

        _isDragging = true;
        TrackList.Cursor = new Cursor(StandardCursorType.DragMove);
    }

    /// <summary>
    /// ドラッグを離した位置の行へトラックを移動する。
    /// </summary>
    private void OnTrackListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var item = _dragItem;
        var wasDragging = _isDragging;
        _dragItem = null;
        _isDragging = false;
        TrackList.Cursor = Cursor.Default;
        if (!wasDragging || item is null || ViewModel is not { } vm) return;

        var target = FindTrackItem(TrackList.InputHitTest(e.GetPosition(TrackList)));
        var index = target is not null
            ? vm.TrackItems.IndexOf(target)
            : e.GetPosition(TrackList).Y < 0 ? 0 : vm.TrackItems.Count - 1;
        vm.MoveTrack(item.Track, index);
        e.Handled = true;
    }

    private static TrackItemViewModel? FindTrackItem(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as TrackItemViewModel;

    #endregion
}
