using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Drums;

namespace HanautaSketch.ViewModels;

/// <summary>
/// スマートドラムトラックの追加とセクション操作。セクションの変更はすべて Undo できる。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>ドラムトラックを追加したときの、最初のセクションの最低長（小節）</summary>
    private const int DefaultDrumSectionBars = 4;

    /// <summary>表示中のドラムトラックのセクション（ピアノロールのセクションレーン用）</summary>
    [ObservableProperty]
    private IReadOnlyList<DrumSection> _drumSections = [];

    [ObservableProperty]
    private DrumSection? _selectedDrumSection;

    /// <summary>選択中のトラックがスマートドラムトラックなら、その設定パネル用の VM</summary>
    [ObservableProperty]
    private SmartDrumTrackViewModel? _selectedDrumTrack;

    public bool IsSelectedTrackDrum => SelectedTrack?.IsDrum ?? false;

    partial void OnSelectedDrumSectionChanged(DrumSection? value)
    {
        if (SelectedDrumTrack is not null) SelectedDrumTrack.Section = value;
    }

    /// <summary>
    /// 選択トラックが変わったときに、ドラムのセクションレーンと設定パネルを合わせる。
    /// </summary>
    private void SyncDrumTrackSelection()
    {
        OnPropertyChanged(nameof(IsSelectedTrackDrum));
        if (SelectedTrack is SmartDrumTrack drum)
        {
            DrumSections = drum.Sections.ToList();
            var section = drum.Sections.FirstOrDefault();
            SelectedDrumTrack = new SmartDrumTrackViewModel(drum, this, section);
            SelectedDrumSection = section;
        }
        else
        {
            DrumSections = [];
            SelectedDrumTrack = null;
            SelectedDrumSection = null;
        }
    }

    /// <summary>
    /// スマートドラムトラックを追加する。ベースのソースは「Bass」という名前のトラック（なければ最後のノートトラック）。
    /// 曲の長さぶん（最低4小節）のセクションを1つ持った状態で始める。
    /// </summary>
    [RelayCommand]
    private void AddSmartDrumTrack()
    {
        var noteTracks = Tracks.OfType<NoteTrack>().ToList();
        var bass = noteTracks.FirstOrDefault(t => t.Name.Contains("Bass", StringComparison.OrdinalIgnoreCase))
                   ?? noteTracks.LastOrDefault();

        var lastTick = Song.Tracks.OfType<NoteTrack>().SelectMany(t => t.Notes).Select(n => n.EndTick)
            .DefaultIfEmpty(0).Max();
        var bars = Math.Max(DefaultDrumSectionBars, (lastTick + Song.TicksPerBar - 1) / Song.TicksPerBar);

        var track = new SmartDrumTrack { Name = UniqueTrackName("Drums"), BassSource = bass };
        track.AddSection(0, bars * Song.TicksPerBar);
        AddTrack(track);
    }

    /// <summary>
    /// セクションレーンのドラッグで、小節範囲のセクションを作る。
    /// </summary>
    public void CreateDrumSection(int start, int end)
    {
        if (SelectedTrack is not SmartDrumTrack drum || end <= start) return;

        DrumSection? created = null;
        EditDrumTrack(drum, () => created = drum.AddSection(start, end));
        SelectDrumSection(created);
    }

    /// <summary>
    /// セクションレーンの端のドラッグで、セクションを伸縮する。
    /// </summary>
    public void ResizeDrumSection(DrumSection section, int start, int end)
    {
        if (SelectedTrack is not SmartDrumTrack drum || end <= start) return;

        DrumSection? resized = null;
        EditDrumTrack(drum, () => resized = drum.ResizeSection(section, start, end));
        SelectDrumSection(resized);
    }

    /// <summary>
    /// ドラムトラックへの変更を Undo できる1操作としてまとめ、再生成と表示の更新まで行う。
    /// </summary>
    public void EditDrumTrack(SmartDrumTrack track, Action edit)
    {
        OnEditStarted();
        edit();
        OnEditCompleted();

        MarkDirty();
        NotesRevision++;
        RegenerateSmartTracks();
    }

    /// <summary>スライダーのドラッグなど、連続した変更を1つの Undo ステップにまとめている最中か</summary>
    private bool _continuousEdit;

    /// <summary>
    /// 連続した変更（スライダーのドラッグなど）の1回分を反映する。最初の1回で Undo の起点を取り、
    /// <see cref="EndContinuousEdit"/> で1ステップとして確定する。
    /// </summary>
    public void EditDrumTrackContinuous(SmartDrumTrack track, Action edit)
    {
        if (!_continuousEdit)
        {
            OnEditStarted();
            _continuousEdit = true;
        }

        edit();
        MarkDirty();
        NotesRevision++;
        RegenerateSmartTracks();
    }

    /// <summary>
    /// 連続した変更を確定する。実際に変化していれば1ステップとして履歴に積む。
    /// </summary>
    public void EndContinuousEdit()
    {
        if (!_continuousEdit) return;
        _continuousEdit = false;
        OnEditCompleted();
    }

    /// <summary>
    /// セクションレーンの表示を更新し、指定のセクションを選択する。
    /// </summary>
    public void SelectDrumSection(DrumSection? section)
    {
        if (SelectedTrack is SmartDrumTrack drum) DrumSections = drum.Sections.ToList();
        SelectedDrumSection = section is not null && DrumSections.Contains(section) ? section : null;
    }

    private void RefreshDrumLane()
    {
        if (SelectedTrack is not SmartDrumTrack drum) return;

        DrumSections = drum.Sections.ToList();
        if (SelectedDrumSection is { } selected && !DrumSections.Contains(selected))
            SelectedDrumSection = DrumSections.FirstOrDefault(s => s.Start == selected.Start);
    }
}
