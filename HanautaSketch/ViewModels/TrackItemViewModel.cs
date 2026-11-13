using CommunityToolkit.Mvvm.ComponentModel;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Drums;

namespace HanautaSketch.ViewModels;

/// <summary>
/// トラック一覧の1行。名前の編集やミュート・ソロをモデルに反映し、オーナーに通知する。
/// </summary>
public sealed partial class TrackItemViewModel(Track track, MainWindowViewModel owner) : ObservableObject
{
    public Track Track { get; } = track;

    public bool IsSmart => Track is SmartChordTrack or SmartDrumTrack;

    public string Name => Track.Name;

    public bool Mute
    {
        get => Track.Mute;
        set
        {
            if (Track.Mute == value) return;
            Track.Mute = value;
            OnPropertyChanged();
            owner.OnMixerChanged();
        }
    }

    public bool Solo
    {
        get => Track.Solo;
        set
        {
            if (Track.Solo == value) return;
            Track.Solo = value;
            OnPropertyChanged();
            owner.OnMixerChanged();
        }
    }

    /// <summary>名前をその場で編集中か</summary>
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editingName = "";

    public void BeginRename()
    {
        EditingName = Track.Name;
        IsEditing = true;
    }

    /// <summary>
    /// 編集中の名前を確定する。空や変化なしなら何もしない。
    /// </summary>
    public void CommitRename()
    {
        if (!IsEditing) return;
        IsEditing = false;

        var name = EditingName.Trim();
        if (name.Length == 0 || name == Track.Name) return;

        Track.Name = name;
        OnPropertyChanged(nameof(Name));
        owner.OnTrackRenamed();
    }

    public void CancelRename() => IsEditing = false;
}
