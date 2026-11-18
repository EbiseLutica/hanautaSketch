using HanautaSketch.Engine.Core;

namespace HanautaSketch.Tests.Core;

public class TapTempoTests
{
    [Fact]
    public void NeedsTwoTaps()
    {
        var tap = new TapTempo();
        Assert.Null(tap.Bpm);

        tap.Tap(0);
        Assert.Null(tap.Bpm);

        tap.Tap(0.5);
        Assert.Equal(120, tap.Bpm);
    }

    [Fact]
    public void AveragesIntervals()
    {
        // 間隔 0.5 秒、0.6 秒、0.4 秒 → 合計 1.5 秒で 3 間隔 → 0.5 秒平均 → 120 BPM
        var tap = new TapTempo();
        foreach (var t in new[] { 0.0, 0.5, 1.1, 1.5 }) tap.Tap(t);
        Assert.Equal(120, tap.Bpm);
    }

    [Fact]
    public void UsesOnlyRecentTaps()
    {
        // 最初の間隔は 1 秒（60 BPM）、その後の間隔は 0.5 秒（120 BPM）。直近 8 回だけ見るので 120 になる
        var tap = new TapTempo();
        tap.Tap(0);
        var time = 1.0;
        tap.Tap(time);
        for (var i = 0; i < TapTempo.WindowSize + 1; i++)
        {
            time += 0.5;
            tap.Tap(time);
        }
        Assert.Equal(TapTempo.WindowSize, tap.TapCount);
        Assert.Equal(120, tap.Bpm);
    }

    [Fact]
    public void RestartsAfterLongGap()
    {
        var tap = new TapTempo();
        tap.Tap(0);
        tap.Tap(0.5);
        tap.Tap(0.5 + TapTempo.RestartGapSeconds + 0.1);
        Assert.Equal(1, tap.TapCount);
        Assert.Null(tap.Bpm);
    }

    [Fact]
    public void Reset_ClearsTaps()
    {
        var tap = new TapTempo();
        tap.Tap(0);
        tap.Tap(0.5);
        tap.Reset();
        Assert.Equal(0, tap.TapCount);
        Assert.Null(tap.Bpm);
    }
}
