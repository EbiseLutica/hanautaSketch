using Avalonia.Controls;
using HanautaSketch.ViewModels;

namespace HanautaSketch.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnNotesEdited(object? sender, EventArgs e) => (DataContext as MainWindowViewModel)?.RecomputeChords();
}
