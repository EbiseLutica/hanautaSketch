using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Core;
using HanautaSketch.Resources;

namespace HanautaSketch.ViewModels;

/// <summary>
/// タップテンポ。タップで候補 BPM を出し、確定すると再生開始位置の BPM に反映する（Undo 可能）。
/// </summary>
public partial class MainWindowViewModel
{
    private readonly TapTempo _tapTempo = new();
    private readonly Stopwatch _tapClock = Stopwatch.StartNew();

    /// <summary>測定中の候補 BPM。測定していなければ null</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TapStatus))]
    [NotifyCanExecuteChangedFor(nameof(ApplyTapCommand))]
    private double? _tapBpm;

    /// <summary>候補 BPM の表示文字列</summary>
    public string TapStatus => TapBpm is { } bpm
        ? Strings.Format(Strings.TapCandidate, bpm.ToString("0.#", CultureInfo.InvariantCulture))
        : "";

    /// <summary>タップを1回記録し、候補 BPM を更新する</summary>
    [RelayCommand]
    private void Tap()
    {
        _tapTempo.Tap(_tapClock.Elapsed.TotalSeconds);
        TapBpm = _tapTempo.Bpm;
    }

    /// <summary>候補 BPM を再生開始位置の BPM にして、測定を終える</summary>
    [RelayCommand(CanExecute = nameof(CanApplyTap))]
    private void ApplyTap()
    {
        if (TapBpm is not { } bpm) return;
        Bpm = bpm;
        ResetTap();
    }

    private bool CanApplyTap => TapBpm.HasValue;

    private void ResetTap()
    {
        _tapTempo.Reset();
        TapBpm = null;
    }
}
