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
    /// 編集対象の写し。各配列の並びは Song.Tracks 内の同じ種類のトラックの並び。
    /// </summary>
    private sealed record EditSnapshot(Note[][] Notes, ChordOverride[][] Overrides)
    {
        public bool ContentEquals(EditSnapshot other) =>
            SequencesEqual(Notes, other.Notes) && SequencesEqual(Overrides, other.Overrides);

        private static bool SequencesEqual<T>(T[][] a, T[][] b) =>
            a.Length == b.Length && a.Zip(b).All(p => p.First.SequenceEqual(p.Second));
    }

    private EditSnapshot TakeSnapshot() => new(
        Song.Tracks.OfType<NoteTrack>().Select(t => t.Notes.ToArray()).ToArray(),
        Song.Tracks.OfType<SmartChordTrack>().Select(t => t.Overrides.ToArray()).ToArray());

    private void RestoreSnapshot(EditSnapshot snapshot)
    {
        var noteTracks = Song.Tracks.OfType<NoteTrack>().ToList();
        for (var i = 0; i < noteTracks.Count && i < snapshot.Notes.Length; i++)
        {
            noteTracks[i].Notes.Clear();
            noteTracks[i].Notes.AddRange(snapshot.Notes[i]);
        }

        var smartTracks = Song.Tracks.OfType<SmartChordTrack>().ToList();
        for (var i = 0; i < smartTracks.Count && i < snapshot.Overrides.Length; i++)
        {
            smartTracks[i].Overrides.Clear();
            smartTracks[i].Overrides.AddRange(snapshot.Overrides[i]);
        }

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
