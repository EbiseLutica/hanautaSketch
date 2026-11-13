using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Drums;

namespace HanautaSketch.Tests.Smart;

public class SmartDrumTrackTests
{
    private const int Ppq = 480;
    private const int Bar = Ppq * 4;
    private const int Step = Bar / 16;

    private static int[] TicksOf(IEnumerable<Note> notes, int pitch) =>
        notes.Where(n => n.Pitch == pitch).Select(n => n.Tick).ToArray();

    [Fact]
    public void EightBeat_HasKickSnareHatAndCrash()
    {
        var notes = DrumGenerator.Generate(new DrumSection { Start = 0, Length = Bar }, [], Ppq, Bar);

        Assert.Equal([0, 8 * Step], TicksOf(notes, GmDrum.Kick));
        Assert.Equal([4 * Step, 12 * Step], TicksOf(notes, GmDrum.Snare));
        Assert.Equal(8, TicksOf(notes, GmDrum.ClosedHiHat).Length);
        Assert.Equal([0], TicksOf(notes, GmDrum.Crash));
    }

    [Fact]
    public void Pattern_RepeatsEveryBar()
    {
        var notes = DrumGenerator.Generate(
            new DrumSection { Start = Bar, Length = Bar * 2, Pattern = DrumPattern.FourOnTheFloor }, [], Ppq, Bar);

        Assert.Equal(Enumerable.Range(0, 8).Select(i => Bar + i * Ppq).ToArray(), TicksOf(notes, GmDrum.Kick));
        Assert.Equal([Bar], TicksOf(notes, GmDrum.Crash));
    }

    [Fact]
    public void Swing_DelaysOffbeatEighths()
    {
        var section = new DrumSection { Start = 0, Length = Bar, Swing = true, SwingAmount = 1.0 };
        var hats = TicksOf(DrumGenerator.Generate(section, [], Ppq, Bar), GmDrum.ClosedHiHat);

        // 裏の8分が 1/2 拍 → 2/3 拍（240 → 320）へ
        Assert.Contains(320, hats);
        Assert.DoesNotContain(240, hats);
        Assert.Contains(0, hats);
    }

    [Fact]
    public void Swing_SixteenBeatDelaysOffbeatSixteenths()
    {
        var section = new DrumSection
        {
            Start = 0, Length = Bar, Pattern = DrumPattern.SixteenBeat, Swing = true, SwingAmount = 1.0,
        };
        var hats = TicksOf(DrumGenerator.Generate(section, [], Ppq, Bar), GmDrum.ClosedHiHat);

        // 16分の裏が 1/4 拍 → 1/3 拍（120 → 160）へ
        Assert.Contains(160, hats);
        Assert.DoesNotContain(120, hats);
    }

    [Fact]
    public void FollowBass_ReplacesKicksWithBassOnsetsExceptOnSnare()
    {
        // 3+3+2 のベース＋スネアと重なる 2拍目
        List<Note> bass = [new(0, Step, 36, 100), new(3 * Step, Step, 36, 100), new(4 * Step, Step, 36, 100), new(6 * Step, Step, 36, 100)];
        var section = new DrumSection { Start = 0, Length = Bar, FollowBass = true };
        var kicks = TicksOf(DrumGenerator.Generate(section, bass, Ppq, Bar), GmDrum.Kick);

        Assert.Equal([0, 3 * Step, 6 * Step], kicks);
    }

    [Fact]
    public void Fix_CopiesGeneratedNotesAndUnfixDiscardsThem()
    {
        var song = new Song();
        var drums = new SmartDrumTrack();
        var section = drums.AddSection(0, Bar);
        song.Tracks.Add(drums);
        drums.Regenerate(song);
        var generated = drums.PlaybackNotes.ToList();

        var fixedSection = drums.Fix(section, song);
        Assert.True(fixedSection.IsFixed);
        Assert.Equal(generated, drums.FixedNotes.OrderBy(n => n.Tick).ThenBy(n => n.Pitch));
        Assert.True(drums.CanPlaceAt(Ppq));

        drums.FixedNotes.Add(new Note(Ppq, 120, GmDrum.Snare, 90));
        drums.Regenerate(song);
        Assert.Equal(generated.Count + 1, drums.PlaybackNotes.Count);

        drums.Unfix(fixedSection);
        Assert.Empty(drums.FixedNotes);
        Assert.False(drums.CanPlaceAt(Ppq));
    }

    [Fact]
    public void AddSection_TrimsOverlappingSections()
    {
        var drums = new SmartDrumTrack();
        drums.AddSection(0, Bar * 4);
        drums.AddSection(Bar, Bar * 2);

        Assert.Equal([(0, Bar), (Bar, Bar), (Bar * 2, Bar * 2)],
            drums.Sections.Select(s => (s.Start, s.Length)).ToArray());
    }
}
