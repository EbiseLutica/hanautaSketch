using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Chords.Corpus;
using HanautaSketch.Resources;
using HanautaSketch.Services;

namespace HanautaSketch.ViewModels;

/// <summary>
/// コード候補の並びに使う遷移の表。同梱は Billboard（CC0）だけで、Chordonomicon（CC BY-NC 4.0）は
/// 利用者がライセンスに同意したときに、裏で取得・集計して足す（進み具合は状態バー）。
/// </summary>
public partial class MainWindowViewModel
{
    private CancellationTokenSource? _chordDataCancellation;

    /// <summary>コード候補の並びに使う表</summary>
    public ChordTransitionTable ChordTable { get; private set; } = ChordDataStore.LoadActive();

    /// <summary>Chordonomicon を足した表を使っているか</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcquireChordDataCommand), nameof(RemoveChordDataCommand))]
    [NotifyPropertyChangedFor(nameof(ChordDataState))]
    private bool _hasExtendedChordData = ChordDataStore.HasExtended;

    /// <summary>設定画面に出す、使っている表の説明</summary>
    public string ChordDataState => HasExtendedChordData ? Strings.ChordDataStateExtended : Strings.ChordDataStateBundled;

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
        if (!await Dialogs.ConfirmAsync(Strings.ChordDataTitle, Strings.ChordDataLicense, Strings.ChordDataAgree, Strings.Cancel)) return;
        _ = ExtractChordDataAsync();
    }

    private bool CanAcquireChordData() => !HasExtendedChordData && !IsExtractingChordData;

    private async Task ExtractChordDataAsync()
    {
        _chordDataCancellation = new CancellationTokenSource();
        IsExtractingChordData = true;
        ChordDataStatus = Strings.ChordDataExtracting;
        var progress = new Progress<ChordImportProgress>(p => ChordDataStatus = p.TotalBytes is { } total
            ? Strings.Format(Strings.ChordDataProgress, p.BytesRead * 100 / Math.Max(1, total), p.Songs)
            : Strings.Format(Strings.ChordDataCount, p.Songs));

        try
        {
            ChordTable = await ChordDataStore.BuildExtendedAsync(progress, _chordDataCancellation.Token);
            HasExtendedChordData = true;
            Message = Strings.ChordDataDone;
        }
        catch (OperationCanceledException)
        {
            Message = Strings.ChordDataCancelled;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Message = Strings.Format(Strings.ChordDataFailed, ex.Message);
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
        Message = Strings.ChordDataRemoved;
    }

    private bool CanRemoveChordData() => HasExtendedChordData && !IsExtractingChordData;

    /// <summary>
    /// アプリを閉じる前に呼ぶ。抽出中なら確認し、終了するなら抽出を止める。終了してよければ true。
    /// </summary>
    public async Task<bool> ConfirmQuitDuringChordExtractionAsync()
    {
        if (!IsExtractingChordData || Dialogs is null) return true;
        if (!await Dialogs.ConfirmAsync("hanautaSketch", Strings.QuitDuringExtraction, Strings.QuitAnyway, Strings.Cancel))
            return false;

        _chordDataCancellation?.Cancel();
        return true;
    }
}
