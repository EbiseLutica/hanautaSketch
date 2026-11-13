using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Drums;

namespace HanautaSketch.ViewModels;

/// <summary>ドラムパターンの選択肢</summary>
public sealed record DrumPatternOption(string Label, DrumPattern Pattern)
{
    public override string ToString() => Label;
}

/// <summary>
/// スマートドラムトラックの設定パネル。トラックのベースソースと、選択中セクションの設定を扱う。
/// セクションは不変レコードなので、変更のたびに新しいレコードへ差し替え、オーナーに通知する。
/// </summary>
public sealed partial class SmartDrumTrackViewModel : ObservableObject
{
    private readonly SmartDrumTrack _track;
    private readonly MainWindowViewModel _owner;

    public SmartDrumTrackViewModel(SmartDrumTrack track, MainWindowViewModel owner, DrumSection? section)
    {
        _track = track;
        _owner = owner;
        _section = section;
        BassSourceOptions = owner.Tracks.OfType<NoteTrack>().Select(t => new SourceOption(t.Name, t))
            .Prepend(new SourceOption("（なし）", null)).ToList();
    }

    public IReadOnlyList<SourceOption> BassSourceOptions { get; }

    public IReadOnlyList<DrumPatternOption> PatternOptions { get; } =
    [
        new("4つ打ち", DrumPattern.FourOnTheFloor),
        new("8ビート", DrumPattern.EightBeat),
        new("16ビート", DrumPattern.SixteenBeat),
    ];

    public SourceOption? BassSource
    {
        get => BassSourceOptions.FirstOrDefault(o => o.Track == _track.BassSource);
        set
        {
            if (value?.Track == _track.BassSource) return;
            _owner.EditDrumTrack(_track, () => _track.BassSource = value?.Track);
        }
    }

    /// <summary>設定対象のセクション。null ならセクションの設定は出さない</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSection), nameof(Pattern), nameof(Swing), nameof(SwingAmount),
        nameof(FollowBass), nameof(FixLabel), nameof(IsFixed))]
    private DrumSection? _section;

    public bool HasSection => Section is not null;

    public bool IsFixed => Section?.IsFixed ?? false;

    public string FixLabel => IsFixed ? "fix を解除（生成に戻す）" : "fix（ノーツを編集できるようにする）";

    public DrumPatternOption? Pattern
    {
        get => PatternOptions.FirstOrDefault(o => o.Pattern == Section?.Pattern);
        set
        {
            if (value is not null) UpdateSection(s => s with { Pattern = value.Pattern });
        }
    }

    public bool Swing
    {
        get => Section?.Swing ?? false;
        set => UpdateSection(s => s with { Swing = value });
    }

    /// <summary>スウィングの強さ（%）</summary>
    public int SwingAmount
    {
        get => (int)Math.Round((Section?.SwingAmount ?? 0) * 100);
        set => UpdateSection(s => s with { SwingAmount = Math.Clamp(value, 0, 100) / 100.0 });
    }

    public bool FollowBass
    {
        get => Section?.FollowBass ?? false;
        set => UpdateSection(s => s with { FollowBass = value });
    }

    [RelayCommand]
    private void ToggleFix()
    {
        if (Section is not { } section) return;
        DrumSection? updated = null;
        _owner.EditDrumTrack(_track, () =>
            updated = section.IsFixed ? _track.Unfix(section) : _track.Fix(section, _owner.Song));
        _owner.SelectDrumSection(updated);
    }

    [RelayCommand]
    private void DeleteSection()
    {
        if (Section is not { } section) return;
        _owner.EditDrumTrack(_track, () => _track.RemoveSection(section));
        _owner.SelectDrumSection(null);
    }

    private void UpdateSection(Func<DrumSection, DrumSection> change)
    {
        if (Section is not { } section) return;
        var updated = change(section);
        if (updated == section) return;

        _owner.EditDrumTrack(_track, () => _track.ReplaceSection(section, updated));
        _owner.SelectDrumSection(updated);
    }
}
