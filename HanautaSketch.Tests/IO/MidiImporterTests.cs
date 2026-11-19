using System.Text;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Note = HanautaSketch.Engine.Core.Note;

namespace HanautaSketch.Tests.IO;

/// <summary>
/// 自前の書き出し、または DryWetMIDI（独立した実装）で作った SMF を読み込んで検証する。
/// </summary>
public class MidiImporterTests
{
    private static Song Import(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return MidiImporter.Import(stream);
    }

    /// <summary>
    /// DryWetMIDI で書く。MultiTrack で1チャンクを書くとメタイベントとノーツが別のチャンクに分かれるので、
    /// 1チャンクのときは Format 0 で書く。
    /// </summary>
    private static byte[] Write(MidiFile file, WritingSettings? settings = null)
    {
        var format = file.GetTrackChunks().Count() == 1 ? MidiFileFormat.SingleTrack : MidiFileFormat.MultiTrack;
        using var stream = new MemoryStream();
        file.Write(stream, format, settings ?? new WritingSettings { TextEncoding = Encoding.UTF8 });
        return stream.ToArray();
    }

    private static NoteOnEvent On(int pitch, int velocity, long delta = 0, int channel = 0) =>
        new((SevenBitNumber)pitch, (SevenBitNumber)velocity) { DeltaTime = delta, Channel = (FourBitNumber)channel };

    private static NoteOffEvent Off(int pitch, long delta, int channel = 0) =>
        new((SevenBitNumber)pitch, (SevenBitNumber)0) { DeltaTime = delta, Channel = (FourBitNumber)channel };

    [Fact]
    public void RoundTrip_FromExporter_RestoresTracksAndConductor()
    {
        var song = new Song();
        song.Conductor.Reset(132, 4, new Key(0, Mode.Minor));
        song.Conductor.SetTempo(1920, 90.5);
        song.Conductor.SetMeter(2, 3);
        song.Conductor.SetKey(3840, new Key(6, Mode.Major));
        var melody = new NoteTrack { Name = "メロディ", Program = 73, Volume = 90 };
        melody.Notes.Add(new Note(0, 240, 72, 100));
        melody.Notes.Add(new Note(240, 240, 72, 80));
        var drums = new NoteTrack { Name = "Drums", IsDrumPart = true, Program = 0, Volume = 110 };
        drums.Notes.Add(new Note(0, 120, 36, 120));
        song.Tracks.Add(melody);
        song.Tracks.Add(new NoteTrack { Name = "Empty" });
        song.Tracks.Add(drums);

        using var stream = new MemoryStream();
        MidiExporter.Export(song, stream);
        var imported = Import(stream.ToArray());

        // コンダクターと空のトラックは作らない
        var tracks = imported.Tracks.Cast<NoteTrack>().ToList();
        Assert.Equal(["メロディ", "Drums"], tracks.Select(t => t.Name).ToArray());
        Assert.Equal((73, 90, false), (tracks[0].Program, tracks[0].Volume, tracks[0].IsDrum));
        Assert.Equal((0, 110, true), (tracks[1].Program, tracks[1].Volume, tracks[1].IsDrum));
        Assert.Equal(melody.Notes, tracks[0].Notes);
        Assert.Equal(drums.Notes, tracks[1].Notes);

        var conductor = imported.Conductor;
        Assert.Equal([new TempoChange(0, 132), new TempoChange(1920, 90.5)],
            conductor.Tempos.Select(t => t with { Bpm = Math.Round(t.Bpm, 1) }));
        Assert.Equal([new MeterChange(0, 4), new MeterChange(2, 3)], conductor.Meters);
        Assert.Equal([new KeyChange(0, new Key(0, Mode.Minor)), new KeyChange(3840, new Key(6, Mode.Major))],
            conductor.Keys);
    }

    [Fact]
    public void Format0_SplitsChannelsAndScalesTicks()
    {
        // PPQ 96 → 480 で 5 倍。ランニングステータスとベロシティ0のノートオンで書く
        var chunk = new TrackChunk(
            new SequenceTrackNameEvent("ソング"),
            new ProgramChangeEvent((SevenBitNumber)33) { Channel = (FourBitNumber)0 },
            new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)80) { Channel = (FourBitNumber)0 },
            On(40, 100),
            On(36, 90, channel: 9),
            Off(36, 24, channel: 9),
            // 途中の音色・音量の変化は読まない（最初の値を使う）
            new ProgramChangeEvent((SevenBitNumber)34) { Channel = (FourBitNumber)0, DeltaTime = 24 },
            new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)60) { Channel = (FourBitNumber)0 },
            Off(40, 48));
        var file = new MidiFile(chunk) { TimeDivision = new TicksPerQuarterNoteTimeDivision(96) };
        var bytes = Write(file, new WritingSettings
        {
            TextEncoding = Encoding.UTF8,
            UseRunningStatus = true,
            NoteOffAsSilentNoteOn = true,
        });

        var tracks = Import(bytes).Tracks.Cast<NoteTrack>().ToList();

        Assert.Equal(["ソング (Ch 1)", "ソング (Ch 10)"], tracks.Select(t => t.Name).ToArray());
        Assert.Equal((33, 80, false), (tracks[0].Program, tracks[0].Volume, tracks[0].IsDrum));
        Assert.Equal((0, 100, true), (tracks[1].Program, tracks[1].Volume, tracks[1].IsDrum));
        Assert.Equal([new Note(0, 480, 40, 100)], tracks[0].Notes);
        Assert.Equal([new Note(0, 120, 36, 90)], tracks[1].Notes);
    }

    [Fact]
    public void ProgramAndVolume_FallBackToSetupTrack()
    {
        // 音色・音量だけを置いたセットアップ用のトラック。Guitar（ch2）の音量はノーツのトラック側の値を優先する
        var setup = new TrackChunk(
            new ProgramChangeEvent((SevenBitNumber)40) { Channel = (FourBitNumber)0 },
            new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)70) { Channel = (FourBitNumber)0 },
            new ProgramChangeEvent((SevenBitNumber)24) { Channel = (FourBitNumber)1 },
            new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)50) { Channel = (FourBitNumber)1 });
        var strings = new TrackChunk(new SequenceTrackNameEvent("Strings"), On(60, 100), Off(60, 480));
        var guitar = new TrackChunk(
            new SequenceTrackNameEvent("Guitar"),
            new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)1 },
            On(64, 100, channel: 1),
            Off(64, 480, channel: 1));
        var file = new MidiFile(setup, strings, guitar) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };

        var tracks = Import(Write(file)).Tracks;

        Assert.Equal(["Strings", "Guitar"], tracks.Select(t => t.Name).ToArray());
        Assert.Equal((40, 70), (tracks[0].Program, tracks[0].Volume));
        Assert.Equal((24, 90), (tracks[1].Program, tracks[1].Volume));
    }

    [Fact]
    public void TooManyConductorEvents_AreRejected()
    {
        var tempos = Enumerable.Range(0, 10_001).Select(i => new SetTempoEvent(500_000 + i) { DeltaTime = 1 });
        var file = new MidiFile(new TrackChunk(tempos), new TrackChunk(On(60, 100), Off(60, 480)))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

        Assert.Throws<InvalidDataException>(() => Import(Write(file)));
    }

    [Fact]
    public void Notes_PairOverlapsInOrderAndCutUnterminatedAtTrackEnd()
    {
        var chunk = new TrackChunk(
            On(60, 100),
            On(60, 80, 480),
            Off(60, 480),
            Off(60, 480),
            On(64, 70),
            // 長さ 0 のノートは捨てる
            On(67, 90),
            Off(67, 0),
            new NormalSysExEvent([0x7E, 0x7F, 0x09, 0x01, 0xF7]) { DeltaTime = 240 },
            new TextEvent("end") { DeltaTime = 240 });
        var file = new MidiFile(chunk) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };

        var track = Assert.IsType<NoteTrack>(Assert.Single(Import(Write(file)).Tracks));

        Assert.Equal("Ch 1", track.Name);
        Assert.Equal([new Note(0, 960, 60, 100), new Note(480, 960, 60, 80), new Note(1440, 480, 64, 70)], track.Notes);
    }

    [Theory]
    [InlineData(6, 8, 3)]
    [InlineData(2, 2, 4)]
    [InlineData(3, 4, 3)]
    [InlineData(7, 8, 4)]
    [InlineData(5, 8, 3)]
    public void TimeSignature_IsRoundedToQuarterBeats(int numerator, int denominator, int expectedBeats)
    {
        var conductor = new TrackChunk(
            new TimeSignatureEvent((byte)numerator, (byte)denominator),
            new SetTempoEvent(500_000));
        var notes = new TrackChunk(On(60, 100), Off(60, 480));
        var file = new MidiFile(conductor, notes) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };

        var song = Import(Write(file));

        Assert.Equal([new MeterChange(0, expectedBeats)], song.Conductor.Meters);
        Assert.Equal([new TempoChange(0, 120)], song.Conductor.Tempos);
    }

    [Fact]
    public void Conductor_IsCollectedFromAllTracksAndScaled()
    {
        // PPQ 96。2つ目のトラックにテンポ・拍子・調号の変化を置く。拍子は小節の途中なので次の小節から
        var conductor = new TrackChunk(new SetTempoEvent(500_000), new TimeSignatureEvent(4, 4));
        var notes = new TrackChunk(
            On(60, 100),
            new SetTempoEvent(400_000) { DeltaTime = 384 },
            new TimeSignatureEvent(3, 4) { DeltaTime = 96 },
            new KeySignatureEvent(2, 0) { DeltaTime = 288 },
            Off(60, 0));
        var file = new MidiFile(conductor, notes) { TimeDivision = new TicksPerQuarterNoteTimeDivision(96) };

        var song = Import(Write(file));

        Assert.Equal([new TempoChange(0, 120), new TempoChange(1920, 150)], song.Conductor.Tempos);
        Assert.Equal([new MeterChange(0, 4), new MeterChange(2, 3)], song.Conductor.Meters);
        Assert.Equal([new KeyChange(0, new Key(0, Mode.Major)), new KeyChange(3840, new Key(2, Mode.Major))],
            song.Conductor.Keys);
        Assert.Equal([new Note(0, 3840, 60, 100)], Assert.IsType<NoteTrack>(Assert.Single(song.Tracks)).Notes);
    }

    [Fact]
    public void Conductor_LaterTrackWinsAtSameTick()
    {
        var first = new TrackChunk(new SetTempoEvent(500_000), new TimeSignatureEvent(3, 4));
        var second = new TrackChunk(new SetTempoEvent(400_000), new TimeSignatureEvent(5, 4), On(60, 100), Off(60, 480));
        var file = new MidiFile(first, second) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };

        var conductor = Import(Write(file)).Conductor;

        Assert.Equal([new TempoChange(0, 150)], conductor.Tempos);
        Assert.Equal([new MeterChange(0, 5)], conductor.Meters);
    }

    [Fact]
    public void Conductor_DefaultsWhenFileHasNoConductorEvents()
    {
        var file = new MidiFile(new TrackChunk(On(60, 100), Off(60, 480)))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

        var conductor = Import(Write(file)).Conductor;

        Assert.Equal([new TempoChange(0, 120)], conductor.Tempos);
        Assert.Equal([new MeterChange(0, 4)], conductor.Meters);
        Assert.Equal([new KeyChange(0, new Key(0, Mode.Major))], conductor.Keys);
    }

    [Theory]
    [InlineData(-3, 1, 0, Mode.Minor)]
    [InlineData(4, 0, 4, Mode.Major)]
    [InlineData(-6, 0, 6, Mode.Major)]
    [InlineData(1, 1, 4, Mode.Minor)]
    public void KeySignature_MapsToKey(int accidentals, int scale, int tonic, Mode mode)
    {
        var file = new MidiFile(
            new TrackChunk(new KeySignatureEvent((sbyte)accidentals, (byte)scale)),
            new TrackChunk(On(60, 100), Off(60, 480)))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

        Assert.Equal(new Key(tonic, mode), Import(Write(file)).Conductor.KeyAt(0));
    }

    [Fact]
    public void TrackName_FallsBackToShiftJis()
    {
        var chunk = new TrackChunk(new SequenceTrackNameEvent("ベース"), On(36, 100), Off(36, 480));
        var file = new MidiFile(chunk) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        var bytes = Write(file, new WritingSettings { TextEncoding = MidiExporter.DefaultTextEncoding });

        Assert.Equal("ベース", Assert.Single(Import(bytes).Tracks).Name);
    }

    [Fact]
    public void SmpteTimeDivision_IsRejected()
    {
        var file = new MidiFile(new TrackChunk(On(60, 100), Off(60, 40)))
        {
            TimeDivision = new SmpteTimeDivision(SmpteFormat.TwentyFive, 40),
        };

        Assert.Throws<InvalidDataException>(() => Import(Write(file)));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    // トラックの途中で切れている
    [InlineData(new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0,
        0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 3, 0x00, 0x90, 0x3C })]
    // Format 2
    [InlineData(new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 2, 0, 1, 0x01, 0xE0,
        0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 4, 0x00, 0xFF, 0x2F, 0x00 })]
    // データバイトの最上位ビットが立っている
    [InlineData(new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 1, 0, 1, 0x01, 0xE0,
        0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 8, 0x00, 0x90, 0xBC, 0x64, 0x00, 0xFF, 0x2F, 0x00 })]
    // PPQ 1 で長いデルタタイム：換算すると int の範囲を超える
    [InlineData(new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, 0x00, 0x01,
        0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 15, 0x00, 0x90, 0x3C, 0x64, 0xFF, 0xFF, 0xFF, 0x7F, 0x80, 0x3C, 0x00,
        0x00, 0xFF, 0x2F, 0x00 })]
    public void InvalidData_IsRejected(byte[] bytes)
    {
        Assert.Throws<InvalidDataException>(() => Import(bytes));
    }
}
