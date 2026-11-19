using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Transcription;

namespace HanautaSketch.Tests.Transcription;

public class PitchSnapperTests
{
    private static readonly Key CMajor = new(0, Mode.Major);

    [Theory]
    // スケールを見ないので、いちばん近い半音
    [InlineData(61.1, PitchSnapMode.Chromatic, 61)]
    [InlineData(60.6, PitchSnapMode.Chromatic, 61)]
    // はっきり C# と歌っていれば残す
    [InlineData(61.1, PitchSnapMode.Lean, 61)]
    // C# 寄りだが D との境界付近なので D に倒す
    [InlineData(61.3, PitchSnapMode.Lean, 62)]
    // C# 寄りだが C との境界付近なので C に倒す
    [InlineData(60.7, PitchSnapMode.Lean, 60)]
    // スケール音に近ければそのまま
    [InlineData(59.6, PitchSnapMode.Lean, 60)]
    // 必ずスケール音
    [InlineData(61.0, PitchSnapMode.Scale, 60)]
    [InlineData(61.6, PitchSnapMode.Scale, 62)]
    [InlineData(65.8, PitchSnapMode.Scale, 65)]
    [InlineData(66.2, PitchSnapMode.Scale, 67)]
    public void SnapsByMode(double midi, PitchSnapMode mode, int expected)
    {
        Assert.Equal(expected, PitchSnapper.Snap(midi, CMajor, mode));
    }

    [Fact]
    public void FollowsKey()
    {
        // A minor では G# はスケール外、A# もスケール外。G#寄りで A との境界付近なら A に倒す
        var aMinor = new Key(9, Mode.Minor);
        Assert.Equal(68, PitchSnapper.Snap(68.0, aMinor, PitchSnapMode.Lean));
        Assert.Equal(69, PitchSnapper.Snap(68.3, aMinor, PitchSnapMode.Lean));
    }

    [Fact]
    public void EstimatesTuningOffset()
    {
        // 全体に +30 セントずれて歌っている
        var notes = new[] { 60.3, 62.28, 64.32, 65.3 }
            .Select((m, i) => new SungNote(i, i + 0.5, m))
            .ToList();
        Assert.InRange(PitchSnapper.EstimateTuningOffset(notes), 0.28, 0.32);
    }

    [Fact]
    public void TuningOffsetWrapsAroundHalfSemitone()
    {
        // +45 セントと -45 セント（= +55 セント）が混ざっても、平均は ±0.5 付近になり 0 付近にはならない
        var notes = new[] { 60.45, 61.55 }.Select((m, i) => new SungNote(i, i + 0.5, m)).ToList();
        Assert.True(Math.Abs(PitchSnapper.EstimateTuningOffset(notes)) > 0.45);
    }
}
