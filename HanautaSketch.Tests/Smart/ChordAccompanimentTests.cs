using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Tests.Smart;

public class ChordAccompanimentTests
{
    private const int Ppq = 480;
    private const int Bar = Ppq * 4;

    private static readonly Chord C = new(0, ChordQuality.Major);
    private static readonly Chord F = new(5, ChordQuality.Major);
    private static readonly Chord G = new(7, ChordQuality.Major);

    private static List<ChordSegment> Segments(params Chord[] chords) =>
        chords.Select((c, i) => new ChordSegment(i * Bar, Bar, c, false)).ToList();

    [Fact]
    public void Voice_StaysInRangeAndPrefersSmallMotion()
    {
        var c = ChordAccompaniment.Voice(C, null);
        var f = ChordAccompaniment.Voice(F, c);

        Assert.All(c.Concat(f), p => Assert.InRange(p, 53, 74));
        Assert.Equal([60, 64, 67], c);
        Assert.Equal([60, 65, 69], f);
    }

    [Fact]
    public void Voice_IgnoresInversionBassForUpperVoices()
    {
        var withBass = ChordAccompaniment.Voice(C with { Bass = 4 }, null);
        Assert.Equal(ChordAccompaniment.Voice(C, null), withBass);
    }

    [Fact]
    public void Sustain_HoldsWholeSegment()
    {
        var notes = ChordAccompaniment.Generate(Segments(C, G), [], new AccompanimentSettings(), Ppq);

        Assert.Equal(6, notes.Count);
        Assert.All(notes, n => Assert.Equal(Bar, n.Length));
        Assert.Equal([0, Bar], notes.Select(n => n.Tick).Distinct().ToArray());
    }

    [Fact]
    public void Beat_RepeatsAtDivision()
    {
        var settings = new AccompanimentSettings { Pattern = AccompanimentPattern.Beat, DivisionTicks = Ppq };
        var notes = ChordAccompaniment.Generate(Segments(C), [], settings, Ppq);

        Assert.Equal([0, Ppq, Ppq * 2, Ppq * 3], notes.Select(n => n.Tick).Distinct().ToArray());
        Assert.All(notes, n => Assert.Equal(Ppq, n.Length));
    }

    [Fact]
    public void FollowBass_PlaysAtBassOnsets()
    {
        // 3+3+2 のベース。区間頭にも発音がある
        const int s = Ppq / 4;
        List<Note> bass = [new(0, s * 2, 36, 100), new(s * 3, s, 36, 100), new(s * 6, s * 2, 36, 100)];
        var settings = new AccompanimentSettings { Pattern = AccompanimentPattern.FollowBass };
        var notes = ChordAccompaniment.Generate([new ChordSegment(0, Ppq * 2, C, false)], bass, settings, Ppq);

        var hits = notes.GroupBy(n => n.Tick).Select(g => (g.Key, g.First().Length)).ToArray();
        Assert.Equal([(0, s * 3), (s * 3, s * 3), (s * 6, s * 2)], hits);
    }

    [Fact]
    public void Arpeggio_CyclesUpThroughChordTones()
    {
        var settings = new AccompanimentSettings { Pattern = AccompanimentPattern.Arpeggio, DivisionTicks = Ppq / 2 };
        var notes = ChordAccompaniment.Generate([new ChordSegment(0, Ppq * 3, C, false)], [], settings, Ppq);

        Assert.Equal([60, 64, 67, 72, 60, 64], notes.Select(n => n.Pitch).ToArray());
    }
}
