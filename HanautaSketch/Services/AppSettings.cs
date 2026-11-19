using System.Text.Json;
using System.Text.Json.Serialization;
using HanautaSketch.Engine.IO;
using HanautaSketch.Engine.Transcription;

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

    public MidiExportOptions MidiExport { get; init; } = new();

    public RecordingSettings Recording { get; init; } = new();
}

/// <summary>
/// 鼻歌の録音の設定（ツールバーの録音ボタン横のポップアップで変える）。
/// </summary>
public sealed record RecordingSettings
{
    /// <summary>入力デバイスの名前。null なら既定のデバイス</summary>
    public string? DeviceName { get; init; }

    /// <summary>録音中に伴奏（録音先以外のトラック）を鳴らすか</summary>
    public bool PlayAccompaniment { get; init; } = true;

    /// <summary>録音中にクリックを鳴らすか（カウントインは常に鳴らす）</summary>
    public bool Metronome { get; init; } = true;

    /// <summary>量子化のグリッド（tick）</summary>
    public int GridTicks { get; init; } = 120;

    /// <summary>スケールの扱い</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PitchSnapMode>))]
    public PitchSnapMode SnapMode { get; init; } = PitchSnapMode.Lean;

    /// <summary>レイテンシ補正（ミリ秒）。歌が録音に入るまでの遅れなど、自動で差し引く分以外の遅れ</summary>
    public int LatencyMilliseconds { get; init; }

    /// <summary>
    /// 歌った高さのまま起こすか。false（既定）なら全体をオクターブ単位でずらして C4〜C6 に合わせる。
    /// ファイルにない項目は false で読まれるので、既定が false になる向きで持つ
    /// </summary>
    public bool KeepSungOctave { get; init; }
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
            return Normalize(JsonSerializer.Deserialize(stream, AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// ファイルにない項目を既定値で埋める。ソース生成の読み込みは、ファイルにない init プロパティを既定値ではなく null / 0 にするため
    /// （録音の設定を足す前の settings.json には recording がない）。
    /// </summary>
    private static AppSettings Normalize(AppSettings settings)
    {
        var recording = settings.Recording ?? new RecordingSettings();
        if (recording.GridTicks <= 0) recording = recording with { GridTicks = new RecordingSettings().GridTicks };
        return settings with
        {
            MidiExport = settings.MidiExport ?? new MidiExportOptions(),
            Recording = recording,
        };
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
