using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.ViewModels;

/// <summary>スナップ単位の選択肢</summary>
public sealed record SnapOption(string Label, int Ticks)
{
    public override string ToString() => Label;
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly NoteTrack _melody = new() { Name = "Melody", Program = 0 };
    private readonly NoteTrack _bass = new() { Name = "Bass", Program = 33 };

    public MainWindowViewModel()
    {
        Tracks.Add(_melody);
        Tracks.Add(_bass);
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

    public IReadOnlyList<MinorDominantMode> MinorDominantModes { get; } = Enum.GetValues<MinorDominantMode>();

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

    [ObservableProperty]
    private MinorDominantMode _minorDominant;

    [ObservableProperty]
    private bool _useMelodyForChords = true;

    [ObservableProperty]
    private IReadOnlyList<ChordSegment> _chords = [];

    partial void OnTonicChanged(int value) => UpdateKey();

    partial void OnModeChanged(Mode value) => UpdateKey();

    partial void OnMinorDominantChanged(MinorDominantMode value) => RecomputeChords();

    partial void OnUseMelodyForChordsChanged(bool value) => RecomputeChords();

    private void UpdateKey()
    {
        Key = new Key(Tonic, Mode);
        Song.Key = Key;
        RecomputeChords();
    }

    /// <summary>
    /// Bass（任意で Melody）からコード進行を判別し直す。試作段階のため毎回全区間を計算する。
    /// </summary>
    public void RecomputeChords()
    {
        var lastTick = Song.Tracks.OfType<NoteTrack>().SelectMany(t => t.Notes).Select(n => n.EndTick)
            .DefaultIfEmpty(0).Max();
        var endTick = (lastTick + Song.TicksPerBar - 1) / Song.TicksPerBar * Song.TicksPerBar;

        var context = new ChordDetectionContext(Song.Key, Song.Ppq, Song.TicksPerBar);
        var settings = new SmartChordSettings { MinorDominant = MinorDominant };
        Chords = ChordDetector.Detect(_bass.Notes, UseMelodyForChords ? _melody.Notes : [], endTick, context,
            settings, []);
    }
}
