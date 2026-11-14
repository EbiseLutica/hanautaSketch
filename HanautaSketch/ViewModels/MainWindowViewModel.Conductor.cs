using CommunityToolkit.Mvvm.ComponentModel;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.ViewModels;

/// <summary>
/// テンポ・拍子・キーの変化（Conductor）の編集。
/// ツールバーの BPM・キーは、再生開始位置で効いている変更点の値を表示・編集する。変更はすべて Undo できる。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>ツールバーの表示を Conductor から合わせている最中か（このときの変更通知では編集しない）</summary>
    private bool _syncingConductor;

    [ObservableProperty]
    private int _tonic;

    [ObservableProperty]
    private Mode _mode;

    /// <summary>Conductor が変わったときに増やす。ピアノロールが描き直す</summary>
    [ObservableProperty]
    private int _conductorRevision;

    /// <summary>再生開始位置のテンポ</summary>
    public double Bpm
    {
        get => Song.Conductor.BpmAt(PlayStartTick);
        set
        {
            var conductor = Song.Conductor;
            var current = conductor.Tempos.Last(t => t.Tick <= PlayStartTick);
            if (Math.Abs(current.Bpm - value) < 0.001) return;
            EditConductor(() => conductor.SetTempo(current.Tick, value));
        }
    }

    /// <summary>再生開始位置のキーの変更点を書き換える</summary>
    partial void OnTonicChanged(int value) => EditKeyAtPlayStart();

    partial void OnModeChanged(Mode value) => EditKeyAtPlayStart();

    private void EditKeyAtPlayStart()
    {
        if (_syncingConductor) return;

        var conductor = Song.Conductor;
        var current = conductor.Keys.Last(k => k.Tick <= PlayStartTick);
        var key = new Key(Tonic, Mode);
        if (current.Key == key) return;
        EditConductor(() => conductor.SetKey(current.Tick, key));
    }

    /// <summary>
    /// ツールバーの BPM・キーを、再生開始位置で効いている値に合わせる。
    /// </summary>
    private void SyncConductorDisplay()
    {
        _syncingConductor = true;
        try
        {
            var key = Song.Conductor.KeyAt(PlayStartTick);
            Tonic = key.Tonic;
            Mode = key.Mode;
            OnPropertyChanged(nameof(Bpm));
        }
        finally
        {
            _syncingConductor = false;
        }
    }

    #region 変更点の編集（コンダクターレーンから使う）

    public void SetTempoChange(int tick, double bpm) => EditConductor(() => Song.Conductor.SetTempo(tick, bpm));

    public void SetMeterChange(int bar, int beatsPerBar) =>
        EditConductor(() => Song.Conductor.SetMeter(bar, beatsPerBar));

    public void SetKeyChange(int tick, Key key) => EditConductor(() => Song.Conductor.SetKey(tick, key));

    /// <summary>
    /// 変更点を消す。position は拍子なら小節番号、それ以外は tick。曲頭のものは消せない。
    /// </summary>
    public void RemoveConductorChange(ConductorChangeKind kind, int position) => EditConductor(() =>
    {
        var conductor = Song.Conductor;
        _ = kind switch
        {
            ConductorChangeKind.Tempo => conductor.RemoveTempo(position),
            ConductorChangeKind.Meter => conductor.RemoveMeter(position),
            _ => conductor.RemoveKey(position),
        };
    });

    /// <summary>
    /// 変更点を動かす。移動先に同じ種類の変更点があれば上書きする。曲頭のものは動かせない。
    /// </summary>
    public void MoveConductorChange(ConductorChangeKind kind, int from, int to)
    {
        if (from <= 0 || to <= 0 || from == to) return;

        EditConductor(() =>
        {
            var conductor = Song.Conductor;
            switch (kind)
            {
                case ConductorChangeKind.Tempo:
                    var tempo = conductor.Tempos.First(t => t.Tick == from);
                    conductor.RemoveTempo(from);
                    conductor.SetTempo(to, tempo.Bpm);
                    break;
                case ConductorChangeKind.Meter:
                    var meter = conductor.Meters.First(m => m.Bar == from);
                    conductor.RemoveMeter(from);
                    conductor.SetMeter(to, meter.BeatsPerBar);
                    break;
                default:
                    var key = conductor.Keys.First(k => k.Tick == from);
                    conductor.RemoveKey(from);
                    conductor.SetKey(to, key.Key);
                    break;
            }
        });
    }

    /// <summary>
    /// Conductor への変更を Undo できる1操作にまとめ、表示とスマートトラックを更新する。
    /// </summary>
    private void EditConductor(Action edit)
    {
        OnEditStarted();
        edit();
        OnEditCompleted();
        OnConductorChanged();
    }

    /// <summary>
    /// Conductor が変わったあと（編集・Undo・読み込み・新規作成）に呼ぶ。
    /// </summary>
    private void OnConductorChanged()
    {
        MarkDirty();
        ConductorRevision++;
        SyncConductorDisplay();
        RegenerateSmartTracks();
    }

    #endregion
}
