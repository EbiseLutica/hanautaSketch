using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HanautaSketch.Engine.IO;
using HanautaSketch.Views;

namespace HanautaSketch.Services;

/// <summary>
/// 未保存の変更があるときの選択。
/// </summary>
public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// ファイル選択や確認などのダイアログ。VM からウィンドウを直接触らないための窓口。
/// </summary>
public interface IDialogService
{
    /// <summary>開くファイルを選ぶ。キャンセルなら null</summary>
    Task<IStorageFile?> PickOpenAsync();

    /// <summary>保存先を選ぶ。キャンセルなら null</summary>
    Task<IStorageFile?> PickSaveAsync(string suggestedName);

    /// <summary>MIDI の書き出し先を選ぶ。キャンセルなら null</summary>
    Task<IStorageFile?> PickMidiExportAsync(string suggestedName);

    /// <summary>MIDI 書き出しのオプションを選ぶ。キャンセルなら null</summary>
    Task<MidiExportOptions?> PickMidiExportOptionsAsync(MidiExportOptions initial, string fileName);

    /// <summary>未保存の変更をどうするか尋ねる</summary>
    Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string documentName);
}

/// <summary>
/// <see cref="IStorageProvider"/> と、オーナーウィンドウ上のモーダル画面を使った実装。
/// </summary>
public sealed class DialogService(Window owner) : IDialogService
{
    private static readonly FilePickerFileType ProjectFileType = new("hanautaSketch プロジェクト")
    {
        Patterns = [$"*{ProjectSerializer.FileExtension}"],
    };

    private static readonly FilePickerFileType MidiFileType = new("Standard MIDI File")
    {
        Patterns = [$"*{MidiExporter.FileExtension}"],
    };

    public async Task<IStorageFile?> PickOpenAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "プロジェクトを開く",
            AllowMultiple = false,
            FileTypeFilter = [ProjectFileType],
        });
        return files.Count > 0 ? files[0] : null;
    }

    public Task<IStorageFile?> PickSaveAsync(string suggestedName) =>
        owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "プロジェクトを保存",
            SuggestedFileName = suggestedName,
            DefaultExtension = ProjectSerializer.FileExtension.TrimStart('.'),
            FileTypeChoices = [ProjectFileType],
            ShowOverwritePrompt = true,
        });

    public Task<IStorageFile?> PickMidiExportAsync(string suggestedName) =>
        owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "MIDI 書き出し",
            SuggestedFileName = suggestedName,
            DefaultExtension = MidiExporter.FileExtension.TrimStart('.'),
            FileTypeChoices = [MidiFileType],
            ShowOverwritePrompt = true,
        });

    public Task<MidiExportOptions?> PickMidiExportOptionsAsync(MidiExportOptions initial, string fileName) =>
        new MidiExportOptionsWindow(initial, fileName).ShowDialog<MidiExportOptions?>(owner);

    public async Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string documentName) =>
        await new UnsavedChangesWindow(documentName).ShowDialog<UnsavedChangesChoice?>(owner) ?? UnsavedChangesChoice.Cancel;
}
