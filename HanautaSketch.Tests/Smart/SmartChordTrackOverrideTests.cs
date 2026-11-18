using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Tests.Smart;

public class SmartChordTrackOverrideTests
{
    private static readonly Chord A = new(9, ChordQuality.Minor);
    private static readonly Chord B = new(11, ChordQuality.Major);
    private static readonly Chord C = new(0, ChordQuality.Major);

    [Fact]
    public void SetOverride_InsideExisting_SplitsIt()
    {
        var track = new SmartChordTrack();
        track.SetOverride(0, 1920, A);
        track.SetOverride(480, 480, B);

        Assert.Equal([(0, 480, A), (480, 480, B), (960, 960, A)], Summarize(track));
    }

    [Fact]
    public void SetOverride_CoveringExisting_RemovesIt()
    {
        var track = new SmartChordTrack();
        track.SetOverride(480, 480, A);
        track.SetOverride(0, 1920, B);

        Assert.Equal([(0, 1920, B)], Summarize(track));
    }

    [Fact]
    public void ResizeOverride_ExtendsIntoNeighbor_TruncatesNeighbor()
    {
        var track = new SmartChordTrack();
        track.SetOverride(0, 960, A);
        track.SetOverride(960, 960, B);

        var resized = track.ResizeOverride(track.Overrides[0], 0, 1440);

        Assert.Equal(new ChordOverride(0, 1440, A), resized);
        Assert.Equal([(0, 1440, A), (1440, 480, B)], Summarize(track));
    }

    [Fact]
    public void ResizeOverride_ToEmptyRange_ReturnsNullAndKeepsState()
    {
        var track = new SmartChordTrack();
        track.SetOverride(0, 960, A);

        Assert.Null(track.ResizeOverride(track.Overrides[0], 480, 480));
        Assert.Equal([(0, 960, A)], Summarize(track));
    }

    [Fact]
    public void MoveOverride_KeepsLength()
    {
        var track = new SmartChordTrack();
        track.SetOverride(0, 480, A);
        track.SetOverride(960, 480, B);

        var moved = track.MoveOverride(track.Overrides[0], 1920);

        Assert.Equal(new ChordOverride(1920, 480, A), moved);
        Assert.Equal([(960, 480, B), (1920, 480, A)], Summarize(track));
    }

    [Fact]
    public void MoveOverride_IntoNeighbor_SplitsNeighbor()
    {
        var track = new SmartChordTrack();
        track.SetOverride(0, 480, A);
        track.SetOverride(960, 960, B);

        track.MoveOverride(track.Overrides[0], 1200);

        Assert.Equal([(960, 240, B), (1200, 480, A), (1680, 240, B)], Summarize(track));
    }

    [Fact]
    public void Regenerate_UsesResizedRange()
    {
        var song = new Song();
        var track = new SmartChordTrack();
        song.Tracks.Add(track);
        track.SetOverride(0, 960, A);
        track.SetOverride(960, 960, B);
        track.ResizeOverride(track.Overrides[0], 0, 1440);

        track.Regenerate(song);

        var segments = track.Chords.Select(c => (c.Start, c.Length, c.Chord)).ToList();
        Assert.Equal([(0, 1440, (Chord?)A), (1440, 480, (Chord?)B)], segments);
    }

    private static List<(int Start, int Length, Chord Chord)> Summarize(SmartChordTrack track) =>
        track.Overrides.Select(o => (o.Start, o.Length, o.Chord!.Value)).ToList();
}
