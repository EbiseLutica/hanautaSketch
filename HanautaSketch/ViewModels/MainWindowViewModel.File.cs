using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;
using HanautaSketch.Services;

namespace HanautaSketch.ViewModels;

/// <summary>
/// プロジェクトの保存・読み込み。
/// </summary>
public partial class MainWindowViewModel
{
    private const string AppName = "hanautaSketch";
    private const string UntitledName = "無題";

    private IStorageFile? _currentFile;
    private bool _suppressDirty;

    /// <summary>View 側から設定するファイルダイアログ</summary>
    public IProjectFileDialog? FileDialog { get; set; }

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _message = "";

    public string WindowTitle => $"{(IsDirty ? "*" : "")}{_currentFile?.Name ?? UntitledName} - {AppName}";

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));

    /// <summary>
    /// 曲に変更があったことを記録する。
    /// </summary>
    public void MarkDirty()
    {
        if (!_suppressDirty) IsDirty = true;
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (FileDialog is null) return;

        var file = await FileDialog.PickOpenAsync();
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenReadAsync();
            var document = await ProjectSerializer.LoadAsync(stream);
            ApplyDocument(document);
            SetCurrentFile(file);
            Message = $"{file.Name} を開いた";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Message = $"開けなかった: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_currentFile is null)
        {
            await SaveAsAsync();
            return;
        }

        await WriteAsync(_currentFile);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (FileDialog is null) return;

        var suggested = _currentFile?.Name ?? UntitledName + ProjectSerializer.FileExtension;
        var file = await FileDialog.PickSaveAsync(suggested);
        if (file is null) return;

        await WriteAsync(file);
    }

    private async Task WriteAsync(IStorageFile file)
    {
        try
        {
            await using (var stream = await file.OpenWriteAsync())
            {
                await ProjectSerializer.SaveAsync(CreateDocument(), stream);

                // 既存ファイルより短くなった場合に末尾が残らないよう切り詰める
                if (stream.CanSeek) stream.SetLength(stream.Position);
            }

            SetCurrentFile(file);
            Message = $"{file.Name} に保存した";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"保存できなかった: {ex.Message}";
        }
    }

    /// <summary>
    /// 曲を MIDI ファイルに書き出す。既定のファイル名はプロジェクト名。
    /// </summary>
    [RelayCommand]
    private async Task ExportMidiAsync()
    {
        if (FileDialog is null) return;

        var baseName = _currentFile is null ? UntitledName : Path.GetFileNameWithoutExtension(_currentFile.Name);
        var file = await FileDialog.PickMidiExportAsync(baseName + MidiExporter.FileExtension);
        if (file is null) return;

        try
        {
            RegenerateSmartTracks();
            await using (var stream = await file.OpenWriteAsync())
            {
                MidiExporter.Export(Song, stream);
                if (stream.CanSeek) stream.SetLength(stream.Position);
            }
            Message = $"{file.Name} に書き出した";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"書き出せなかった: {ex.Message}";
        }
    }

    private void SetCurrentFile(IStorageFile file)
    {
        _currentFile = file;
        IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
    }

    private ProjectDocument CreateDocument() => new(Song);

    /// <summary>
    /// 読み込んだ内容を現在の曲に反映する。トラックは丸ごと差し替える。
    /// </summary>
    private void ApplyDocument(ProjectDocument document)
    {
        StopPlayback();
        _suppressDirty = true;
        try
        {
            ReplaceTracks(document.Song.Tracks);
            Bpm = document.Song.Bpm;
            Song.BeatsPerBar = document.Song.BeatsPerBar;
            Tonic = document.Song.Key.Tonic;
            Mode = document.Song.Key.Mode;
            PlayStartTick = 0;
            ClearHistory();
            NotesRevision++;
            RegenerateSmartTracks();
        }
        finally
        {
            _suppressDirty = false;
        }
    }
}
