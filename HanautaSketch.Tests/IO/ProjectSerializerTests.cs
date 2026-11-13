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
    public async Task RoundTrip_PreservesSongAndSettings()
    {
        var song = new Song { Bpm = 132, Key = new Key(0, Mode.Minor) };
        var melody = new NoteTrack { Name = "Melody", Program = 0, Volume = 90 };
        melody.Notes.Add(new Note(0, 120, 60, 100));
        melody.Notes.Add(new Note(240, 480, 67, 80));
        var bass = new NoteTrack { Name = "Bass", Program = 33 };
        bass.Notes.Add(new Note(0, 240, 36, 110));
        song.Tracks.Add(melody);
        song.Tracks.Add(bass);

        var loaded = await RoundTrip(new ProjectDocument(song, MinorDominantMode.Auto, false));

        Assert.Equal(132, loaded.Song.Bpm);
        Assert.Equal(new Key(0, Mode.Minor), loaded.Song.Key);
        Assert.Equal(MinorDominantMode.Auto, loaded.MinorDominant);
        Assert.False(loaded.UseMelodyForChords);

        var tracks = loaded.Song.Tracks.OfType<NoteTrack>().ToList();
        Assert.Equal(["Melody", "Bass"], tracks.Select(t => t.Name).ToArray());
        Assert.Equal(90, tracks[0].Volume);
        Assert.Equal(33, tracks[1].Program);
        Assert.Equal(melody.Notes, tracks[0].Notes);
        Assert.Equal(bass.Notes, tracks[1].Notes);
    }

    [Fact]
    public async Task Load_FillsDefaultsForMissingFields()
    {
        var loaded = await LoadJson("""{ "version": 1, "song": { "tracks": [ { "type": "note", "notes": [ { "tick": 0, "length": 120, "pitch": 60 } ] } ] } }""");

        Assert.Equal(Song.DefaultPpq, loaded.Song.Ppq);
        Assert.Equal(120, loaded.Song.Bpm);
        var note = Assert.Single(loaded.Song.Tracks.OfType<NoteTrack>().Single().Notes);
        Assert.Equal(100, note.Velocity);
        Assert.True(loaded.UseMelodyForChords);
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
