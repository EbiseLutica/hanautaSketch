using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Editing;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.ViewModels;

/// <summary>
/// 編集の Undo/Redo と新規作成。
/// 履歴はノートトラックのノーツと、スマートコードトラックの override のスナップショットで持つ
/// （試作段階のデータ量なら十分軽い）。トラック構成や設定の変更は対象外。
/// </summary>
public partial class MainWindowViewModel
{
    private readonly UndoHistory<EditSnapshot> _history = new();
    private EditSnapshot? _pendingSnapshot;

    /// <summary>ノーツを外部から差し替えたときに増やす。ピアノロールが選択を解除して描き直す</summary>
    [ObservableProperty]
    private int _notesRevision;

    /// <summary>
    /// スマートコードトラック1本分の編集対象の写し。
    /// </summary>
    private sealed record SmartTrackState(ChordOverride[] Overrides, NoteTrack? BassSource, NoteTrack? MelodySource)
    {
        public bool ContentEquals(SmartTrackState other) =>
            Overrides.SequenceEqual(other.Overrides)
            && BassSource == other.BassSource && MelodySource == other.MelodySource;
    }

    /// <summary>
    /// 編集対象の写し。トラック構成（並びと参照）と、各トラックの中身を持つ。
    /// トラックはオブジェクトごと保持するので、削除したトラックも Undo で元に戻せる。
    /// </summary>
    private sealed record EditSnapshot(
        Track[] Tracks,
        Dictionary<NoteTrack, Note[]> Notes,
        Dictionary<SmartChordTrack, SmartTrackState> SmartTracks)
    {
        public bool ContentEquals(EditSnapshot other) =>
            Tracks.SequenceEqual(other.Tracks)
            && Notes.All(p => other.Notes.TryGetValue(p.Key, out var n) && p.Value.SequenceEqual(n))
            && SmartTracks.All(p => other.SmartTracks.TryGetValue(p.Key, out var s) && p.Value.ContentEquals(s));
    }

    private EditSnapshot TakeSnapshot() => new(
        Song.Tracks.ToArray(),
        Song.Tracks.OfType<NoteTrack>().ToDictionary(t => t, t => t.Notes.ToArray()),
        Song.Tracks.OfType<SmartChordTrack>().ToDictionary(
            t => t, t => new SmartTrackState(t.Overrides.ToArray(), t.BassSource, t.MelodySource)));

    private void RestoreSnapshot(EditSnapshot snapshot)
    {
        var selected = SelectedTrack;
        if (!Song.Tracks.SequenceEqual(snapshot.Tracks))
            ReplaceTracks(snapshot.Tracks);

        foreach (var (track, notes) in snapshot.Notes)
        {
            track.Notes.Clear();
            track.Notes.AddRange(notes);
        }

        foreach (var (track, state) in snapshot.SmartTracks)
        {
            track.Overrides.Clear();
            track.Overrides.AddRange(state.Overrides);
            track.BassSource = state.BassSource;
            track.MelodySource = state.MelodySource;
        }

        SelectedTrack = selected is not null && Tracks.Contains(selected) ? selected : Tracks.FirstOrDefault();
        OnSelectedTrackChanged(SelectedTrack);
        NotesRevision++;
        MarkDirty();
        RegenerateSmartTracks();
    }

    /// <summary>
    /// 一連の編集が始まる直前に呼ぶ。変更前の状態を控える。
    /// </summary>
    public void OnEditStarted() => _pendingSnapshot = TakeSnapshot();

    /// <summary>
    /// 一連の編集が終わったら呼ぶ。実際に変化していれば履歴に積む。
    /// </summary>
    public void OnEditCompleted()
    {
        if (_pendingSnapshot is null) return;

        if (!_pendingSnapshot.ContentEquals(TakeSnapshot()))
        {
            _history.Push(_pendingSnapshot);
            NotifyHistoryChanged();
        }
        _pendingSnapshot = null;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        RestoreSnapshot(_history.Undo(TakeSnapshot()));
        NotifyHistoryChanged();
    }

    private bool CanUndo() => _history.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        RestoreSnapshot(_history.Redo(TakeSnapshot()));
        NotifyHistoryChanged();
    }

    private bool CanRedo() => _history.CanRedo;

    private void NotifyHistoryChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void ClearHistory()
    {
        _history.Clear();
        _pendingSnapshot = null;
        NotifyHistoryChanged();
    }

    /// <summary>
    /// 空の曲にする。トラック構成は既定（Melody / Bass / Chord）に戻す。
    /// </summary>
    [RelayCommand]
    private void New()
    {
        StopPlayback();
        _suppressDirty = true;
        try
        {
            ReplaceTracks(CreateDefaultTracks());
            Bpm = 120;
            Song.BeatsPerBar = 4;
            Tonic = 0;
            Mode = Mode.Major;
            PlayStartTick = 0;
        }
        finally
        {
            _suppressDirty = false;
        }

        _currentFile = null;
        IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
        ClearHistory();
        NotesRevision++;
        RegenerateSmartTracks();
        Message = "新しい曲";
    }
}
