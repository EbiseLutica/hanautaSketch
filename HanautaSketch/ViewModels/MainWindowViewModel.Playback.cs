using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Audio;

namespace HanautaSketch.ViewModels;

/// <summary>
/// 再生まわり。SF2 の読み込みは重いので起動後にバックグラウンドで行う。
/// </summary>
public partial class MainWindowViewModel
{
    private const string SoundFontDirectory = "SoundFonts";

    private AudioOutput? _audio;
    private DispatcherTimer? _playheadTimer;

    [ObservableProperty]
    private string _audioStatus = "音源を読み込み中…";

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
        if (IsPlaying) StartPlayback();
    }

    public double Bpm
    {
        get => Song.Bpm;
        set
        {
            if (Math.Abs(Song.Bpm - value) < 0.001) return;
            Song.Bpm = Math.Clamp(value, 20, 400);
            MarkDirty();
            OnPropertyChanged();
        }
    }

    private void InitializePlayback()
    {
        _playheadTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => UpdatePlayhead());
        _ = LoadAudioAsync();
    }

    private async Task LoadAudioAsync()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, SoundFontDirectory);
        var path = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.sf2").FirstOrDefault() : null;
        if (path is null)
        {
            AudioStatus = $"SF2 が見つからない（{SoundFontDirectory}/ に置く）";
            return;
        }

        try
        {
            _audio = await Task.Run(() => AudioOutput.Create(path));
            AudioStatus = Path.GetFileName(path);
            IsAudioReady = true;
            ApplyPresetNames(_audio.Engine.GetPresetNames());
        }
        catch (Exception ex)
        {
            AudioStatus = $"音源の初期化に失敗: {ex.Message}";
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
            _audio.Engine.PreviewNotesOn(SelectedTrack?.Program ?? 0, notes);
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
        _audio?.Dispose();
    }
}
