using HanautaSketch.Engine.Transcription;
using static HanautaSketch.Tests.Transcription.SyntheticVoice;

namespace HanautaSketch.Tests.Transcription;

public class PitchTrackerTests
{
    [Theory]
    [InlineData(44100, 45.0)]
    [InlineData(44100, 60.0)]
    [InlineData(48000, 69.3)]
    [InlineData(16000, 81.0)]
    public void DetectsPitchOfSteadyTone(int sampleRate, double midi)
    {
        // 安定した音は、どのサンプルレートでも 0.1 半音以内で検出できる
        var samples = Render(sampleRate, new Segment(0.5, midi));
        var voiced = PitchTracker.Track(samples, sampleRate).Where(f => f.IsVoiced).ToList();

        Assert.True(voiced.Count > 30);
        Assert.InRange(NoteSegmenter.Median(voiced.Select(f => f.Midi!.Value).ToList()), midi - 0.1, midi + 0.1);
    }

    [Fact]
    public void SilenceIsUnvoiced()
    {
        // ノイズだけの区間は無声になる
        var samples = Render(44100, new Segment(0.3, null), new Segment(0.3, 64), new Segment(0.3, null));
        var frames = PitchTracker.Track(samples, 44100);

        Assert.All(frames.Where(f => f.Time < 0.25 || f.Time > 0.65), f => Assert.False(f.IsVoiced));
        Assert.All(frames.Where(f => f.Time > 0.35 && f.Time < 0.55), f => Assert.True(f.IsVoiced));
    }
}
