using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Drums;
using HanautaSketch.Services;

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
        InitializeTrackItems();
        ReplaceTracks(CreateDefaultTracks());
        _selectedSnap = SnapOptions[2];
        SyncConductorDisplay();
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

    /// <summary>コードレーンに表示する判別結果</summary>
    [ObservableProperty]
    private IReadOnlyList<ChordSegment> _chords = [];

    /// <summary>
    /// コードレーンに表示し、override の対象にするスマートコードトラック。
    /// 選択中がスマートコードトラックならそれ、そうでなければ最初の1本。
    /// </summary>
    public SmartChordTrack? ChordLaneTrack =>
        SelectedTrack as SmartChordTrack ?? Tracks.OfType<SmartChordTrack>().FirstOrDefault();

    partial void OnSelectedTrackChanged(Track? value)
    {
        SelectedSmartTrack = value is SmartChordTrack smart ? new SmartChordTrackViewModel(smart, this) : null;
        SyncSelectedTrackItem();
        SyncDrumTrackSelection();
        RefreshChordLane();
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
        foreach (var track in Tracks.OfType<SmartDrumTrack>())
            track.Regenerate(Song);
        RefreshChordLane();
        RefreshDrumLane();
    }

    private void RefreshChordLane() => Chords = ChordLaneTrack?.Chords.ToList() ?? [];

    #region トラック管理

    /// <summary>新規作成時のトラック構成: Melody / Bass。スマートトラックは必要に応じて追加する</summary>
    private static List<Track> CreateDefaultTracks() =>
    [
        new NoteTrack { Name = "Melody", Program = 0 },
        new NoteTrack { Name = "Bass", Program = 33 },
    ];

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
    /// 空のノートトラックを追加する。
    /// </summary>
    [RelayCommand]
    private void AddNoteTrack()
    {
        var track = new NoteTrack { Name = UniqueTrackName("Track") };
        AddTrack(track);
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

        AddTrack(new SmartChordTrack { Name = UniqueTrackName("Chord"), BassSource = bass, MelodySource = melody });
    }

    /// <summary>
    /// 選択中のトラックの直後にトラックを追加して選択する。Undo できる。
    /// </summary>
    private void AddTrack(Track track)
    {
        OnEditStarted();
        var index = SelectedTrack is { } selected ? Tracks.IndexOf(selected) + 1 : Tracks.Count;
        Song.Tracks.Insert(index, track);
        Tracks.Insert(index, track);
        OnEditCompleted();

        SelectedTrack = track;
        MarkDirty();
        RegenerateSmartTracks();
    }

    /// <summary>
    /// 選択中のトラックを削除する。ノートトラックなら、それをソースにしているスマートトラックの参照を外す。Undo できる。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelectedTrack))]
    private void RemoveSelectedTrack()
    {
        if (SelectedTrack is not { } track) return;

        OnEditStarted();
        if (track is NoteTrack noteTrack)
        {
            foreach (var smart in Tracks.OfType<SmartChordTrack>())
            {
                if (smart.BassSource == noteTrack) smart.BassSource = null;
                if (smart.MelodySource == noteTrack) smart.MelodySource = null;
            }

            foreach (var drum in Tracks.OfType<SmartDrumTrack>())
            {
                if (drum.BassSource == noteTrack) drum.BassSource = null;
            }
        }

        var index = Tracks.IndexOf(track);
        Song.Tracks.Remove(track);
        Tracks.Remove(track);
        OnEditCompleted();

        SelectedTrack = Tracks.Count > 0 ? Tracks[Math.Clamp(index - 1, 0, Tracks.Count - 1)] : null;
        Message = $"{track.Name} を削除した（{PlatformKeys.CommandName}+Z で元に戻せる）";
        MarkDirty();
        RegenerateSmartTracks();
    }

    private bool CanRemoveSelectedTrack() => SelectedTrack is not null;

    /// <summary>
    /// 「基本名 + 番号」で、既存のトラックと重ならない名前を作る。
    /// </summary>
    private string UniqueTrackName(string baseName)
    {
        for (var i = 1; ; i++)
        {
            var name = $"{baseName} {i}";
            if (Tracks.All(t => t.Name != name)) return name;
        }
    }

    #endregion

    #region コードの override

    /// <summary>
    /// コードレーンの区間に対する候補を返す。現在のコードを先頭に、直前のコードから移りやすい順で並べる。
    /// </summary>
    public ChordSuggestions? GetChordSuggestions(ChordSegment segment)
    {
        if (ChordLaneTrack is not { } track) return null;

        var key = Song.Conductor.KeyAt(segment.Start);
        var previous = Chords.LastOrDefault(c => c.End <= segment.Start)?.Chord;
        return new ChordSuggestions(key, previous, track.BassSource?.Notes ?? [], segment.Start, segment.End, ChordTable);
    }

    /// <summary>
    /// 区間のコードを手動で指定する。null なら N.C.（鳴らさない）にする。
    /// </summary>
    public void SetChordOverride(ChordSegment segment, Chord? chord)
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
