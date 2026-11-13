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
}
