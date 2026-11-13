using System.Text.Json;
using HanautaSketch.Engine.IO;

namespace HanautaSketch.Services;

/// <summary>
/// 曲に依存しないアプリ全体の設定。
/// </summary>
public sealed record AppSettings
{
    public MidiExportOptions MidiExport { get; init; } = new();
}

/// <summary>
/// アプリ設定を %APPDATA%\hanautaSketch\settings.json に読み書きする。
/// 読めない・壊れている場合は既定値で動き、設定のせいでアプリが止まらないようにする。
/// </summary>
public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hanautaSketch", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(stream, Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// 設定を保存する。失敗しても例外は投げない（書き出しなど本来の操作を妨げないため）。
    /// </summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using var stream = File.Create(FilePath);
            JsonSerializer.Serialize(stream, settings, Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 設定が保存できなくても書き出し自体は成功しているので、黙って続ける
        }
    }
}
