using HanautaSketch.Engine.Core;

namespace HanautaSketch.Tests.Core;

public class ConductorTests
{
    private const int Ppq = 480;

    [Fact]
    public void Bars_FollowMeterChanges()
    {
        // 4/4 ×2小節 → 3/4 ×1小節 → 5/4
        var conductor = new Conductor(Ppq);
        conductor.SetMeter(2, 3);
        conductor.SetMeter(3, 5);

        Assert.Equal([0, 1920, 3840, 5280, 7680], Enumerable.Range(0, 5).Select(conductor.BarToTick).ToArray());
        Assert.Equal(2, conductor.BarAt(3840));
        Assert.Equal(2, conductor.BarAt(5279));
        Assert.Equal(3, conductor.BarAt(5280));
        Assert.Equal(3 * Ppq, conductor.TicksPerBarAt(4000));
        Assert.Equal(480, conductor.PositionInBar(4320));
        Assert.Equal([3840, 5280], conductor.BarStarts(3000, 7680).ToArray());
        Assert.Equal(5280, conductor.CeilToBar(3841));
        Assert.Equal(3840, conductor.RoundToBar(4300));
    }

    [Fact]
    public void Time_FollowsTempoChanges()
    {
        // 120 BPM で 4拍（2秒）→ 60 BPM
        var conductor = new Conductor(Ppq);
        conductor.SetTempo(Ppq * 4, 60);

        Assert.Equal(2.0, conductor.TickToSeconds(Ppq * 4), 6);
        Assert.Equal(3.0, conductor.TickToSeconds(Ppq * 5), 6);
        Assert.Equal(Ppq * 5, conductor.SecondsToTick(3.0), 6);
        Assert.Equal(Ppq * 2, conductor.SecondsToTick(1.0), 6);
    }

    [Fact]
    public void Changes_ReplaceAtSamePositionAndKeepHeadChange()
    {
        var conductor = new Conductor(Ppq);
        conductor.SetKey(1920, new Key(7, Mode.Major));
        conductor.SetKey(1920, new Key(9, Mode.Minor));

        Assert.Equal(2, conductor.Keys.Count);
        Assert.Equal(new Key(9, Mode.Minor), conductor.KeyAt(2000));
        Assert.Equal(new Key(0, Mode.Major), conductor.KeyAt(1919));
        Assert.Equal([(0, 1920), (1920, 3000)], conductor.KeyRegions(3000).Select(r => (r.Start, r.End)).ToArray());

        Assert.False(conductor.RemoveKey(0));
        Assert.True(conductor.RemoveKey(1920));
        Assert.Single(conductor.Keys);
    }
}
