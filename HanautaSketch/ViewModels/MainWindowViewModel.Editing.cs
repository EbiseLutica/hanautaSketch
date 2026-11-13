using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Editing;

namespace HanautaSketch.ViewModels;

/// <summary>
/// ノート編集の Undo/Redo と新規作成。
/// 履歴は全トラックのノーツのスナップショットで持つ（試作段階のデータ量なら十分軽い）。
/// </summary>
public partial class MainWindowViewModel
{
    private readonly UndoHistory<NotesSnapshot> _history = new();
    private NotesSnapshot? _pendingSnapshot;

    /// <summary>ノーツを外部から差し替えたときに増やす。ピアノロールが選択を解除して描き直す</summary>
    [ObservableProperty]
    private int _notesRevision;

    /// <summary>
    /// 全トラックのノーツの写し。トラックの並びは Song.Tracks と同じ。
    /// </summary>
    private sealed record NotesSnapshot(Note[][] Tracks)
    {
        public bool ContentEquals(NotesSnapshot other) =>
            Tracks.Length == other.Tracks.Length && Tracks.Zip(other.Tracks).All(p => p.First.SequenceEqual(p.Second));
    }

    private NotesSnapshot TakeSnapshot() =>
        new(Song.Tracks.OfType<NoteTrack>().Select(t => t.Notes.ToArray()).ToArray());

    private void RestoreSnapshot(NotesSnapshot snapshot)
    {
        var tracks = Song.Tracks.OfType<NoteTrack>().ToList();
        for (var i = 0; i < tracks.Count && i < snapshot.Tracks.Length; i++)
        {
            tracks[i].Notes.Clear();
            tracks[i].Notes.AddRange(snapshot.Tracks[i]);
        }

        NotesRevision++;
        MarkDirty();
        RecomputeChords();
    }

    /// <summary>
    /// ピアノロールで一連の編集が始まる直前に呼ぶ。変更前の状態を控える。
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
    /// 空の曲にする。トラック構成（Melody / Bass）と音色は既定に戻す。
    /// </summary>
    [RelayCommand]
    private void New()
    {
        StopPlayback();
        _suppressDirty = true;
        try
        {
            ResetTrack(_melody, "Melody", 0);
            ResetTrack(_bass, "Bass", 33);
            Bpm = 120;
            Song.BeatsPerBar = 4;
            Tonic = 0;
            Mode = Mode.Major;
            MinorDominant = default;
            UseMelodyForChords = true;
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
        RecomputeChords();
        Message = "新しい曲";
    }

    private static void ResetTrack(NoteTrack track, string name, int program)
    {
        track.Name = name;
        track.Program = program;
        track.Volume = 100;
        track.Notes.Clear();
    }
}
