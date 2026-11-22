using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Editing;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Drums;
using HanautaSketch.Resources;
using HanautaSketch.Services;

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
    /// スマートドラムトラック1本分の編集対象の写し。
    /// </summary>
    private sealed record DrumTrackState(DrumSection[] Sections, Note[] FixedNotes, NoteTrack? BassSource)
    {
        public bool ContentEquals(DrumTrackState other) =>
            Sections.SequenceEqual(other.Sections) && FixedNotes.SequenceEqual(other.FixedNotes)
            && BassSource == other.BassSource;
    }

    /// <summary>
    /// 編集対象の写し。トラック構成（並びと参照）と、各トラックの中身を持つ。
    /// トラックはオブジェクトごと保持するので、削除したトラックも Undo で元に戻せる。
    /// </summary>
    private sealed record EditSnapshot(
        Conductor Conductor,
        Track[] Tracks,
        Dictionary<NoteTrack, Note[]> Notes,
        Dictionary<SmartChordTrack, SmartTrackState> SmartTracks,
        Dictionary<SmartDrumTrack, DrumTrackState> DrumTracks)
    {
        public bool ContentEquals(EditSnapshot other) =>
            Conductor.ContentEquals(other.Conductor)
            && Tracks.SequenceEqual(other.Tracks)
            && Notes.All(p => other.Notes.TryGetValue(p.Key, out var n) && p.Value.SequenceEqual(n))
            && SmartTracks.All(p => other.SmartTracks.TryGetValue(p.Key, out var s) && p.Value.ContentEquals(s))
            && DrumTracks.All(p => other.DrumTracks.TryGetValue(p.Key, out var d) && p.Value.ContentEquals(d));
    }

    private EditSnapshot TakeSnapshot() => new(
        Song.Conductor.Clone(),
        Song.Tracks.ToArray(),
        Song.Tracks.OfType<NoteTrack>().ToDictionary(t => t, t => t.Notes.ToArray()),
        Song.Tracks.OfType<SmartChordTrack>().ToDictionary(
            t => t, t => new SmartTrackState(t.Overrides.ToArray(), t.BassSource, t.MelodySource)),
        Song.Tracks.OfType<SmartDrumTrack>().ToDictionary(
            t => t, t => new DrumTrackState(t.Sections.ToArray(), t.FixedNotes.ToArray(), t.BassSource)));

    private void RestoreSnapshot(EditSnapshot snapshot)
    {
        var selected = SelectedTrack;
        var conductorChanged = !Song.Conductor.ContentEquals(snapshot.Conductor);
        Song.Conductor.CopyFrom(snapshot.Conductor);
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

        foreach (var (track, state) in snapshot.DrumTracks)
        {
            track.Sections.Clear();
            track.Sections.AddRange(state.Sections);
            track.FixedNotes.Clear();
            track.FixedNotes.AddRange(state.FixedNotes);
            track.BassSource = state.BassSource;
        }

        SelectedTrack = selected is not null && Tracks.Contains(selected) ? selected : Tracks.FirstOrDefault();
        OnSelectedTrackChanged(SelectedTrack);
        NotesRevision++;
        if (conductorChanged)
        {
            ConductorRevision++;
            SyncConductorDisplay();
        }
        MarkDirty();
        RegenerateSmartTracks();
    }

    /// <summary>
    /// 一連の編集が始まる直前に呼ぶ。変更前の状態を控える。
    /// </summary>
    public void OnEditStarted()
    {
        // 確定前の連続した変更があれば、別の操作として先に確定しておく
        EndContinuousEdit();
        _pendingSnapshot = TakeSnapshot();
    }

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

    /// <summary>
    /// フォーカスのある文字の入力欄（なければ null）。あるあいだは Undo/Redo をその入力欄に向け、曲の履歴には触らない。
    /// </summary>
    public ITextEditTarget? TextEditTarget
    {
        get;
        set
        {
            field = value;
            NotifyHistoryChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (TextEditTarget is { } text)
        {
            text.Undo();
            return;
        }
        RestoreSnapshot(_history.Undo(TakeSnapshot()));
        NotifyHistoryChanged();
    }

    private bool CanUndo() => TextEditTarget?.CanUndo ?? _history.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (TextEditTarget is { } text)
        {
            text.Redo();
            return;
        }
        RestoreSnapshot(_history.Redo(TakeSnapshot()));
        NotifyHistoryChanged();
    }

    private bool CanRedo() => TextEditTarget?.CanRedo ?? _history.CanRedo;

    /// <summary>取り消す・やり直すの可否が変わったとき（履歴・入力欄の中身・フォーカス）に呼ぶ</summary>
    public void NotifyHistoryChanged()
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
    /// 空の曲にする。トラック構成は既定（Melody / Bass）に戻す。
    /// </summary>
    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await ConfirmDiscardChangesAsync()) return;

        StopPlayback();
        _suppressDirty = true;
        try
        {
            ReplaceTracks(CreateDefaultTracks());
            Song.Conductor.Reset(120, 4, new Key(0, Mode.Major));
            PlayStartTick = 0;
            ConductorRevision++;
            SyncConductorDisplay();
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
        ShowToast(Strings.NewSong);
    }
}
