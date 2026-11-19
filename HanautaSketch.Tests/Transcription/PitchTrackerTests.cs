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

    [Theory]
    [InlineData(34.0)]
    [InlineData(35.0)]
    public void BelowRangeIsUnvoiced(double midi)
    {
        // 検出範囲（65Hz）より低い音は、範囲の端の音高で誤って有声にせず、無声にする
        var samples = Render(44100, new Segment(0.5, midi));
        Assert.All(PitchTracker.Track(samples, 44100), f => Assert.False(f.IsVoiced));
    }

    [Fact]
    public void AnalyzesUntilEndOfClip()
    {
        // 録音の最後まで伸ばした音も、末尾近くまでフレームがある
        var samples = Render(44100, new Segment(1.0, 64));
        var last = PitchTracker.Track(samples, 44100).Last(f => f.IsVoiced);
        Assert.True(last.Time > 0.95, $"last voiced frame at {last.Time}");
    }

    [Fact]
    public void LoudBurstDoesNotSilenceQuietVoice()
    {
        // 小さめの声の途中に咳のような大きなノイズがあっても、声は無声にならない
        var samples = Render(
            44100,
            new Segment(1.0, 64, Amplitude: 0.008),
            new Segment(0.1, null, NoiseBurst: 0.9),
            new Segment(1.0, 64, Amplitude: 0.008));
        var voiced = PitchTracker.Track(samples, 44100).Count(f => f.IsVoiced);
        Assert.True(voiced > 150, $"voiced frames: {voiced}");
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
