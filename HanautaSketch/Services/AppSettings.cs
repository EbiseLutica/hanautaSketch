using System.Text.Json;
using System.Text.Json.Serialization;
using HanautaSketch.Engine.IO;

namespace HanautaSketch.Services;

/// <summary>
/// 曲に依存しないアプリ全体の設定。
/// </summary>
public sealed record AppSettings
{
    /// <summary>表示言語。<see cref="AppLanguage.System"/>（OS に従う）・<see cref="AppLanguage.Japanese"/>・<see cref="AppLanguage.English"/></summary>
    public string Language { get; init; } = AppLanguage.System;

    /// <summary>配色テーマ。<see cref="AppTheme.System"/>（OS に従う）・<see cref="AppTheme.Light"/>・<see cref="AppTheme.Dark"/></summary>
    public string Theme { get; init; } = AppTheme.System;

    /// <summary>再生に使う SoundFont（.sf2）のパス。未設定なら null</summary>
    public string? SoundFontPath { get; init; }

    /// <summary>ピアノロールのスケールロック（スケール外の音を置けないモード）</summary>
    public bool ScaleLock { get; init; }

    public MidiExportOptions MidiExport { get; init; } = new();
}

/// <summary>
/// アプリ設定を %APPDATA%\hanautaSketch\settings.json に読み書きする。
/// 読めない・壊れている場合は既定値で動き、設定のせいでアプリが止まらないようにする。
/// </summary>
public static class AppSettingsStore
{
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hanautaSketch", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize(stream, AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();
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
            JsonSerializer.Serialize(stream, settings, AppSettingsJsonContext.Default.AppSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 設定が保存できなくても書き出し自体は成功しているので、黙って続ける
        }
    }
}

/// <summary>
/// 設定ファイルの JSON シリアライザー（NativeAOT で動くようソース生成する）。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
