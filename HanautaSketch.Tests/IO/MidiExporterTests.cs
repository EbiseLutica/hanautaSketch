using System.Text;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;
using HanautaSketch.Engine.Smart.Chords;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using DryNote = Melanchall.DryWetMidi.Interaction.Note;
using Note = HanautaSketch.Engine.Core.Note;

namespace HanautaSketch.Tests.IO;

/// <summary>
/// 書き出した SMF を DryWetMIDI（独立した実装）で読み戻して検証する。
/// </summary>
public class MidiExporterTests
{
    private static MidiFile ExportAndRead(Song song, MidiExportOptions? options = null)
    {
        using var stream = new MemoryStream();
        MidiExporter.Export(song, stream, options);
        stream.Position = 0;
        return MidiFile.Read(stream, new ReadingSettings { TextEncoding = MidiExporter.DefaultTextEncoding });
    }

    private static Song CreateSong()
    {
        var song = new Song { Bpm = 132, Key = new Key(0, Mode.Minor) };
        var melody = new NoteTrack { Name = "メロディ", Program = 0, Volume = 90 };
        melody.Notes.Add(new Note(0, 240, 72, 100));
        melody.Notes.Add(new Note(240, 240, 72, 80)); // 同じ音の連打
        var bass = new NoteTrack { Name = "Bass", Program = 33 };
        bass.Notes.Add(new Note(0, 1920, 36, 110));
        song.Tracks.Add(melody);
        song.Tracks.Add(bass);
        return song;
    }

    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(0x7F, new byte[] { 0x7F })]
    [InlineData(0x80, new byte[] { 0x81, 0x00 })]
    [InlineData(0x3FFF, new byte[] { 0xFF, 0x7F })]
    [InlineData(0x4000, new byte[] { 0x81, 0x80, 0x00 })]
    [InlineData(0x0FFFFFFF, new byte[] { 0xFF, 0xFF, 0xFF, 0x7F })]
    public void VariableLength_EncodesPerSpec(int value, byte[] expected)
    {
        Span<byte> buffer = stackalloc byte[4];
        var written = SmfTrackBuilder.WriteVariableLength(buffer, value);
        Assert.Equal(expected, buffer[..written].ToArray());
    }

    [Fact]
    public void Header_IsFormat1WithConductorAndTracks()
    {
        var midi = ExportAndRead(CreateSong());

        Assert.Equal(MidiFileFormat.MultiTrack, midi.OriginalFormat);
        Assert.Equal(480, ((TicksPerQuarterNoteTimeDivision)midi.TimeDivision).TicksPerQuarterNote);
        Assert.Equal(3, midi.GetTrackChunks().Count());
    }

    [Fact]
    public void Conductor_HasTempoTimeSignatureAndKey()
    {
        var conductor = ExportAndRead(CreateSong()).GetTrackChunks().First();
        var events = conductor.Events;

        var tempo = Assert.Single(events.OfType<SetTempoEvent>());
        Assert.Equal(Math.Round(60_000_000 / 132.0), tempo.MicrosecondsPerQuarterNote);

        var meter = Assert.Single(events.OfType<TimeSignatureEvent>());
        Assert.Equal((4, 4), (meter.Numerator, meter.Denominator));

        // C minor = フラット3つ
        var key = Assert.Single(events.OfType<KeySignatureEvent>());
        Assert.Equal((-3, 1), (key.Key, key.Scale));
    }

    [Theory]
    [InlineData(0, Mode.Major, 0)]
    [InlineData(7, Mode.Major, 1)]
    [InlineData(6, Mode.Major, 6)]
    [InlineData(5, Mode.Major, -1)]
    [InlineData(1, Mode.Major, -5)]
    [InlineData(9, Mode.Minor, 0)]
    [InlineData(4, Mode.Minor, 1)]
    [InlineData(2, Mode.Minor, -1)]
    public void KeySignature_CountsAccidentals(int tonic, Mode mode, int expected)
    {
        var song = new Song { Key = new Key(tonic, mode) };
        var key = ExportAndRead(song).GetTrackChunks().First().Events.OfType<KeySignatureEvent>().Single();
        Assert.Equal(expected, key.Key);
    }

    [Fact]
    public void Tracks_HaveNameProgramVolumeAndNotes()
    {
        var chunks = ExportAndRead(CreateSong()).GetTrackChunks().ToList();
        var melody = chunks[1];
        var bass = chunks[2];

        Assert.Equal("メロディ", melody.Events.OfType<SequenceTrackNameEvent>().Single().Text);
        Assert.Equal(0, (int)melody.Events.OfType<ProgramChangeEvent>().Single().ProgramNumber);
        var volume = melody.Events.OfType<ControlChangeEvent>().Single();
        Assert.Equal((7, 90), ((int)volume.ControlNumber, (int)volume.ControlValue));

        DryNote[] melodyNotes = [.. melody.GetNotes()];
        Assert.Equal([(0L, 240L, 72, 100), (240L, 240L, 72, 80)],
            melodyNotes.Select(n => (n.Time, n.Length, (int)n.NoteNumber, (int)n.Velocity)).ToArray());
        Assert.All(melodyNotes, n => Assert.Equal(0, (int)n.Channel));

        var bassNote = Assert.Single(bass.GetNotes());
        Assert.Equal((0L, 1920L, 1), (bassNote.Time, bassNote.Length, (int)bassNote.Channel));
        Assert.Equal(33, (int)bass.Events.OfType<ProgramChangeEvent>().Single().ProgramNumber);
    }

    [Fact]
    public void Channels_SkipDrumChannel()
    {
        var song = new Song();
        for (var i = 0; i < 11; i++)
        {
            var track = new NoteTrack { Name = $"T{i}" };
            track.Notes.Add(new Note(0, 120, 60, 100));
            song.Tracks.Add(track);
        }

        var channels = ExportAndRead(song).GetTrackChunks().Skip(1)
            .Select(c => (int)c.GetNotes().Single().Channel).ToArray();
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11], channels);
    }

    [Fact]
    public void DrumTrack_UsesChannel10WithoutProgramChange()
    {
        var song = CreateSong();
        var drums = new HanautaSketch.Engine.Smart.Drums.SmartDrumTrack { Name = "Drums" };
        drums.AddSection(0, 1920);
        song.Tracks.Insert(0, drums);
        drums.Regenerate(song);

        var chunks = ExportAndRead(song).GetTrackChunks().ToList();

        Assert.All(chunks[1].GetNotes(), n => Assert.Equal(9, (int)n.Channel));
        Assert.Empty(chunks[1].Events.OfType<ProgramChangeEvent>()); // Standard キットは指定しない
        // 後ろのメロディ・ベースは 1ch・2ch のまま
        Assert.Equal(0, (int)chunks[2].GetNotes().First().Channel);
        Assert.Equal(1, (int)chunks[3].GetNotes().First().Channel);
    }

    [Fact]
    public void DrumTrack_ExportsKitProgramChangeExceptStandard()
    {
        var song = new Song();
        var drums = new HanautaSketch.Engine.Smart.Drums.SmartDrumTrack { Name = "Drums", Program = 25 };
        drums.AddSection(0, 1920);
        song.Tracks.Add(drums);
        drums.Regenerate(song);

        var program = ExportAndRead(song).GetTrackChunks().ElementAt(1).Events.OfType<ProgramChangeEvent>().Single();
        Assert.Equal((25, 9), ((int)program.ProgramNumber, (int)program.Channel));

        var withoutProgram = ExportAndRead(song, new MidiExportOptions { IncludeProgramChange = false });
        Assert.Empty(withoutProgram.GetTrackChunks().ElementAt(1).Events.OfType<ProgramChangeEvent>());
    }

    [Fact]
    public void SmartChordTrack_ExportsGeneratedNotesAndChordMarkers()
    {
        var song = CreateSong();
        var bass = (NoteTrack)song.Tracks[1];
        bass.Notes.Clear();
        bass.Notes.Add(new Note(0, 1920, 36, 100));      // C
        bass.Notes.Add(new Note(1920, 1920, 43, 100));   // G
        var chord = new SmartChordTrack { Name = "Chord", BassSource = bass };
        song.Tracks.Add(chord);
        chord.Regenerate(song);

        var chunks = ExportAndRead(song).GetTrackChunks().ToList();

        var markers = chunks[0].GetTimedEvents()
            .Where(e => e.Event is MarkerEvent)
            .Select(e => (e.Time, ((MarkerEvent)e.Event).Text)).ToArray();
        Assert.Equal([(0L, "Cm"), (1920L, "Gm")], markers);

        Assert.Equal(chord.PlaybackNotes.Count, chunks[3].GetNotes().Count);
    }

    [Fact]
    public void Options_CanOmitSetupAndMetaEvents()
    {
        var song = CreateSong();
        var chord = new SmartChordTrack { Name = "Chord", BassSource = (NoteTrack)song.Tracks[1] };
        song.Tracks.Add(chord);
        chord.Regenerate(song);

        var options = new MidiExportOptions
        {
            IncludeProgramChange = false,
            IncludeControlChange = false,
            IncludeChordMarkers = false,
            IncludeKeySignature = false,
        };
        var chunks = ExportAndRead(song, options).GetTrackChunks().ToList();
        var events = chunks.SelectMany(c => c.Events).ToList();

        Assert.Empty(events.OfType<ProgramChangeEvent>());
        Assert.Empty(events.OfType<ControlChangeEvent>());
        Assert.Empty(events.OfType<MarkerEvent>());
        Assert.Empty(events.OfType<KeySignatureEvent>());

        // 常に書き出すもの
        Assert.Single(chunks[0].Events.OfType<SetTempoEvent>());
        Assert.Single(chunks[0].Events.OfType<TimeSignatureEvent>());
        Assert.Equal(2, chunks[1].GetNotes().Count);
    }

    [Fact]
    public void EveryTrack_EndsWithEndOfTrack()
    {
        using var stream = new MemoryStream();
        MidiExporter.Export(CreateSong(), stream);
        var bytes = stream.ToArray();

        // 各 MTrk の末尾が FF 2F 00
        var index = 14;
        var count = 0;
        while (index < bytes.Length)
        {
            Assert.Equal("MTrk", Encoding.ASCII.GetString(bytes, index, 4));
            var length = bytes[index + 4] << 24 | bytes[index + 5] << 16 | bytes[index + 6] << 8 | bytes[index + 7];
            Assert.Equal(new byte[] { 0xFF, 0x2F, 0x00 }, bytes[(index + 8 + length - 3)..(index + 8 + length)]);
            index += 8 + length;
            count++;
        }
        Assert.Equal(3, count);
    }
}
