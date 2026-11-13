using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HanautaSketch.Engine.IO;

namespace HanautaSketch.Services;

/// <summary>
/// プロジェクトファイルを選ぶダイアログ。VM から TopLevel を直接触らないための窓口。
/// </summary>
public interface IProjectFileDialog
{
    /// <summary>開くファイルを選ぶ。キャンセルなら null</summary>
    Task<IStorageFile?> PickOpenAsync();

    /// <summary>保存先を選ぶ。キャンセルなら null</summary>
    Task<IStorageFile?> PickSaveAsync(string suggestedName);

    /// <summary>MIDI の書き出し先を選ぶ。キャンセルなら null</summary>
    Task<IStorageFile?> PickMidiExportAsync(string suggestedName);
}

/// <summary>
/// <see cref="IStorageProvider"/> を使った実装。
/// </summary>
public sealed class StorageProjectFileDialog(TopLevel topLevel) : IProjectFileDialog
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
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "プロジェクトを開く",
            AllowMultiple = false,
            FileTypeFilter = [ProjectFileType],
        });
        return files.Count > 0 ? files[0] : null;
    }

    public Task<IStorageFile?> PickSaveAsync(string suggestedName) =>
        topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "プロジェクトを保存",
            SuggestedFileName = suggestedName,
            DefaultExtension = ProjectSerializer.FileExtension.TrimStart('.'),
            FileTypeChoices = [ProjectFileType],
            ShowOverwritePrompt = true,
        });

    public Task<IStorageFile?> PickMidiExportAsync(string suggestedName) =>
        topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "MIDI 書き出し",
            SuggestedFileName = suggestedName,
            DefaultExtension = MidiExporter.FileExtension.TrimStart('.'),
            FileTypeChoices = [MidiFileType],
            ShowOverwritePrompt = true,
        });
}
