using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Editing;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Tests.Editing;

public class TranspositionEditingTests
{
    private static Song CreateSong(params Note[] notes)
    {
        var song = new Song();
        var track = new NoteTrack { Name = "Melody" };
        track.Notes.AddRange(notes);
        song.Tracks.Add(track);
        return song;
    }

    [Fact]
    public void Transpose_ShiftsNotePitch()
    {
        var song = CreateSong(new Note(0, 480, 60, 100), new Note(480, 480, 64, 90));

        Assert.True(TranspositionEditing.Transpose(song, 3));

        var notes = ((NoteTrack)song.Tracks[0]).Notes;
        Assert.Equal(63, notes[0].Pitch);
        Assert.Equal(67, notes[1].Pitch);
        Assert.Equal(100, notes[0].Velocity);
        Assert.Equal(480, notes[1].Tick);
    }

    [Fact]
    public void Transpose_RejectsWhenNoteWouldLeaveRange()
    {
        var song = CreateSong(new Note(0, 480, 125, 100));

        Assert.False(TranspositionEditing.CanTranspose(song, 3));
        Assert.False(TranspositionEditing.Transpose(song, 3));
        Assert.Equal(125, ((NoteTrack)song.Tracks[0]).Notes[0].Pitch);
    }

    [Fact]
    public void Transpose_RejectsMoreThanOctave()
    {
        var song = CreateSong(new Note(0, 480, 60, 100));

        Assert.False(TranspositionEditing.Transpose(song, 13));
    }

    [Fact]
    public void Transpose_ShiftsKeysAndWrapsTonic()
    {
        var song = CreateSong(new Note(0, 480, 60, 100));
        song.Conductor.SetKey(1920, new Key(10, Mode.Minor));

        Assert.True(TranspositionEditing.Transpose(song, 4));

        Assert.Equal(new Key(4, Mode.Major), song.Conductor.KeyAt(0));
        Assert.Equal(new Key(2, Mode.Minor), song.Conductor.KeyAt(1920));
    }

    [Fact]
    public void Transpose_ShiftsChordOverrides()
    {
        var song = CreateSong(new Note(0, 480, 60, 100));
        var smart = new SmartChordTrack();
        smart.Overrides.Add(new ChordOverride(0, 1920, new Chord(9, ChordQuality.Minor, 7)));
        smart.Overrides.Add(new ChordOverride(1920, 1920, null));
        song.Tracks.Add(smart);

        Assert.True(TranspositionEditing.Transpose(song, 5));

        Assert.Equal(new Chord(2, ChordQuality.Minor, 0), smart.Overrides[0].Chord);
        Assert.Null(smart.Overrides[1].Chord);
    }

    [Fact]
    public void Transpose_ZeroIsNoOp()
    {
        var song = CreateSong(new Note(0, 480, 60, 100));

        Assert.False(TranspositionEditing.Transpose(song, 0));
        Assert.Equal(60, ((NoteTrack)song.Tracks[0]).Notes[0].Pitch);
    }
}
