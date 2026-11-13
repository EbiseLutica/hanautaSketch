using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.ViewModels;

/// <summary>スナップ単位の選択肢</summary>
public sealed record SnapOption(string Label, int Ticks)
{
    public override string ToString() => Label;
}

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
        Tracks.Add(new NoteTrack { Name = "Melody", Program = 0 });
        Tracks.Add(new NoteTrack { Name = "Bass", Program = 33 });
        foreach (var track in Tracks)
            Song.Tracks.Add(track);

        _selectedTrack = Tracks[0];
        _selectedSnap = SnapOptions[2];
        UpdateKey();
    }

    public Song Song { get; } = new();

    public ObservableCollection<NoteTrack> Tracks { get; } = [];

    public IReadOnlyList<string> TonicNames => Key.PitchNames;

    public IReadOnlyList<Mode> Modes { get; } = Enum.GetValues<Mode>();

    public IReadOnlyList<SnapOption> SnapOptions { get; } =
    [
        new("1/4", Song.DefaultPpq),
        new("1/8", Song.DefaultPpq / 2),
        new("1/16", Song.DefaultPpq / 4),
        new("1/32", Song.DefaultPpq / 8),
    ];

    [ObservableProperty]
    private NoteTrack? _selectedTrack;

    [ObservableProperty]
    private SnapOption _selectedSnap;

    [ObservableProperty]
    private int _tonic;

    [ObservableProperty]
    private Mode _mode;

    [ObservableProperty]
    private Key _key;

    partial void OnTonicChanged(int value) => UpdateKey();

    partial void OnModeChanged(Mode value) => UpdateKey();

    private void UpdateKey()
    {
        Key = new Key(Tonic, Mode);
        Song.Key = Key;
    }
}
