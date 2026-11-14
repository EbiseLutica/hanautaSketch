using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HanautaSketch.Controls;
using HanautaSketch.Engine.Smart.Chords;
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
    /// 未保存の変更があれば、閉じる前に確認する。確認は非同期なので一度キャンセルし、続行なら改めて閉じる。
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || ViewModel is not { IsDirty: true } vm) return;

        e.Cancel = true;
        if (!await vm.ConfirmDiscardChangesAsync()) return;

        _closeConfirmed = true;
        Close();
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
                header.Children.Add(new TextBlock { Text = "ベース外", Opacity = 0.5, FontSize = 11 });
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => vm.SetChordOverride(segment, suggestion.Chord);
            return item;
        }

        foreach (var suggestion in suggestions.QualitiesOf(currentRoot, QualitySuggestionCount))
            flyout.Items.Add(ChordItem(suggestion));

        flyout.Items.Add(new Separator());

        var otherRoots = new MenuItem { Header = "基音を変える" };
        foreach (var suggestion in suggestions.OtherRoots(currentRoot, RootSuggestionCount))
            otherRoots.Items.Add(ChordItem(suggestion));
        flyout.Items.Add(otherRoots);

        // その他: 根音 → 種類（グループごと）で全部から選ぶ
        var all = new MenuItem { Header = "その他" };
        for (var root = 0; root < 12; root++)
        {
            var rootItem = new MenuItem { Header = key.GetPitchName(root) };
            foreach (var group in ChordQualities.All.GroupBy(q => q.Group))
            {
                var groupItem = new MenuItem { Header = group.Key };
                foreach (var quality in group)
                    groupItem.Items.Add(ChordItem(suggestions.Evaluate(new Chord(root, quality.Quality))));
                rootItem.Items.Add(groupItem);
            }
            all.Items.Add(rootItem);
        }
        flyout.Items.Add(all);

        var noChord = new MenuItem { Header = (segment.Chord is null ? "● " : "　") + "N.C.（鳴らさない）" };
        noChord.Click += (_, _) => vm.SetChordOverride(segment, null);
        flyout.Items.Add(noChord);

        flyout.Items.Add(new Separator());
        if (segment.IsOverride)
        {
            var reset = new MenuItem { Header = "自動判別に戻す" };
            reset.Click += (_, _) => vm.ClearChordOverride(segment);
            flyout.Items.Add(reset);
        }
        else
        {
            var fix = new MenuItem { Header = "このコードで確定" };
            fix.Click += (_, _) => vm.SetChordOverride(segment, segment.Chord?.RootPosition);
            flyout.Items.Add(fix);
        }

        flyout.ShowAt(target, true);
    }

    #endregion

    #region ヘルプ

    /// <summary>
    /// コード候補のランキングに使ったデータセットの出典を表示する。
    /// </summary>
    private void OnShowDataSources(object? sender, RoutedEventArgs e)
    {
        var text = string.Join("\n", [
            "コード候補の並び（前のコードからの来やすさ）は、次のデータセットから集計した表を使っています。",
            "",
            .. ChordTransitionTable.Default.Sources.Select(s => "・" + s),
            "",
            "McGill Billboard Project: J. A. Burgoyne, J. Wild, I. Fujinaga,",
            "  \"An Expert Ground Truth Set for Audio Chord Recognition and Music Analysis\", ISMIR 2011.",
            "Chordonomicon: S. Kantarelis et al., \"CHORDONOMICON: A Dataset of 666,000 Songs and their",
            "  Chord Progressions\", arXiv:2410.22046, 2024. https://huggingface.co/datasets/ailsntua/Chordonomicon",
            "",
            "Chordonomicon は CC BY-NC 4.0 のため、この表を含む hanautaSketch は非営利でのみ配布します。",
        ]);
        var window = new Window
        {
            Title = "データの出典",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new SelectableTextBlock { Text = text, Margin = new Thickness(16), TextWrapping = Avalonia.Media.TextWrapping.NoWrap },
        };
        window.ShowDialog(this);
    }

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
