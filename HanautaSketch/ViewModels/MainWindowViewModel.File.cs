using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;
using HanautaSketch.Resources;
using HanautaSketch.Services;

namespace HanautaSketch.ViewModels;

/// <summary>
/// プロジェクトの保存・読み込み、MIDI・WAV の書き出し。
/// </summary>
public partial class MainWindowViewModel
{
    private const string AppName = "hanautaSketch";

    /// <summary>無題の曲の名前（表示言語に従う）</summary>
    private static string UntitledName => Strings.Untitled;

    private IStorageFile? _currentFile;
    private AppSettings _appSettings = AppSettingsStore.Load();
    private bool _suppressDirty;

    /// <summary>View 側から設定するダイアログ</summary>
    public IDialogService? Dialogs { get; set; }

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _message = "";

    public string WindowTitle => $"{(IsDirty ? "*" : "")}{_currentFile?.Name ?? UntitledName} - {AppName}";

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));

    /// <summary>
    /// 例外の詳細文。プロジェクト形式のエラーは Engine の文（英語）ではなく、表示言語の文にする。
    /// </summary>
    private static string ErrorDetail(Exception ex) =>
        ex is InvalidDataException ? Strings.ErrorInvalidProject : ex.Message;

    /// <summary>
    /// 曲に変更があったことを記録する。
    /// </summary>
    public void MarkDirty()
    {
        if (!_suppressDirty) IsDirty = true;
    }

    /// <summary>
    /// 未保存の変更があれば、保存する・保存しない・キャンセルを尋ねる。続行してよければ true。
    /// 「保存する」で保存をキャンセル・失敗した場合も false（変更を失わないため）。
    /// </summary>
    public async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!IsDirty || Dialogs is null) return true;

        switch (await Dialogs.AskUnsavedChangesAsync(_currentFile?.Name ?? UntitledName))
        {
            case UnsavedChangesChoice.Save:
                await SaveAsync();
                return !IsDirty;
            case UnsavedChangesChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (Dialogs is null || !await ConfirmDiscardChangesAsync()) return;

        var file = await Dialogs.PickOpenAsync();
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenReadAsync();
            var document = await ProjectSerializer.LoadAsync(stream);
            ApplyDocument(document);
            SetCurrentFile(file);
            Message = Strings.Format(Strings.FileOpened, file.Name);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Message = Strings.Format(Strings.FileOpenFailed, ErrorDetail(ex));
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
        if (Dialogs is null) return;

        var suggested = _currentFile?.Name ?? UntitledName + ProjectSerializer.FileExtension;
        var file = await Dialogs.PickSaveAsync(suggested);
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
            Message = Strings.Format(Strings.FileSaved, file.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = Strings.Format(Strings.FileSaveFailed, ErrorDetail(ex));
        }
    }

    /// <summary>
    /// 曲を MIDI ファイルに書き出す。保存先を選んだあとにオプションを選ぶ（OS の保存ダイアログは拡張できないため）。
    /// 既定のファイル名はプロジェクト名、オプションの初期値は前回の選択。
    /// </summary>
    [RelayCommand]
    private async Task ExportMidiAsync()
    {
        if (Dialogs is null) return;

        var file = await Dialogs.PickMidiExportAsync(ExportBaseName + MidiExporter.FileExtension);
        if (file is null) return;

        var options = await Dialogs.PickMidiExportOptionsAsync(_appSettings.MidiExport, file.Name);
        if (options is null) return;

        _appSettings = _appSettings with { MidiExport = options };
        AppSettingsStore.Save(_appSettings);

        try
        {
            RegenerateSmartTracks();
            await using (var stream = await file.OpenWriteAsync())
            {
                MidiExporter.Export(Song, stream, options);
                if (stream.CanSeek) stream.SetLength(stream.Position);
            }
            Message = Strings.Format(Strings.FileExported, file.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = Strings.Format(Strings.FileExportFailed, ErrorDetail(ex));
        }
    }

    /// <summary>
    /// 曲を SoundFont で鳴らした音を WAV に書き出す。保存先を選んだあとにオプションを選ぶ。
    /// 曲のスナップショットはこのスレッドで取り、鳴らして書き込むのは裏で行う（書き出し中も編集できる）。
    /// 実行中はコマンドが無効になるので、二重には走らない。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsAudioReady))]
    private async Task ExportWavAsync()
    {
        if (Dialogs is null || _audio is null) return;
        var soundFont = _audio.Engine.SoundFont;

        var file = await Dialogs.PickWavExportAsync(ExportBaseName + WavExporter.FileExtension);
        if (file is null) return;

        var options = await Dialogs.PickWavExportOptionsAsync(_appSettings.WavExport, file.Name);
        if (options is null) return;

        _appSettings = _appSettings with { WavExport = options };
        AppSettingsStore.Save(_appSettings);

        var opened = false;
        try
        {
            RegenerateSmartTracks();
            var export = WavExporter.Prepare(Song, soundFont, options);
            if (!export.FitsInWav)
            {
                Message = Strings.Format(Strings.FileExportFailed, Strings.WavExportTooLong);
                return;
            }

            IsWritingWav = true;
            WavExportStatus = Strings.Format(Strings.WavExporting, file.Name);
            await using (var stream = await file.OpenWriteAsync())
            {
                opened = true;
                await Task.Run(() => export.WriteTo(stream));
                if (stream.CanSeek) stream.SetLength(stream.Position);
            }
            Message = Strings.Format(Strings.FileExported, file.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = Strings.Format(Strings.FileExportFailed, ErrorDetail(ex));

            // 開けなかったときは何も書いていないので、既存のファイルを消さない
            if (opened) await DeleteQuietlyAsync(file);
        }
        finally
        {
            IsWritingWav = false;
            WavExportStatus = "";
        }
    }

    /// <summary>
    /// WAV をファイルに書き込み中か。書き込み中はアプリを終了しない（書きかけの壊れた WAV が残るため）。
    /// </summary>
    [ObservableProperty]
    private bool _isWritingWav;

    /// <summary>WAV の書き込み中の状態（ステータスバーに出し続ける）</summary>
    [ObservableProperty]
    private string _wavExportStatus = "";

    /// <summary>
    /// 書きかけのファイルを消す（壊れた WAV を残さないため）。消せなくても続ける。
    /// </summary>
    private static async Task DeleteQuietlyAsync(IStorageFile file)
    {
        try
        {
            await file.DeleteAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    /// <summary>書き出すファイルの既定の名前（拡張子なし）。プロジェクト名にする</summary>
    private string ExportBaseName =>
        _currentFile is null ? UntitledName : Path.GetFileNameWithoutExtension(_currentFile.Name);

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
            Song.Conductor.CopyFrom(document.Song.Conductor);
            PlayStartTick = 0;
            ConductorRevision++;
            SyncConductorDisplay();
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
