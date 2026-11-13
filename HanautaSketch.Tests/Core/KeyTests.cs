using HanautaSketch.Engine.Core;

namespace HanautaSketch.Tests.Core;

public class KeyTests
{
    [Fact]
    public void CMajor_ScaleIsWhiteKeys()
    {
        var key = new Key(0, Mode.Major);
        Assert.Equal([0, 2, 4, 5, 7, 9, 11], key.GetScalePitchClasses());
    }

    [Fact]
    public void AMinor_SharesPitchClassesWithCMajor()
    {
        var aMinor = new Key(9, Mode.Minor);
        Assert.Equal([9, 11, 0, 2, 4, 5, 7], aMinor.GetScalePitchClasses());
    }

    [Theory]
    [InlineData(60, true)]   // C4
    [InlineData(61, false)]  // C#4
    [InlineData(71, true)]   // B4
    public void Contains_JudgesByPitchClass(int pitch, bool expected)
    {
        Assert.Equal(expected, new Key(0, Mode.Major).Contains(pitch));
    }
}
