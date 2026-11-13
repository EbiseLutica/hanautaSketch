using Avalonia.Controls;
using HanautaSketch.Controls;
using HanautaSketch.Services;
using HanautaSketch.ViewModels;

namespace HanautaSketch.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
            vm.FileDialog = new StorageProjectFileDialog(this);
    }

    private void OnNotesEdited(object? sender, EventArgs e) => (DataContext as MainWindowViewModel)?.OnNotesEdited();

    private void OnEditStarted(object? sender, EventArgs e) => (DataContext as MainWindowViewModel)?.OnEditStarted();

    private void OnEditCompleted(object? sender, EventArgs e) => (DataContext as MainWindowViewModel)?.OnEditCompleted();

    private void OnNotePreview(object? sender, NotePreviewEventArgs e) =>
        (DataContext as MainWindowViewModel)?.PreviewNote(e.Pitch, e.Velocity);

    /// <summary>
    /// コードレーンのクリックで、候補コード・確定・自動に戻す のメニューをカーソル位置に出す。
    /// </summary>
    private void OnChordSegmentClicked(object? sender, ChordSegmentClickedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not Control target) return;

        var segment = e.Segment;
        var flyout = new MenuFlyout();
        foreach (var chord in vm.GetChordSuggestions(segment))
        {
            var isCurrent = chord == segment.Chord.RootPosition;
            var item = new MenuItem { Header = (isCurrent ? "● " : "　") + chord.GetName(vm.Key) };
            item.Click += (_, _) => vm.SetChordOverride(segment, chord);
            flyout.Items.Add(item);
        }

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
            fix.Click += (_, _) => vm.SetChordOverride(segment, segment.Chord.RootPosition);
            flyout.Items.Add(fix);
        }

        flyout.ShowAt(target, true);
    }
}
