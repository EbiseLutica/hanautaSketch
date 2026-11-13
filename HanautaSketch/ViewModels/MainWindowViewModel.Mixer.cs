using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.ViewModels;

/// <summary>
/// 楽器欄の1枠。カテゴリが変わっても枠は同じまま、表示名だけを書き換える。
/// </summary>
public sealed partial class ProgramSlot : ObservableObject
{
    /// <summary>1始まりの番号＋名前（例: 34 Fingered Bass）</summary>
    [ObservableProperty]
    private string _label = "";
}

/// <summary>
/// トラック一覧の表示状態、音色・音量・ミュート・ソロ、トラックの並び替え。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>音色名（添字は音色番号）。SF2 の読み込み後にプリセット名で上書きする</summary>
    private readonly string[] _programNames = [.. GeneralMidi.ProgramNames];

    /// <summary>トラック一覧の行。Tracks と同じ並び</summary>
    public ObservableCollection<TrackItemViewModel> TrackItems { get; } = [];

    [ObservableProperty]
    private TrackItemViewModel? _selectedTrackItem;

    public IReadOnlyList<string> ProgramCategories => GeneralMidi.Categories;

    /// <summary>選択中トラックの音色のカテゴリ。変えるとそのカテゴリの先頭の音色にする</summary>
    public int SelectedProgramCategory
    {
        get => GeneralMidi.CategoryOf(SelectedTrack?.Program ?? 0);
        set
        {
            if (value < 0 || value == SelectedProgramCategory) return;
            SetSelectedTrackProgram(value * GeneralMidi.ProgramsPerCategory);
        }
    }

    /// <summary>
    /// 楽器欄の選択肢（カテゴリ内の8枠）。コレクションは差し替えず、各枠の表示名だけを更新する。
    /// ComboBox は ItemsSource が差し替わると選択を -1 に戻し、その後の番号の通知を取りこぼすことがあるため。
    /// </summary>
    public IReadOnlyList<ProgramSlot> ProgramSlots { get; } =
        Enumerable.Range(0, GeneralMidi.ProgramsPerCategory).Select(_ => new ProgramSlot()).ToList();

    /// <summary>選択中の音色の、カテゴリ内での位置（0〜7）。楽器欄とは番号で結ぶ</summary>
    public int SelectedProgramIndex
    {
        get => (SelectedTrack?.Program ?? 0) % GeneralMidi.ProgramsPerCategory;
        set
        {
            if (value < 0 || value >= GeneralMidi.ProgramsPerCategory) return;
            SetSelectedTrackProgram(SelectedProgramCategory * GeneralMidi.ProgramsPerCategory + value);
        }
    }

    /// <summary>選択中トラックの音量（0〜127）</summary>
    public int SelectedTrackVolume
    {
        get => SelectedTrack?.Volume ?? 0;
        set
        {
            if (SelectedTrack is null || SelectedTrack.Volume == value) return;
            SelectedTrack.Volume = Math.Clamp(value, 0, 127);
            OnPropertyChanged();
            OnMixerChanged();
        }
    }

    private void InitializeTrackItems() => Tracks.CollectionChanged += OnTracksChanged;

    /// <summary>
    /// トラックの増減・並び替えに合わせて行を作り直す。選択は同じトラックに保つ。
    /// </summary>
    private void OnTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var existing = TrackItems.ToDictionary(i => i.Track);
        TrackItems.Clear();
        foreach (var track in Tracks)
            TrackItems.Add(existing.TryGetValue(track, out var item) ? item : new TrackItemViewModel(track, this));
        SelectedTrackItem = TrackItems.FirstOrDefault(i => i.Track == SelectedTrack);
    }

    partial void OnSelectedTrackItemChanged(TrackItemViewModel? value)
    {
        if (value is not null && value.Track != SelectedTrack) SelectedTrack = value.Track;
    }

    /// <summary>
    /// 選択トラックが変わったときに、行の選択と音色・音量の表示を合わせる。
    /// </summary>
    private void SyncSelectedTrackItem()
    {
        SelectedTrackItem = TrackItems.FirstOrDefault(i => i.Track == SelectedTrack);
        NotifyProgramChanged();
        OnPropertyChanged(nameof(SelectedTrackVolume));
    }

    private void SetSelectedTrackProgram(int program)
    {
        if (SelectedTrack is null || SelectedTrack.Program == program) return;
        SelectedTrack.Program = Math.Clamp(program, 0, GeneralMidi.ProgramCount - 1);
        NotifyProgramChanged();
        OnMixerChanged();
    }

    private void NotifyProgramChanged()
    {
        OnPropertyChanged(nameof(SelectedProgramCategory));
        RefreshProgramSlots();
        OnPropertyChanged(nameof(SelectedProgramIndex));
    }

    /// <summary>
    /// 楽器欄の各枠の表示名を、選択中のカテゴリの音色に合わせる。
    /// </summary>
    private void RefreshProgramSlots()
    {
        var first = SelectedProgramCategory * GeneralMidi.ProgramsPerCategory;
        for (var i = 0; i < ProgramSlots.Count; i++)
            ProgramSlots[i].Label = $"{first + i + 1} {_programNames[first + i]}";
    }

    /// <summary>
    /// SF2 のプリセット名で音色名を上書きする。SF2 にない番号は GM の名前のまま。
    /// </summary>
    private void ApplyPresetNames(IReadOnlyDictionary<int, string> presetNames)
    {
        foreach (var (program, name) in presetNames)
        {
            if (!string.IsNullOrWhiteSpace(name)) _programNames[program] = name;
        }
        RefreshProgramSlots();
    }

    /// <summary>
    /// 音色・音量・ミュート・ソロが変わったときに呼ぶ。再生中ならすぐ反映する。
    /// </summary>
    public void OnMixerChanged()
    {
        MarkDirty();
        _audio?.Engine.ApplyMixer(Song);
    }

    /// <summary>
    /// トラック名が変わったときに呼ぶ。ソースの選択肢などに名前が出ているので、設定パネルも作り直す。
    /// </summary>
    public void OnTrackRenamed()
    {
        MarkDirty();
        OnSelectedTrackChanged(SelectedTrack);
    }

    /// <summary>
    /// トラックを指定位置へ移動する。Undo できる。チャンネルの割り当てはトラック順なので再生にも反映する。
    /// </summary>
    public void MoveTrack(Track track, int newIndex)
    {
        var oldIndex = Tracks.IndexOf(track);
        newIndex = Math.Clamp(newIndex, 0, Tracks.Count - 1);
        if (oldIndex < 0 || oldIndex == newIndex) return;

        OnEditStarted();
        Song.Tracks.RemoveAt(oldIndex);
        Song.Tracks.Insert(newIndex, track);
        Tracks.Move(oldIndex, newIndex);
        OnEditCompleted();

        SelectedTrack = track;
        SyncSelectedTrackItem();
        OnMixerChanged();
        RegenerateSmartTracks();
    }
}
