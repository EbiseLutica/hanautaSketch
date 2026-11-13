using System.Text;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Tests.IO;

public class ProjectSerializerTests
{
    private static async Task<ProjectDocument> RoundTrip(ProjectDocument document)
    {
        using var stream = new MemoryStream();
        await ProjectSerializer.SaveAsync(document, stream);
        stream.Position = 0;
        return await ProjectSerializer.LoadAsync(stream);
    }

    private static async Task<ProjectDocument> LoadJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await ProjectSerializer.LoadAsync(stream);
    }

    [Fact]
    public async Task RoundTrip_PreservesNoteTracks()
    {
        var song = new Song { Bpm = 132, Key = new Key(0, Mode.Minor) };
        var melody = new NoteTrack { Name = "Melody", Program = 0, Volume = 90 };
        melody.Notes.Add(new Note(0, 120, 60, 100));
        melody.Notes.Add(new Note(240, 480, 67, 80));
        var bass = new NoteTrack { Name = "Bass", Program = 33 };
        bass.Notes.Add(new Note(0, 240, 36, 110));
        song.Tracks.Add(melody);
        song.Tracks.Add(bass);

        var loaded = await RoundTrip(new ProjectDocument(song));

        Assert.Equal(132, loaded.Song.Bpm);
        Assert.Equal(new Key(0, Mode.Minor), loaded.Song.Key);

        var tracks = loaded.Song.Tracks.OfType<NoteTrack>().ToList();
        Assert.Equal(["Melody", "Bass"], tracks.Select(t => t.Name).ToArray());
        Assert.Equal(90, tracks[0].Volume);
        Assert.Equal(33, tracks[1].Program);
        Assert.Equal(melody.Notes, tracks[0].Notes);
        Assert.Equal(bass.Notes, tracks[1].Notes);
    }

    [Fact]
    public async Task RoundTrip_PreservesSmartChordTrack()
    {
        var song = new Song();
        var melody = new NoteTrack { Name = "Melody" };
        var bass = new NoteTrack { Name = "Bass" };
        var chord = new SmartChordTrack
        {
            Name = "Chord",
            Program = 48,
            BassSource = bass,
            MelodySource = melody,
            ChordSettings = new SmartChordSettings { MinorDominant = MinorDominantMode.Auto },
            Accompaniment = new AccompanimentSettings { Pattern = AccompanimentPattern.Arpeggio, DivisionTicks = 120, Velocity = 70 },
        };
        chord.SetOverride(960, 960, new Chord(2, ChordQuality.Minor, 5));
        song.Tracks.Add(melody);
        song.Tracks.Add(bass);
        song.Tracks.Add(chord);

        var loaded = await RoundTrip(new ProjectDocument(song));
        var tracks = loaded.Song.Tracks;
        var smart = Assert.IsType<SmartChordTrack>(tracks[2]);

        Assert.Same(tracks[1], smart.BassSource);
        Assert.Same(tracks[0], smart.MelodySource);
        Assert.Equal(48, smart.Program);
        Assert.Equal(MinorDominantMode.Auto, smart.ChordSettings.MinorDominant);
        Assert.Equal(chord.Accompaniment, smart.Accompaniment);
        Assert.Equal(chord.Overrides, smart.Overrides);
    }

    [Fact]
    public async Task LoadV1_ReadsNoteTracksAndIgnoresGlobalChordSettings()
    {
        var loaded = await LoadJson("""
            { "version": 1,
              "song": { "tracks": [ { "type": "note", "name": "Melody" }, { "type": "note", "name": "Bass" } ] },
              "smartChord": { "minorDominant": "major", "useMelody": false } }
            """);

        Assert.Equal(["Melody", "Bass"], loaded.Song.Tracks.Select(t => t.Name).ToArray());
        Assert.All(loaded.Song.Tracks, t => Assert.IsType<NoteTrack>(t));
    }

    [Fact]
    public async Task Load_FillsDefaultsForMissingFields()
    {
        var loaded = await LoadJson("""{ "version": 2, "song": { "tracks": [ { "type": "note", "notes": [ { "tick": 0, "length": 120, "pitch": 60 } ] } ] } }""");

        Assert.Equal(Song.DefaultPpq, loaded.Song.Ppq);
        Assert.Equal(120, loaded.Song.Bpm);
        var note = Assert.Single(Assert.Single(loaded.Song.Tracks.OfType<NoteTrack>()).Notes);
        Assert.Equal(100, note.Velocity);
    }

    [Fact]
    public async Task Load_RejectsNewerVersion()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => LoadJson("""{ "version": 999 }"""));
    }

    [Fact]
    public async Task Load_RejectsBrokenJson()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => LoadJson("{ not json"));
    }
}
