using System.Text;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Drums;

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
    public async Task RoundTrip_PreservesOnionSkinFlag()
    {
        var song = new Song();
        var hidden = new NoteTrack { Name = "Hidden", OnionSkin = false };
        var shown = new NoteTrack { Name = "Shown" };
        song.Tracks.Add(hidden);
        song.Tracks.Add(shown);

        var loaded = await RoundTrip(new ProjectDocument(song));

        var tracks = loaded.Song.Tracks.OfType<NoteTrack>().ToList();
        Assert.False(tracks[0].OnionSkin);
        Assert.True(tracks[1].OnionSkin);
    }

    [Fact]
    public async Task Load_TrackWithoutOnionSkinField_ShowsOnion()
    {
        const string json = """
            {
              "version": "0.6",
              "song": {
                "tracks": [ { "type": "note", "name": "Old", "notes": [] } ]
              }
            }
            """;

        var loaded = await LoadJson(json);

        Assert.True(loaded.Song.Tracks.OfType<NoteTrack>().Single().OnionSkin);
    }

    [Fact]
    public async Task RoundTrip_PreservesNoteTracks()
    {
        var song = new Song();
        song.Conductor.Reset(132, 4, new Key(0, Mode.Minor));
        var melody = new NoteTrack { Name = "Melody", Program = 0, Volume = 90, Mute = true };
        melody.Notes.Add(new Note(0, 120, 60, 100));
        melody.Notes.Add(new Note(240, 480, 67, 80));
        var bass = new NoteTrack { Name = "Bass", Program = 33, Solo = true };
        bass.Notes.Add(new Note(0, 240, 36, 110));
        song.Tracks.Add(melody);
        song.Tracks.Add(bass);

        var loaded = await RoundTrip(new ProjectDocument(song));

        Assert.Equal(132, loaded.Song.Conductor.BpmAt(0));
        Assert.Equal(new Key(0, Mode.Minor), loaded.Song.Conductor.KeyAt(0));

        var tracks = loaded.Song.Tracks.OfType<NoteTrack>().ToList();
        Assert.Equal(["Melody", "Bass"], tracks.Select(t => t.Name).ToArray());
        Assert.Equal(90, tracks[0].Volume);
        Assert.Equal(33, tracks[1].Program);
        Assert.Equal((true, false), (tracks[0].Mute, tracks[0].Solo));
        Assert.Equal((false, true), (tracks[1].Mute, tracks[1].Solo));
        Assert.Equal(melody.Notes, tracks[0].Notes);
        Assert.Equal(bass.Notes, tracks[1].Notes);
    }

    [Fact]
    public async Task RoundTrip_PreservesDrumPartFlag()
    {
        var song = new Song();
        var drums = new NoteTrack { Name = "Drums", IsDrumPart = true, Program = 8 };
        drums.Notes.Add(new Note(0, 120, 36, 100));
        song.Tracks.Add(drums);
        song.Tracks.Add(new NoteTrack { Name = "Bass" });

        var loaded = await RoundTrip(new ProjectDocument(song));

        var tracks = loaded.Song.Tracks.OfType<NoteTrack>().ToList();
        Assert.True(tracks[0].IsDrum);
        Assert.Equal(8, tracks[0].Program);
        Assert.False(tracks[1].IsDrum);
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
        chord.SetOverride(1920, 480, null);
        chord.SetOverride(2400, 480, new Chord(7, ChordQuality.Dominant7Sharp9));
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
    public async Task RoundTrip_PreservesSmartDrumTrack()
    {
        var song = new Song();
        var bass = new NoteTrack { Name = "Bass" };
        var drums = new SmartDrumTrack { Name = "Drums", BassSource = bass, Volume = 90 };
        drums.AddSection(0, 1920);
        var second = drums.AddSection(1920, 3840);
        drums.ReplaceSection(second, second with
        {
            Pattern = DrumPattern.SixteenBeat, Swing = true, SwingAmount = 0.5, BassFollow = DrumBassFollow.Fill,
        });
        song.Tracks.Add(bass);
        song.Tracks.Add(drums);
        drums.Fix(drums.Sections[0], song);

        var loaded = await RoundTrip(new ProjectDocument(song));
        var loadedDrums = Assert.IsType<SmartDrumTrack>(loaded.Song.Tracks[1]);

        Assert.Same(loaded.Song.Tracks[0], loadedDrums.BassSource);
        Assert.Equal(drums.Sections, loadedDrums.Sections);
        Assert.Equal(drums.FixedNotes.OrderBy(n => n.Tick).ThenBy(n => n.Pitch), loadedDrums.FixedNotes);
        Assert.Equal(90, loadedDrums.Volume);
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
        Assert.Equal(120, loaded.Song.Conductor.BpmAt(0));
        var note = Assert.Single(Assert.Single(loaded.Song.Tracks.OfType<NoteTrack>()).Notes);
        Assert.Equal(100, note.Velocity);
    }

    [Fact]
    public async Task Save_WritesVersionAsMajorMinorString()
    {
        using var stream = new MemoryStream();
        await ProjectSerializer.SaveAsync(new ProjectDocument(new Song()), stream);
        var json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("\"version\": \"0.7\"", json);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("\"0.1\"")]
    [InlineData("\"0.2\"")]
    [InlineData("\"0.3\"")]
    [InlineData("\"0.4\"")]
    [InlineData("\"0.5\"")]
    [InlineData("\"0.6\"")]
    [InlineData("\"0.7\"")]
    public async Task Load_AcceptsCurrentAndOlderVersions(string version)
    {
        var loaded = await LoadJson($$"""{ "version": {{version}}, "song": { "tracks": [] } }""");
        Assert.Empty(loaded.Song.Tracks);
    }

    [Theory]
    [InlineData("\"0.8\"")]
    [InlineData("\"1.0\"")]
    [InlineData("999")]
    public async Task Load_RejectsNewerVersion(string version)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => LoadJson($$"""{ "version": {{version}} }"""));
    }

    [Theory]
    [InlineData("true", DrumBassFollow.Full)]
    [InlineData("false", DrumBassFollow.None)]
    public async Task LoadV03_ConvertsFollowBassFlag(string followBass, DrumBassFollow expected)
    {
        var loaded = await LoadJson($$"""
            { "version": "0.3", "song": { "tracks": [
              { "type": "smartDrum", "sections": [ { "start": 0, "length": 1920, "followBass": {{followBass}} } ] }
            ] } }
            """);

        var drums = Assert.IsType<SmartDrumTrack>(Assert.Single(loaded.Song.Tracks));
        Assert.Equal(expected, Assert.Single(drums.Sections).BassFollow);
    }

    [Fact]
    public async Task RoundTrip_PreservesConductorChanges()
    {
        var song = new Song();
        song.Conductor.Reset(100, 4, new Key(2, Mode.Major));
        song.Conductor.SetTempo(1920, 140.5);
        song.Conductor.SetMeter(4, 3);
        song.Conductor.SetKey(3840 + 240, new Key(9, Mode.Minor));

        var loaded = await RoundTrip(new ProjectDocument(song));

        Assert.True(song.Conductor.ContentEquals(loaded.Song.Conductor));
    }

    [Fact]
    public async Task LoadV04_ReadsHeadValuesIntoConductor()
    {
        var loaded = await LoadJson("""
            { "version": "0.4", "song": { "bpm": 90, "beatsPerBar": 3, "key": { "tonic": 4, "mode": "minor" }, "tracks": [] } }
            """);

        var conductor = loaded.Song.Conductor;
        Assert.Equal([new TempoChange(0, 90)], conductor.Tempos);
        Assert.Equal([new MeterChange(0, 3)], conductor.Meters);
        Assert.Equal([new KeyChange(0, new Key(4, Mode.Minor))], conductor.Keys);
    }

    [Fact]
    public async Task Load_RejectsBrokenJson()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => LoadJson("{ not json"));
    }
}
