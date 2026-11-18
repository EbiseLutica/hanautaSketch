using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Audio;
using HanautaSketch.Resources;

namespace HanautaSketch.ViewModels;

/// <summary>
/// 再生まわり。SF2 の読み込みは重いので起動後にバックグラウンドで行う。
/// SoundFont は設定（<see cref="Services.AppSettings.SoundFontPath"/>）のものを使い、変えたらその場で読み込み直す。
/// </summary>
public partial class MainWindowViewModel
{
    private IAudioOutput? _audio;

    /// <summary>読み込みの世代。読み込み中に次の読み込みや終了が来たら、古い結果は捨てる</summary>
    private int _audioLoadVersion;
    private DispatcherTimer? _playheadTimer;

    [ObservableProperty]
    private string _audioStatus = Strings.AudioLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePlaybackCommand))]
    private bool _isAudioReady;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>再生ヘッドの位置（tick）。停止中は -1</summary>
    [ObservableProperty]
    private double _playheadTick = -1;

    /// <summary>再生開始位置（tick）。ピアノロールのルーラーで設定する</summary>
    [ObservableProperty]
    private int _playStartTick;

    /// <summary>再生中に開始位置が変わったら、そこから再生し直す</summary>
    partial void OnPlayStartTickChanged(int value)
    {
        SyncConductorDisplay();
        if (IsPlaying) StartPlayback();
    }

    private void InitializePlayback()
    {
        _playheadTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => UpdatePlayhead());
        _ = LoadAudioAsync();
    }

    /// <summary>
    /// 設定の SoundFont で出力を作り直す。今の出力は止めて破棄し、音色名・ドラムキット名も GM の名前に戻してから読み込む。
    /// </summary>
    private async Task LoadAudioAsync()
    {
        var version = ++_audioLoadVersion;
        StopPlayback();
        var previous = _audio;
        _audio = null;
        IsAudioReady = false;
        previous?.Dispose();
        ResetSoundFontNames();

        var path = _appSettings.SoundFontPath;
        if (path is null)
        {
            AudioStatus = Strings.AudioSoundFontNotSet;
            return;
        }
        if (!File.Exists(path))
        {
            AudioStatus = Strings.Format(Strings.AudioSoundFontNotFound, Path.GetFileName(path));
            return;
        }

        AudioStatus = Strings.AudioLoading;
        try
        {
            var audio = await Task.Run(() => AudioOutput.Create(path));
            if (version != _audioLoadVersion)
            {
                audio.Dispose();
                return;
            }

            _audio = audio;
            AudioStatus = Path.GetFileName(path);
            IsAudioReady = true;
            ApplyPresetNames(audio.Engine.GetPresetNames());
            ApplyDrumKitNames(audio.Engine.GetDrumKitNames());
        }
        catch (Exception ex)
        {
            if (version == _audioLoadVersion)
                AudioStatus = Strings.Format(Strings.AudioInitFailed, ErrorDetail(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(IsAudioReady))]
    private void TogglePlayback()
    {
        if (_audio is null) return;

        if (_audio.Engine.IsPlaying)
        {
            StopPlayback();
            return;
        }

        StartPlayback();
    }

    private void StartPlayback()
    {
        if (_audio is null) return;
        _audio.Engine.Play(Song, PlayStartTick);
        IsPlaying = true;
        _playheadTimer?.Start();
    }

    /// <summary>
    /// 入力中の確認音。選択中トラックの音色で鳴らす。空なら止める。
    /// </summary>
    public void PreviewNotes(IReadOnlyList<(int Pitch, int Velocity)> notes)
    {
        if (_audio is null) return;
        if (notes.Count > 0)
            _audio.Engine.PreviewNotesOn(SelectedTrack?.Program ?? 0, notes, SelectedTrack?.IsDrum ?? false);
        else
            _audio.Engine.PreviewNoteOff();
    }

    private void StopPlayback()
    {
        _audio?.Engine.Stop();
        IsPlaying = false;
        PlayheadTick = -1;
        _playheadTimer?.Stop();
    }

    private void UpdatePlayhead()
    {
        if (_audio is null) return;

        if (!_audio.Engine.IsPlaying)
        {
            StopPlayback();
            return;
        }

        PlayheadTick = _audio.Engine.PositionTick;
    }

    public void Dispose()
    {
        _playheadTimer?.Stop();
        _audioLoadVersion++;
        _audio?.Dispose();
    }
}
