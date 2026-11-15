using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Chords.Corpus;
using HanautaSketch.Services;

namespace HanautaSketch.ViewModels;

/// <summary>
/// コード候補の並びに使う遷移の表。同梱は Billboard（CC0）だけで、Chordonomicon（CC BY-NC 4.0）は
/// 利用者がライセンスに同意したときに、裏で取得・集計して足す（進み具合は状態バー）。
/// </summary>
public partial class MainWindowViewModel
{
    private const string LicenseMessage =
        "コード候補の並びを、より多くの曲（約66万曲）から集計したデータで精度よくします。\n\n" +
        "データセット「Chordonomicon」（S. Kantarelis et al., 2024）を Hugging Face から取得し（約260MB）、" +
        "この PC 上でコードの遷移の回数だけを集計して保存します。取得した元のデータは保存しません。\n\n" +
        "このデータセットは CC BY-NC 4.0（表示 - 非営利）ライセンスで提供されています。" +
        "集計したデータも同じライセンスに従い、非営利の目的でのみ利用できます。\n" +
        "https://creativecommons.org/licenses/by-nc/4.0/deed.ja\n" +
        "https://huggingface.co/datasets/ailsntua/Chordonomicon\n\n" +
        "ライセンスに同意して取得しますか？";

    private const string QuitDuringExtractionMessage =
        "コード情報の抽出中です。アプリケーションを終了すると、抽出中の情報が削除され、" +
        "また最初から抽出を始める必要があります。本当に終了しますか？";

    private CancellationTokenSource? _chordDataCancellation;

    /// <summary>コード候補の並びに使う表</summary>
    public ChordTransitionTable ChordTable { get; private set; } = ChordDataStore.LoadActive();

    /// <summary>Chordonomicon を足した表を使っているか</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcquireChordDataCommand), nameof(RemoveChordDataCommand))]
    private bool _hasExtendedChordData = ChordDataStore.HasExtended;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcquireChordDataCommand), nameof(RemoveChordDataCommand),
        nameof(CancelChordDataCommand))]
    private bool _isExtractingChordData;

    /// <summary>状態バーに出す、取得・集計の進み具合</summary>
    [ObservableProperty]
    private string _chordDataStatus = "";

    /// <summary>ライセンスへの同意を得てから、Chordonomicon の取得・集計を裏で始める</summary>
    [RelayCommand(CanExecute = nameof(CanAcquireChordData))]
    private async Task AcquireChordDataAsync()
    {
        if (Dialogs is null) return;
        if (!await Dialogs.ConfirmAsync("高度なコード遷移データ", LicenseMessage, "同意して取得", "キャンセル")) return;
        _ = ExtractChordDataAsync();
    }

    private bool CanAcquireChordData() => !HasExtendedChordData && !IsExtractingChordData;

    private async Task ExtractChordDataAsync()
    {
        _chordDataCancellation = new CancellationTokenSource();
        IsExtractingChordData = true;
        ChordDataStatus = "コード情報を抽出中…";
        var progress = new Progress<ChordImportProgress>(p => ChordDataStatus = p.TotalBytes is { } total
            ? $"コード情報を抽出中… {p.BytesRead * 100 / Math.Max(1, total)}%（{p.Songs:N0} 曲）"
            : $"コード情報を抽出中…（{p.Songs:N0} 曲）");

        try
        {
            ChordTable = await ChordDataStore.BuildExtendedAsync(progress, _chordDataCancellation.Token);
            HasExtendedChordData = true;
            Message = "コード候補の並びを、追加のデータで精度よくしました";
        }
        catch (OperationCanceledException)
        {
            Message = "コード情報の抽出を中止しました";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Message = $"コード情報の抽出に失敗しました: {ex.Message}";
        }
        finally
        {
            IsExtractingChordData = false;
            ChordDataStatus = "";
            _chordDataCancellation.Dispose();
            _chordDataCancellation = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsExtractingChordData))]
    private void CancelChordData() => _chordDataCancellation?.Cancel();

    /// <summary>追加の表を消し、同梱の表（Billboard のみ）に戻す</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveChordData))]
    private void RemoveChordData()
    {
        ChordDataStore.DeleteExtended();
        ChordTable = ChordTransitionTable.Default;
        HasExtendedChordData = false;
        Message = "追加のコード遷移データを削除しました";
    }

    private bool CanRemoveChordData() => HasExtendedChordData && !IsExtractingChordData;

    /// <summary>
    /// アプリを閉じる前に呼ぶ。抽出中なら確認し、終了するなら抽出を止める。終了してよければ true。
    /// </summary>
    public async Task<bool> ConfirmQuitDuringChordExtractionAsync()
    {
        if (!IsExtractingChordData || Dialogs is null) return true;
        if (!await Dialogs.ConfirmAsync("hanautaSketch", QuitDuringExtractionMessage, "終了する", "キャンセル"))
            return false;

        _chordDataCancellation?.Cancel();
        return true;
    }
}
