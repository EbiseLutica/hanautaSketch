using System.Buffers.Binary;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;

namespace HanautaSketch.Tests.IO;

/// <summary>
/// 最小の SoundFont（<see cref="TestSoundFont"/>）で実際に鳴らし、書き出す長さと中身を検証する。
/// テンポは既定の 120BPM なので、480 tick = 0.5 秒（44.1kHz で 22050 フレーム）。
/// </summary>
public class WavExporterTests
{
    private static readonly MeltySynth.SoundFont SoundFont = TestSoundFont.Create();

    private static NoteTrack Track(params (int Tick, int Duration)[] notes)
    {
        var track = new NoteTrack();
        foreach (var (tick, duration) in notes) track.Notes.Add(new Note(tick, duration, 60, 100));
        return track;
    }

    private static float[] ExportAndRead(PreparedWavExport export)
    {
        using var stream = new MemoryStream();
        export.WriteTo(stream);
        stream.Position = 0;
        return WavReader.Read(stream).Samples;
    }

    private static bool IsSilent(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples)
        {
            if (s != 0) return false;
        }
        return true;
    }

    private static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
        return peak;
    }

    /// <summary>余韻なしでは、最後のノートオフでちょうど切ることを確かめる</summary>
    [Theory]
    [InlineData(44100, 45938)]
    [InlineData(48000, 50000)]
    public void WithoutTail_EndsAtLastNoteOff(int sampleRate, long expectedFrames)
    {
        var song = new Song();
        song.Tracks.Add(Track((0, 1000)));

        var export = WavExporter.Prepare(song, SoundFont, new WavExportOptions { SampleRate = sampleRate });

        Assert.Equal(expectedFrames, export.Frames);
        using var stream = new MemoryStream();
        export.WriteTo(stream);
        stream.Position = 0;
        var clip = WavReader.Read(stream);
        Assert.Equal(expectedFrames, clip.Samples.Length);
        Assert.Equal(sampleRate, clip.SampleRate);
        Assert.True(Peak(clip.Samples) > 0.001f);
    }

    /// <summary>余韻ありでは、余韻なしより 1.5 秒長くなることを確かめる</summary>
    [Fact]
    public void WithTail_AddsReleaseTail()
    {
        var song = new Song();
        song.Tracks.Add(Track((0, 1000)));

        var export = WavExporter.Prepare(song, SoundFont, new WavExportOptions { IncludeReleaseTail = true });

        Assert.Equal(45938 + 66150, export.Frames);
        Assert.Equal(export.Frames, ExportAndRead(export).Length);
    }

    /// <summary>長さは後から始まる音ではなく、最後に終わる音で決まることを確かめる</summary>
    [Fact]
    public void Length_FollowsLatestNoteOff()
    {
        var song = new Song();
        song.Tracks.Add(Track((0, 1920), (480, 480)));

        var export = WavExporter.Prepare(song, SoundFont, new WavExportOptions());

        Assert.Equal(88200, export.Frames);
    }

    /// <summary>ミュートしたトラックは、長さにも音にも入らないことを確かめる</summary>
    [Fact]
    public void MutedTrack_IsExcluded()
    {
        var song = new Song();
        var muted = Track((0, 480), (2880, 960));
        muted.Mute = true;
        song.Tracks.Add(muted);
        song.Tracks.Add(Track((960, 960)));

        var export = WavExporter.Prepare(song, SoundFont, new WavExportOptions());
        var samples = ExportAndRead(export);

        Assert.Equal(88200, export.Frames);
        Assert.True(IsSilent(samples.AsSpan(0, 44100)));
        Assert.True(Peak(samples.AsSpan(44100)) > 0.001f);
    }

    /// <summary>ソロのトラックがあれば、それだけを鳴らすことを確かめる</summary>
    [Fact]
    public void Solo_PlaysOnlySoloTracks()
    {
        var song = new Song();
        song.Tracks.Add(Track((0, 480), (2880, 960)));
        var solo = Track((960, 960));
        solo.Solo = true;
        song.Tracks.Add(solo);

        var export = WavExporter.Prepare(song, SoundFont, new WavExportOptions());
        var samples = ExportAndRead(export);

        Assert.Equal(88200, export.Frames);
        Assert.True(IsSilent(samples.AsSpan(0, 44100)));
        Assert.True(Peak(samples.AsSpan(44100)) > 0.001f);
    }

    /// <summary>チャンネルを共有するトラックでも、ミュートがトラック単位で効くことを確かめる</summary>
    [Fact]
    public void SharedChannel_MuteIsPerTrack()
    {
        // チャンネルが足りないと、あふれたトラックは最後のチャンネルを共有する
        var song = new Song();
        for (var i = 0; i < MidiChannels.Count; i++) song.Tracks.Add(new NoteTrack());
        song.Tracks.Add(Track((960, 960)));
        var muted = Track((0, 480), (2880, 960));
        muted.Mute = true;
        song.Tracks.Add(muted);

        var export = WavExporter.Prepare(song, SoundFont, new WavExportOptions());
        var samples = ExportAndRead(export);

        Assert.Equal(88200, export.Frames);
        Assert.True(IsSilent(samples.AsSpan(0, 44100)));
        Assert.True(Peak(samples.AsSpan(44100)) > 0.001f);
    }

    /// <summary>すべてミュートなら、余韻の長さだけの無音になることを確かめる</summary>
    [Fact]
    public void AllMuted_IsSilentTail()
    {
        var song = new Song();
        var track = Track((0, 960));
        track.Mute = true;
        song.Tracks.Add(track);

        var samples = ExportAndRead(WavExporter.Prepare(song, SoundFont, new WavExportOptions { IncludeReleaseTail = true }));

        Assert.Equal(66150, samples.Length);
        Assert.True(IsSilent(samples));
    }

    /// <summary>ノーツのない曲を余韻なしで書き出すと、長さ 0 の WAV になることを確かめる</summary>
    [Fact]
    public void EmptySong_WithoutTail_IsZeroLength()
    {
        var export = WavExporter.Prepare(new Song(), SoundFont, new WavExportOptions());

        Assert.Equal(0, export.Frames);
        Assert.Empty(ExportAndRead(export));
    }

    /// <summary>書き出しの準備のあとに曲を編集しても、書き出す内容は変わらないことを確かめる</summary>
    [Fact]
    public void EditsAfterPrepare_DoNotAffectExport()
    {
        var song = new Song();
        var track = Track((960, 960));
        song.Tracks.Add(track);

        var export = WavExporter.Prepare(song, SoundFont, new WavExportOptions());
        track.Notes.Add(new Note(0, 480, 64, 100));
        track.Mute = true;
        var samples = ExportAndRead(export);

        Assert.True(IsSilent(samples.AsSpan(0, 44100)));
        Assert.True(Peak(samples.AsSpan(44100)) > 0.001f);
    }

    /// <summary>選んだサンプル形式で書き出され、どの形式でも長さが変わらないことを確かめる</summary>
    [Theory]
    [InlineData(WavSampleFormat.Pcm16, 1, 16)]
    [InlineData(WavSampleFormat.Pcm24, 1, 24)]
    [InlineData(WavSampleFormat.Float32, 3, 32)]
    public void Format_IsPassedToWriter(WavSampleFormat format, int formatTag, int bits)
    {
        var song = new Song();
        song.Tracks.Add(Track((0, 1000)));

        using var stream = new MemoryStream();
        WavExporter.Prepare(song, SoundFont, new WavExportOptions { Format = format }).WriteTo(stream);
        var wav = stream.ToArray();

        Assert.Equal(formatTag, BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(20)));
        Assert.Equal(bits, BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(34)));
        stream.Position = 0;
        Assert.Equal(45938, WavReader.Read(stream).Samples.Length);
    }

    /// <summary>対応していないサンプルレートは例外になることを確かめる</summary>
    [Theory]
    [InlineData(22050)]
    [InlineData(96000)]
    public void UnsupportedSampleRate_Throws(int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WavExporter.Prepare(new Song(), SoundFont, new WavExportOptions { SampleRate = sampleRate }));
    }
}
