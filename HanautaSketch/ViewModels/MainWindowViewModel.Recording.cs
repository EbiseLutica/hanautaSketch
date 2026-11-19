using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Audio;
using HanautaSketch.Controls;
using HanautaSketch.Engine.Audio;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;
using HanautaSketch.Engine.Transcription;
using HanautaSketch.Resources;
using HanautaSketch.Services;

namespace HanautaSketch.ViewModels;

/// <summary>
/// 量子化のグリッドの選択肢。
/// </summary>
public sealed record RecordingGridOption(string Label, int Ticks);

/// <summary>
/// 鼻歌の録音。再生開始位置の1小節前からカウントインし、開始位置から停止までを録音範囲として、
/// 選択中のノートトラックの範囲内のノーツを採譜結果で置き換える（Undo 1回で戻せる）。
/// 録音中は録音先のトラックを鳴らさず、ピアノロールに 100ms ごとの音量を重ねて描く。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>音量のオーバーレイの1区間の長さ（秒）</summary>
    private const double LevelBlockSeconds = 0.1;

    /// <summary>音量のオーバーレイで 0 にあたる音量（dB）</summary>
    private const double LevelFloorDb = -60;

    private IAudioInput? _recordingInput;
    private NoteTrack? _recordingTrack;
    private int _recordingStartTick;

    /// <summary>録音の先頭のサンプルが、曲のどの時刻（秒）にあたるか（出力の遅れは差し引き済み）</summary>
    private double _recordingClipStartSeconds;

    /// <summary>音量のオーバーレイに取り込み済みのサンプル数</summary>
    private int _levelSamplesConsumed;

    private readonly List<RecordingLevel> _recordingLevels = [];

    /// <summary>録音中（カウントインを含む）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    private bool _isRecording;

    /// <summary>停止後の採譜中</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    private bool _isTranscribing;

    /// <summary>ピアノロールに重ねる録音中の音量。録音していないときは null</summary>
    [ObservableProperty]
    private IReadOnlyList<RecordingLevel>? _recordingLevelsDisplay;

    /// <summary>NotesRevision を変えるときに、ピアノロールで選択状態にするノーツ</summary>
    [ObservableProperty]
    private IReadOnlyCollection<Note>? _notesToSelect;

    /// <summary>
    /// 直前の録音と、採譜に使った条件。採譜の調整用に WAV で書き出せるよう取っておく。
    /// </summary>
    private sealed record RecordingTake(
        AudioClip Clip, double ClipStartSeconds, int StartTick, int StopTick, TranscriptionOptions Options, Conductor Conductor);

    private RecordingTake? _lastRecording;

    #region 設定（ポップアップ）

    private RecordingSettings RecordingSettings => _appSettings.Recording;

    private void UpdateRecordingSettings(RecordingSettings settings) =>
        UpdateSettings(_appSettings with { Recording = settings });

    /// <summary>OpenAL Soft がデバイス名の頭に付ける文字列。表示では省く</summary>
    private const string OpenAlDevicePrefix = "OpenAL Soft on ";

    /// <summary>入力デバイスの本来の名前（設定に保存し、デバイスを開くときに使う）。<see cref="RecordingDevices"/> の 2 つ目以降と同じ並び</summary>
    private IReadOnlyList<string> _recordingDeviceNames = [];

    /// <summary>入力デバイスの選択肢（表示名）。先頭は既定のデバイス</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _recordingDevices = [Strings.RecordingDefaultDevice];

    /// <summary>
    /// 入力デバイスの一覧を取り直す。ポップアップを開くたびに呼ぶ（マイクの抜き差しに追従するため）。
    /// </summary>
    public void RefreshRecordingDevices()
    {
        IReadOnlyList<string> names;
        try
        {
            names = AudioInput.GetDeviceNames();
        }
        catch (Exception)
        {
            names = [];
        }
        _recordingDeviceNames = names;
        RecordingDevices =
        [
            Strings.RecordingDefaultDevice,
            .. names.Select(n => n.StartsWith(OpenAlDevicePrefix, StringComparison.Ordinal) ? n[OpenAlDevicePrefix.Length..] : n),
        ];
        OnPropertyChanged(nameof(RecordingDeviceIndex));
    }

    /// <summary>入力デバイスの選択肢での位置。保存したデバイスが見つからなければ既定（0）</summary>
    public int RecordingDeviceIndex
    {
        get
        {
            var name = RecordingSettings.DeviceName;
            if (name is null) return 0;
            var index = _recordingDeviceNames.ToList().IndexOf(name);
            return index < 0 ? 0 : index + 1;
        }
        set
        {
            if (value < 0 || value >= RecordingDevices.Count || value == RecordingDeviceIndex) return;
            UpdateRecordingSettings(RecordingSettings with { DeviceName = value == 0 ? null : _recordingDeviceNames[value - 1] });
            OnPropertyChanged();
        }
    }

    public bool RecordingPlayAccompaniment
    {
        get => RecordingSettings.PlayAccompaniment;
        set
        {
            if (value == RecordingPlayAccompaniment) return;
            UpdateRecordingSettings(RecordingSettings with { PlayAccompaniment = value });
            OnPropertyChanged();
        }
    }

    public bool RecordingMetronome
    {
        get => RecordingSettings.Metronome;
        set
        {
            if (value == RecordingMetronome) return;
            UpdateRecordingSettings(RecordingSettings with { Metronome = value });
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<RecordingGridOption> RecordingGridOptions { get; } =
    [
        new(Strings.RecordingGrid8th, Song.DefaultPpq / 2),
        new(Strings.RecordingGrid16th, Song.DefaultPpq / 4),
        new(Strings.RecordingGrid8thTriplet, Song.DefaultPpq / 3),
        new(Strings.RecordingGrid16thTriplet, Song.DefaultPpq / 6),
    ];

    public RecordingGridOption RecordingGrid
    {
        get => RecordingGridOptions.FirstOrDefault(o => o.Ticks == RecordingSettings.GridTicks) ?? RecordingGridOptions[1];
        set
        {
            if (value is null || value.Ticks == RecordingSettings.GridTicks) return;
            UpdateRecordingSettings(RecordingSettings with { GridTicks = value.Ticks });
            OnPropertyChanged();
        }
    }

    private static readonly PitchSnapMode[] SnapModeValues = [PitchSnapMode.Chromatic, PitchSnapMode.Lean, PitchSnapMode.Scale];

    public IReadOnlyList<string> RecordingScaleOptions { get; } =
        [Strings.RecordingScaleOff, Strings.RecordingScaleLean, Strings.RecordingScaleSnap];

    public int RecordingScaleIndex
    {
        get => Math.Max(0, Array.IndexOf(SnapModeValues, RecordingSettings.SnapMode));
        set
        {
            if (value < 0 || value >= SnapModeValues.Length || value == RecordingScaleIndex) return;
            UpdateRecordingSettings(RecordingSettings with { SnapMode = SnapModeValues[value] });
            OnPropertyChanged();
        }
    }

    public bool RecordingFitOctave
    {
        get => !RecordingSettings.KeepSungOctave;
        set
        {
            if (value == RecordingFitOctave) return;
            UpdateRecordingSettings(RecordingSettings with { KeepSungOctave = !value });
            OnPropertyChanged();
        }
    }

    public decimal RecordingLatency
    {
        get => RecordingSettings.LatencyMilliseconds;
        set
        {
            var ms = (int)Math.Clamp(value, 0, 1000);
            if (ms == RecordingSettings.LatencyMilliseconds) return;
            UpdateRecordingSettings(RecordingSettings with { LatencyMilliseconds = ms });
            OnPropertyChanged();
        }
    }

    #endregion

    [RelayCommand(CanExecute = nameof(CanToggleRecording))]
    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
            await StopRecordingAsync();
        else
            StartRecording();
    }

    private bool CanToggleRecording() => IsRecording || (IsAudioReady && !IsTranscribing);

    private void StartRecording()
    {
        // キー操作からは CanExecute を通らずに呼ばれるので、ここでも確かめる
        if (_audio is null || IsTranscribing) return;
        if (SelectedTrack is not NoteTrack track)
        {
            Message = Strings.RecordingNeedsNoteTrack;
            return;
        }

        StopPlayback();
        IAudioInput input;
        try
        {
            input = AudioInput.Start(RecordingSettings.DeviceName);
        }
        catch (Exception ex)
        {
            Message = Strings.Format(Strings.RecordingStartFailed, ErrorDetail(ex));
            return;
        }

        _recordingInput = input;
        _recordingTrack = track;
        _recordingStartTick = PlayStartTick;
        _recordingLevels.Clear();
        _levelSamplesConsumed = 0;
        RecordingLevelsDisplay = [];

        // 鳴らし始めた時点で取り込み済みだったサンプル数から、録音の先頭が曲のどの時刻にあたるかを求める
        var captured = input.CapturedSampleCount;
        _audio.Engine.Play(Song, _recordingStartTick, new PlaybackOptions
        {
            CountInBeats = Song.Conductor.BeatsPerBarAt(_recordingStartTick),
            Metronome = RecordingSettings.Metronome,
            PlaySong = RecordingSettings.PlayAccompaniment,
            ExcludedTrack = track,
            Continuous = true,
        });
        _recordingClipStartSeconds = _audio.Engine.PlaybackStartSeconds
            - (double)captured / input.SampleRate
            - _audio.LatencySeconds;

        IsRecording = true;
        IsPlaying = true;
        Message = Strings.RecordingInProgress;
        _playheadTimer?.Start();
    }

    /// <summary>
    /// 録音を止めて採譜し、録音範囲のノーツを置き換える。カウントイン中に止めたときは何も変えない。
    /// </summary>
    private async Task StopRecordingAsync()
    {
        if (_recordingInput is not { } input || _recordingTrack is not { } track) return;

        var stopTick = (int)Math.Round(_audio?.Engine.PositionTick ?? 0);
        var startTick = _recordingStartTick;
        var clipStart = _recordingClipStartSeconds;
        EndRecordingSession();
        var samples = input.Stop();
        input.Dispose();

        if (stopTick <= startTick)
        {
            RecordingLevelsDisplay = null;
            Message = Strings.RecordingCancelled;
            return;
        }

        IsTranscribing = true;
        Message = Strings.Transcribing;
        var options = new TranscriptionOptions
        {
            GridTicks = RecordingSettings.GridTicks,
            SnapMode = RecordingSettings.SnapMode,
            LatencySeconds = RecordingSettings.LatencyMilliseconds / 1000.0,
            FitOctave = !RecordingSettings.KeepSungOctave,
        };
        var conductor = Song.Conductor.Clone();
        var clip = new AudioClip(samples, input.SampleRate);
        _lastRecording = new RecordingTake(clip, clipStart, startTick, stopTick, options, conductor);
        SaveLastRecordingCommand.NotifyCanExecuteChanged();
        IReadOnlyList<Note> notes;
        try
        {
            notes = await Task.Run(() => HummingTranscriber.Transcribe(clip, conductor, options, clipStart, startTick, stopTick));
        }
        catch (Exception ex)
        {
            Message = Strings.Format(Strings.TranscriptionFailed, ErrorDetail(ex));
            return;
        }
        finally
        {
            IsTranscribing = false;
            RecordingLevelsDisplay = null;
        }

        if (!Song.Tracks.Contains(track))
        {
            Message = Strings.TranscriptionTrackRemoved;
            return;
        }

        OnEditStarted();
        HummingTranscriber.ReplaceRange(track.Notes, startTick, stopTick, notes);
        OnEditCompleted();

        SelectedTrack = track;
        NotesToSelect = notes.ToArray();
        NotesRevision++;
        NotesToSelect = null;
        OnNotesEdited();
        Message = Strings.Format(Strings.TranscriptionDone, notes.Count);
    }

    /// <summary>
    /// 直前の録音を WAV で保存する。採譜の調整用に、録音と曲の時刻の対応や採譜の設定を同名の .txt に書き添える。
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasLastRecording))]
    private async Task SaveLastRecordingAsync()
    {
        if (_lastRecording is not { } last || Dialogs is null) return;
        var file = await Dialogs.PickRecordingSaveAsync($"recording-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
        if (file is null) return;

        try
        {
            await using (var stream = await file.OpenWriteAsync())
            {
                stream.SetLength(0);
                WavWriter.Write(stream, last.Clip);
            }
            if (file.TryGetLocalPath() is { } path)
                await File.WriteAllTextAsync(Path.ChangeExtension(path, ".txt"), DescribeRecording(last));
            Message = Strings.Format(Strings.RecordingSaved, file.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = Strings.Format(Strings.RecordingSaveFailed, ErrorDetail(ex));
        }
    }

    private bool HasLastRecording() => _lastRecording is not null;

    /// <summary>録音を採譜し直すのに要る情報を、key=value の行で書く</summary>
    private static string DescribeRecording(RecordingTake last)
    {
        var c = last.Conductor;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        string F(double value) => value.ToString("0.######", invariant);
        var lines = new List<string>
        {
            $"sampleRate={last.Clip.SampleRate}",
            $"clipStartSeconds={F(last.ClipStartSeconds)}",
            $"rangeStartTick={last.StartTick}",
            $"rangeEndTick={last.StopTick}",
            $"ppq={c.Ppq}",
            $"tempos={string.Join(",", c.Tempos.Select(t => $"{t.Tick}:{F(t.Bpm)}"))}",
            $"meters={string.Join(",", c.Meters.Select(m => $"{m.Bar}:{m.BeatsPerBar}"))}",
            $"keys={string.Join(",", c.Keys.Select(k => $"{k.Tick}:{k.Key.Tonic}:{k.Key.Mode}"))}",
            $"gridTicks={last.Options.GridTicks}",
            $"snapMode={last.Options.SnapMode}",
            $"latencySeconds={F(last.Options.LatencySeconds)}",
            $"fitOctave={last.Options.FitOctave}",
        };
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>
    /// 録音を止めて、取り込んだ分を捨てる（録音中に再生が止められたときなど）。
    /// </summary>
    private void CancelRecording()
    {
        if (_recordingInput is not { } input) return;
        EndRecordingSession();
        input.Dispose();
        RecordingLevelsDisplay = null;
        Message = Strings.RecordingCancelled;
    }

    /// <summary>再生と録音の状態を止める（入力デバイスはまだ閉じない）</summary>
    private void EndRecordingSession()
    {
        _recordingInput = null;
        _recordingTrack = null;
        IsRecording = false;
        _audio?.Engine.Stop();
        IsPlaying = false;
        PlayheadTick = -1;
        _playheadTimer?.Stop();
    }

    /// <summary>
    /// 取り込んだ録音から、100ms ごとの音量を足していく。録音範囲より前（カウントイン）は描かない。
    /// </summary>
    private void UpdateRecordingLevels()
    {
        if (_recordingInput is not { } input) return;
        var blockSize = (int)(input.SampleRate * LevelBlockSeconds);
        var samples = input.CopySamples(_levelSamplesConsumed);
        var blocks = samples.Length / blockSize;
        if (blocks == 0) return;

        var conductor = Song.Conductor;
        var latency = RecordingSettings.LatencyMilliseconds / 1000.0;
        for (var b = 0; b < blocks; b++)
        {
            var sum = 0.0;
            for (var i = b * blockSize; i < (b + 1) * blockSize; i++) sum += samples[i] * samples[i];
            var rms = Math.Sqrt(sum / blockSize);
            var db = rms > 0 ? 20 * Math.Log10(rms) : LevelFloorDb;
            var level = Math.Clamp((db - LevelFloorDb) / -LevelFloorDb, 0, 1);

            var startSeconds = _recordingClipStartSeconds + (double)(_levelSamplesConsumed + b * blockSize) / input.SampleRate - latency;
            var startTick = conductor.SecondsToTick(startSeconds);
            if (startTick < _recordingStartTick) continue;
            var endTick = conductor.SecondsToTick(startSeconds + LevelBlockSeconds);
            _recordingLevels.Add(new RecordingLevel(startTick, endTick, level));
        }
        _levelSamplesConsumed += blocks * blockSize;
        RecordingLevelsDisplay = _recordingLevels.ToArray();
    }
}
