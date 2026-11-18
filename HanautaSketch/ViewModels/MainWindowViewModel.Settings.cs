using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Resources;
using HanautaSketch.Services;

namespace HanautaSketch.ViewModels;

/// <summary>
/// 設定画面の項目（SoundFont・テーマ・表示言語）。変えたらすぐ設定に保存して反映する（表示言語だけは次回起動時）。
/// コード遷移データの取得・削除は <c>MainWindowViewModel.ChordData.cs</c>。
/// </summary>
public partial class MainWindowViewModel
{
    private static readonly string[] ThemeValues = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    private static readonly string[] LanguageValues = [AppLanguage.System, AppLanguage.Japanese, AppLanguage.English];

    public IReadOnlyList<string> ThemeOptions { get; } = [Strings.ThemeSystem, Strings.ThemeLight, Strings.ThemeDark];

    public IReadOnlyList<string> LanguageOptions { get; } = [Strings.LanguageSystem, Strings.LanguageJapanese, Strings.LanguageEnglish];

    /// <summary>テーマの選択肢での位置。変えると画面にすぐ反映する</summary>
    public int ThemeIndex
    {
        get => Math.Max(0, Array.IndexOf(ThemeValues, _appSettings.Theme));
        set
        {
            if (value < 0 || value >= ThemeValues.Length || value == ThemeIndex) return;
            UpdateSettings(_appSettings with { Theme = ThemeValues[value] });
            AppTheme.Apply(ThemeValues[value]);
            OnPropertyChanged();
        }
    }

    /// <summary>表示言語の選択肢での位置。画面の文字列は次回起動時から切り替わる</summary>
    public int LanguageIndex
    {
        get => Math.Max(0, Array.IndexOf(LanguageValues, _appSettings.Language));
        set
        {
            if (value < 0 || value >= LanguageValues.Length || value == LanguageIndex) return;
            UpdateSettings(_appSettings with { Language = LanguageValues[value] });
            OnPropertyChanged();
        }
    }

    /// <summary>設定画面に出す SoundFont のパス。未設定なら案内文</summary>
    public string SoundFontDisplay => _appSettings.SoundFontPath ?? Strings.SoundFontNotSet;

    /// <summary>SoundFont が未設定か、設定したファイルが見つからない</summary>
    public bool IsSoundFontMissing => _appSettings.SoundFontPath is not { } path || !File.Exists(path);

    /// <summary>
    /// SoundFont のファイルを選んで設定に保存し、その場で読み込み直す。
    /// </summary>
    [RelayCommand]
    private async Task ChooseSoundFontAsync()
    {
        if (Dialogs is null) return;

        var path = await Dialogs.PickSoundFontAsync();
        if (path is null) return;

        UpdateSettings(_appSettings with { SoundFontPath = path });
        OnPropertyChanged(nameof(SoundFontDisplay));
        await LoadAudioAsync();
    }

    /// <summary>
    /// 起動時に呼ぶ。SoundFont が未設定か見つからなければ、選ぶよう促す。
    /// </summary>
    public async Task PromptSoundFontSetupAsync()
    {
        if (!IsSoundFontMissing || Dialogs is null) return;

        var message = _appSettings.SoundFontPath is { } path
            ? Strings.Format(Strings.SoundFontNotFoundPrompt, path)
            : Strings.SoundFontSetupPrompt;
        if (await Dialogs.ConfirmAsync(Strings.SoundFontLabel, message, Strings.SoundFontChoose, Strings.Later))
            await ChooseSoundFontAsync();
    }

    private void UpdateSettings(AppSettings settings)
    {
        _appSettings = settings;
        AppSettingsStore.Save(settings);
    }
}
