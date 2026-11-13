using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.ViewModels;

/// <summary>スナップ単位の選択肢</summary>
public sealed record SnapOption(string Label, int Ticks)
{
    public override string ToString() => Label;
}

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    public MainWindowViewModel()
    {
        ReplaceTracks(CreateDefaultTracks());
        _selectedSnap = SnapOptions[2];
        UpdateKey();
        InitializePlayback();
        IsDirty = false;
    }

    public Song Song { get; } = new();

    /// <summary>トラック一覧。Song.Tracks と同じ内容・順序を保つ</summary>
    public ObservableCollection<Track> Tracks { get; } = [];

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
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedTrackCommand))]
    private Track? _selectedTrack;

    /// <summary>選択中のトラックがスマートコードトラックなら、その設定パネル用の VM</summary>
    [ObservableProperty]
    private SmartChordTrackViewModel? _selectedSmartTrack;

    [ObservableProperty]
    private SnapOption _selectedSnap;

    [ObservableProperty]
    private int _tonic;

    [ObservableProperty]
    private Mode _mode;

    [ObservableProperty]
    private Key _key;

    /// <summary>コードレーンに表示する判別結果</summary>
    [ObservableProperty]
    private IReadOnlyList<ChordSegment> _chords = [];

    /// <summary>選択中トラックの音色（GM 番号）</summary>
    public int SelectedTrackProgram
    {
        get => SelectedTrack?.Program ?? 0;
        set
        {
            if (SelectedTrack is null || SelectedTrack.Program == value) return;
            SelectedTrack.Program = Math.Clamp(value, 0, 127);
            MarkDirty();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// コードレーンに表示し、override の対象にするスマートコードトラック。
    /// 選択中がスマートコードトラックならそれ、そうでなければ最初の1本。
    /// </summary>
    public SmartChordTrack? ChordLaneTrack =>
        SelectedTrack as SmartChordTrack ?? Tracks.OfType<SmartChordTrack>().FirstOrDefault();

    partial void OnTonicChanged(int value) => UpdateKey();

    partial void OnModeChanged(Mode value) => UpdateKey();

    partial void OnSelectedTrackChanged(Track? value)
    {
        SelectedSmartTrack = value is SmartChordTrack smart ? new SmartChordTrackViewModel(smart, this) : null;
        OnPropertyChanged(nameof(SelectedTrackProgram));
        RefreshChordLane();
    }

    private void UpdateKey()
    {
        Key = new Key(Tonic, Mode);
        Song.Key = Key;
        MarkDirty();
        RegenerateSmartTracks();
    }

    /// <summary>
    /// ピアノロールでノーツが編集されたときに呼ぶ。
    /// </summary>
    public void OnNotesEdited()
    {
        MarkDirty();
        RegenerateSmartTracks();
    }

    /// <summary>
    /// スマートトラックの設定が変わったときに呼ぶ。
    /// </summary>
    public void OnSmartTrackSettingsChanged()
    {
        MarkDirty();
        RegenerateSmartTracks();
    }

    /// <summary>
    /// 全スマートトラックを再生成する。試作段階のため毎回全区間を計算する。
    /// </summary>
    public void RegenerateSmartTracks()
    {
        foreach (var track in Tracks.OfType<SmartChordTrack>())
            track.Regenerate(Song);
        RefreshChordLane();
    }

    private void RefreshChordLane() => Chords = ChordLaneTrack?.Chords.ToList() ?? [];

    #region トラック管理

    /// <summary>新規作成時のトラック構成: Melody / Bass と、それをソースにしたスマートコード</summary>
    private static List<Track> CreateDefaultTracks()
    {
        var melody = new NoteTrack { Name = "Melody", Program = 0 };
        var bass = new NoteTrack { Name = "Bass", Program = 33 };
        var chord = new SmartChordTrack { Name = "Chord", Program = 0, BassSource = bass, MelodySource = melody };
        return [melody, bass, chord];
    }

    /// <summary>
    /// トラック構成を丸ごと差し替える。
    /// </summary>
    private void ReplaceTracks(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        Song.Tracks.Clear();
        Tracks.Clear();
        foreach (var track in list)
        {
            Song.Tracks.Add(track);
            Tracks.Add(track);
        }
        SelectedTrack = Tracks.FirstOrDefault();
    }

    /// <summary>
    /// スマートコードトラックを追加する。ソースは「Bass」という名前のトラック（なければ最後のノートトラック）。
    /// </summary>
    [RelayCommand]
    private void AddSmartChordTrack()
    {
        var noteTracks = Tracks.OfType<NoteTrack>().ToList();
        var bass = noteTracks.FirstOrDefault(t => t.Name.Contains("Bass", StringComparison.OrdinalIgnoreCase))
                   ?? noteTracks.LastOrDefault();
        var melody = noteTracks.FirstOrDefault(t => t != bass);

        var track = new SmartChordTrack
        {
            Name = $"Chord {Tracks.OfType<SmartChordTrack>().Count() + 1}",
            BassSource = bass,
            MelodySource = melody,
        };
        Song.Tracks.Add(track);
        Tracks.Add(track);
        track.Regenerate(Song);
        SelectedTrack = track;
        MarkDirty();
    }

    /// <summary>
    /// 選択中のスマートトラックを削除する。ノートトラックは他のスマートトラックのソースになり得るので、今は削除できない。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelectedTrack))]
    private void RemoveSelectedTrack()
    {
        if (SelectedTrack is not SmartChordTrack track) return;

        var index = Tracks.IndexOf(track);
        Song.Tracks.Remove(track);
        Tracks.Remove(track);
        SelectedTrack = Tracks.Count > 0 ? Tracks[Math.Clamp(index - 1, 0, Tracks.Count - 1)] : null;
        MarkDirty();
        RefreshChordLane();
    }

    private bool CanRemoveSelectedTrack() => SelectedTrack is SmartChordTrack;

    #endregion

    #region コードの override

    /// <summary>
    /// コードレーンの区間に対する候補を返す。現在のコードを先頭に、直前のコードから移りやすい順で並べる。
    /// </summary>
    public IReadOnlyList<Chord> GetChordSuggestions(ChordSegment segment)
    {
        if (ChordLaneTrack is not { } track) return [];

        var previous = Chords.LastOrDefault(c => c.End <= segment.Start)?.Chord.RootPosition;
        var minorDominant = track.ChordSettings.MinorDominant;
        IEnumerable<Chord> suggestions = previous is { } p
            ? ChordTransitions.SuggestNext(Key, p, minorDominant).Append(p)
            : ChordTransitions.GetDiatonicChords(Key, minorDominant);

        return suggestions.Prepend(segment.Chord.RootPosition).Distinct().ToList();
    }

    /// <summary>
    /// 区間のコードを手動で指定する。
    /// </summary>
    public void SetChordOverride(ChordSegment segment, Chord chord)
    {
        if (ChordLaneTrack is not { } track) return;
        OnEditStarted();
        track.SetOverride(segment.Start, segment.Length, chord);
        OnEditCompleted();
        OnSmartTrackSettingsChanged();
    }

    /// <summary>
    /// 区間の手動指定を外し、自動判別に戻す。
    /// </summary>
    public void ClearChordOverride(ChordSegment segment)
    {
        if (ChordLaneTrack is not { } track) return;
        OnEditStarted();
        track.ClearOverrides(segment.Start, segment.Length);
        OnEditCompleted();
        OnSmartTrackSettingsChanged();
    }

    #endregion
}
