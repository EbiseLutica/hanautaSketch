using HanautaSketch.Engine.Audio;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.Tests.Audio;

public class MetronomeClicksTests
{
    private static Conductor CreateConductor(double bpm = 120, int beatsPerBar = 4)
    {
        var conductor = new Conductor();
        conductor.Reset(bpm, beatsPerBar, new Key(0, Mode.Major));
        return conductor;
    }

    [Fact]
    public void CountInPrecedesStart()
    {
        // 120 BPM で 4 拍のカウントイン：開始位置の 2 秒前から 0.5 秒ごと。最初の拍だけ強拍
        var clicks = MetronomeClicks.Build(CreateConductor(), 1920, new PlaybackOptions { CountInBeats = 4 }, 1920 * 4);

        Assert.Equal([-2.0, -1.5, -1.0, -0.5], clicks.Select(c => c.Seconds));
        Assert.Equal([true, false, false, false], clicks.Select(c => c.IsAccent));
    }

    [Fact]
    public void CountInUsesTempoAtStart()
    {
        // 開始位置（2 小節目）で 60 BPM に変わるなら、カウントインは 1 秒ごと
        var conductor = CreateConductor();
        conductor.SetTempo(1920, 60);

        var clicks = MetronomeClicks.Build(conductor, 1920, new PlaybackOptions { CountInBeats = 2 }, 1920);

        Assert.Equal([-2.0, -1.0], clicks.Select(c => c.Seconds));
    }

    [Fact]
    public void MetronomeFollowsBeatsAndBars()
    {
        // 3 拍子・120 BPM で、拍の途中（240）から始める。最初のクリックは次の拍（480）で、小節の頭（1440）が強拍
        var conductor = CreateConductor(beatsPerBar: 3);

        var clicks = MetronomeClicks.Build(conductor, 240, new PlaybackOptions { Metronome = true }, 1920);

        Assert.Equal([0.25, 0.75, 1.25], clicks.Select(c => c.Seconds));
        Assert.Equal([false, false, true], clicks.Select(c => c.IsAccent));
    }

    [Fact]
    public void NoClicksByDefault()
    {
        Assert.Empty(MetronomeClicks.Build(CreateConductor(), 0, new PlaybackOptions(), 1920));
    }
}
