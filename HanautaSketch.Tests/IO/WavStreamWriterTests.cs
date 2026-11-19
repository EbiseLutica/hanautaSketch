using System.Buffers.Binary;
using System.Text;
using HanautaSketch.Engine.IO;

namespace HanautaSketch.Tests.IO;

/// <summary>
/// 書き出した WAV を <see cref="WavReader"/> で読み戻したり、ヘッダのバイト列を直接見たりして検証する。
/// </summary>
public class WavStreamWriterTests
{
    private static byte[] Write(WavSampleFormat format, float[] left, float[] right, int sampleRate = 44100)
    {
        using var stream = new MemoryStream();
        var writer = new WavStreamWriter(stream, sampleRate, format, left.Length);
        writer.Write(left, right);
        writer.Complete();
        return stream.ToArray();
    }

    /// <summary>ヘッダから指定のチャンクの位置（中身の先頭）と大きさを探す</summary>
    private static (int Offset, int Size) FindChunk(byte[] wav, string tag)
    {
        var p = 12;
        while (p + 8 <= wav.Length)
        {
            var name = Encoding.ASCII.GetString(wav, p, 4);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(p + 4));
            if (name == tag) return (p + 8, size);
            p += 8 + size + (size & 1);
        }
        throw new InvalidOperationException($"{tag} chunk not found");
    }

    /// <summary>ヘッダの形式コード・チャンネル数・レート・ブロック長・ビット数・各チャンクの大きさが正しいことを確かめる</summary>
    [Theory]
    [InlineData(WavSampleFormat.Pcm16, 1, 16)]
    [InlineData(WavSampleFormat.Pcm24, 1, 24)]
    [InlineData(WavSampleFormat.Float32, 3, 32)]
    public void Header_HasFormatAndSizes(WavSampleFormat format, int formatTag, int bits)
    {
        var wav = Write(format, [0f, 0.5f, -0.5f], [0f, 0.25f, -0.25f], 48000);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(wav.Length - 8, (int)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(4)));
        var (fmt, fmtSize) = FindChunk(wav, "fmt ");
        Assert.Equal(formatTag, BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmt)));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmt + 2)));
        Assert.Equal(48000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(fmt + 4)));
        Assert.Equal(48000 * 2 * bits / 8, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(fmt + 8)));
        Assert.Equal(2 * bits / 8, BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmt + 12)));
        Assert.Equal(bits, BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmt + 14)));
        if (format == WavSampleFormat.Float32)
        {
            Assert.Equal(18, fmtSize);
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmt + 16)));
        }
        else
        {
            Assert.Equal(16, fmtSize);
        }
        var (_, dataSize) = FindChunk(wav, "data");
        Assert.Equal(3 * 2 * bits / 8, dataSize);
    }

    /// <summary>float 形式では fact チャンクにフレーム数を書くことを確かめる</summary>
    [Fact]
    public void Float32_WritesFactChunkWithFrameCount()
    {
        var wav = Write(WavSampleFormat.Float32, [0f, 0f, 0f], [0f, 0f, 0f]);

        var (fact, size) = FindChunk(wav, "fact");
        Assert.Equal(4, size);
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(fact)));
    }

    /// <summary>WavReader で読み戻すと、左右の平均（モノラル）になることを確かめる</summary>
    [Theory]
    [InlineData(WavSampleFormat.Pcm16)]
    [InlineData(WavSampleFormat.Pcm24)]
    [InlineData(WavSampleFormat.Float32)]
    public void Write_ReadsBackAsAverageOfChannels(WavSampleFormat format)
    {
        var wav = Write(format, [0f, 0.5f, -1f, 1f], [0f, 0.25f, -1f, 0.5f]);

        var clip = WavReader.Read(new MemoryStream(wav));

        Assert.Equal(44100, clip.SampleRate);
        Assert.Equal(new[] { 0f, 0.375f, -1f, 0.75f }, clip.Samples, (a, b) => Math.Abs(a - b) < 1e-4f);
    }

    /// <summary>data チャンクの i 番目のサンプル（左右を交互に数える）を整数で読む</summary>
    private static int ReadPcm(byte[] wav, WavSampleFormat format, int i)
    {
        var (data, _) = FindChunk(wav, "data");
        return format == WavSampleFormat.Pcm16
            ? BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(data + i * 2))
            : wav[data + i * 3] | (wav[data + i * 3 + 1] << 8) | ((sbyte)wav[data + i * 3 + 2] << 16);
    }

    /// <summary>PCM は左・右の順に書き、範囲外の値は切り詰め、NaN・無限大は 0 にすることを確かめる</summary>
    [Theory]
    [InlineData(WavSampleFormat.Pcm16, 32767)]
    [InlineData(WavSampleFormat.Pcm24, 8388607)]
    public void Pcm_WritesLeftThenRightAndClamps(WavSampleFormat format, int max)
    {
        var wav = Write(format, [0.5f, 2f, float.NaN], [-0.25f, -3f, float.PositiveInfinity]);

        Assert.Equal((int)MathF.Round(0.5f * max), ReadPcm(wav, format, 0));
        Assert.Equal((int)MathF.Round(-0.25f * max), ReadPcm(wav, format, 1));
        Assert.Equal(max, ReadPcm(wav, format, 2));
        Assert.Equal(-max, ReadPcm(wav, format, 3));
        Assert.Equal(0, ReadPcm(wav, format, 4));
        Assert.Equal(0, ReadPcm(wav, format, 5));
    }

    /// <summary>float は範囲外の値をそのまま書き、NaN・無限大は 0 にすることを確かめる</summary>
    [Fact]
    public void Float32_KeepsOutOfRangeAndZeroesNonFinite()
    {
        var wav = Write(WavSampleFormat.Float32, [1.5f, float.NaN], [-2f, float.NegativeInfinity]);

        var (data, _) = FindChunk(wav, "data");
        Assert.Equal(1.5f, BinaryPrimitives.ReadSingleLittleEndian(wav.AsSpan(data)));
        Assert.Equal(-2f, BinaryPrimitives.ReadSingleLittleEndian(wav.AsSpan(data + 4)));
        Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(wav.AsSpan(data + 8)));
        Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(wav.AsSpan(data + 12)));
    }

    /// <summary>長さ 0 でも、読める WAV になることを確かめる</summary>
    [Fact]
    public void ZeroFrames_IsReadable()
    {
        var wav = Write(WavSampleFormat.Pcm16, [], []);

        var clip = WavReader.Read(new MemoryStream(wav));

        Assert.Empty(clip.Samples);
    }

    /// <summary>ヘッダで宣言した長さより多く・少なく書くと例外になることを確かめる</summary>
    [Fact]
    public void FrameCountMismatch_Throws()
    {
        using var stream = new MemoryStream();
        var writer = new WavStreamWriter(stream, 44100, WavSampleFormat.Pcm16, 2);

        Assert.Throws<InvalidOperationException>(() => writer.Write([0f, 0f, 0f], [0f, 0f, 0f]));
        writer.Write([0f], [0f]);
        Assert.Throws<InvalidOperationException>(writer.Complete);
    }

    /// <summary>WAV の上限 4GB に収まるかの境界と、超えると例外になることを確かめる</summary>
    [Fact]
    public void Over4GB_DoesNotFit()
    {
        // RIFF の大きさ = ヘッダ（PCM 36、float 50）+ フレーム数 × 4（PCM16）/ 6（PCM24）/ 8（float）が uint に収まるまで
        Assert.True(WavStreamWriter.Fits(1073741814, WavSampleFormat.Pcm16));
        Assert.False(WavStreamWriter.Fits(1073741815, WavSampleFormat.Pcm16));
        Assert.True(WavStreamWriter.Fits(715827876, WavSampleFormat.Pcm24));
        Assert.False(WavStreamWriter.Fits(715827877, WavSampleFormat.Pcm24));
        Assert.True(WavStreamWriter.Fits(536870905, WavSampleFormat.Float32));
        Assert.False(WavStreamWriter.Fits(536870906, WavSampleFormat.Float32));
        Assert.Throws<IOException>(() => new WavStreamWriter(new MemoryStream(), 48000, WavSampleFormat.Float32, 536870906));
    }
}
