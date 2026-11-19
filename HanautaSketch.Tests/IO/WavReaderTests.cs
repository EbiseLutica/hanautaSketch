using System.Text;
using HanautaSketch.Engine.IO;

namespace HanautaSketch.Tests.IO;

public class WavReaderTests
{
    /// <summary>
    /// WAV を組み立てる。奇数長のチャンクを data の前に挟み、読み飛ばせるか確かめる。
    /// format に 0xFFFE を渡すと WAVE_FORMAT_EXTENSIBLE（中身の形式は subFormat）にする
    /// </summary>
    private static MemoryStream BuildWav(
        ushort format, ushort channels, int sampleRate, ushort bits, byte[] data,
        ushort? blockAlign = null, ushort subFormat = 1)
    {
        var align = blockAlign ?? (ushort)(channels * bits / 8);
        var extensible = format == 0xFFFE;
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(0u);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(extensible ? 40u : 16u);
            writer.Write(format);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write((uint)(sampleRate * align));
            writer.Write(align);
            writer.Write(bits);
            if (extensible)
            {
                writer.Write((ushort)22);
                writer.Write(bits);
                writer.Write(0u);
                writer.Write(subFormat);
                writer.Write(new byte[14]);
            }
            writer.Write("LIST"u8);
            writer.Write(3u);
            writer.Write(new byte[] { 1, 2, 3, 0 });
            writer.Write("data"u8);
            writer.Write((uint)data.Length);
            writer.Write(data);
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void ReadsStereo16BitAsMono()
    {
        // 2 フレーム：(16384, 0) と (-32768, -32768)
        var data = new byte[8];
        BitConverter.TryWriteBytes(data.AsSpan(0), (short)16384);
        BitConverter.TryWriteBytes(data.AsSpan(4), (short)-32768);
        BitConverter.TryWriteBytes(data.AsSpan(6), (short)-32768);

        var clip = WavReader.Read(BuildWav(1, 2, 22050, 16, data));

        Assert.Equal(22050, clip.SampleRate);
        Assert.Equal([0.25f, -1f], clip.Samples);
    }

    [Fact]
    public void ReadsFloat32()
    {
        var data = new byte[8];
        BitConverter.TryWriteBytes(data.AsSpan(0), 0.5f);
        BitConverter.TryWriteBytes(data.AsSpan(4), -0.125f);

        var clip = WavReader.Read(BuildWav(3, 1, 48000, 32, data));

        Assert.Equal([0.5f, -0.125f], clip.Samples);
    }

    [Fact]
    public void Reads24Bit()
    {
        // 0x400000 = 0.5、0xC00000 = -0.5
        byte[] data = [0x00, 0x00, 0x40, 0x00, 0x00, 0xC0];

        var clip = WavReader.Read(BuildWav(1, 1, 44100, 24, data));

        Assert.Equal([0.5f, -0.5f], clip.Samples);
    }

    [Fact]
    public void Reads8BitAndExtensible()
    {
        // 8bit は符号なし（128 が 0）。extensible の中身が PCM なら同じように読める
        byte[] data = [192, 64];

        Assert.Equal([0.5f, -0.5f], WavReader.Read(BuildWav(1, 1, 8000, 8, data)).Samples);
        Assert.Equal([0.5f, -0.5f], WavReader.Read(BuildWav(0xFFFE, 1, 8000, 8, data)).Samples);
    }

    [Fact]
    public void FollowsBlockAlign()
    {
        // 24bit を 4 バイト単位で詰めたファイル（末尾 1 バイトは詰め物）
        byte[] data = [0x00, 0x00, 0x40, 0xFF, 0x00, 0x00, 0xC0, 0xFF];

        var clip = WavReader.Read(BuildWav(1, 1, 44100, 24, data, blockAlign: 4));

        Assert.Equal([0.5f, -0.5f], clip.Samples);
    }

    [Fact]
    public void ReplacesNonFiniteAndClamps()
    {
        var data = new byte[16];
        BitConverter.TryWriteBytes(data.AsSpan(0), float.NaN);
        BitConverter.TryWriteBytes(data.AsSpan(4), float.PositiveInfinity);
        BitConverter.TryWriteBytes(data.AsSpan(8), 1.5f);
        BitConverter.TryWriteBytes(data.AsSpan(12), -0.25f);

        var clip = WavReader.Read(BuildWav(3, 1, 48000, 32, data));

        Assert.Equal([0f, 0f, 1f, -0.25f], clip.Samples);
    }

    [Fact]
    public void TruncatedFileThrowsInvalidData()
    {
        var full = BuildWav(1, 1, 44100, 16, new byte[4]).ToArray();

        // RIFF の途中、fmt の途中で切れたファイル
        Assert.Throws<InvalidDataException>(() => WavReader.Read(new MemoryStream(full[..6])));
        Assert.Throws<InvalidDataException>(() => WavReader.Read(new MemoryStream(full[..24])));
    }

    [Fact]
    public void RejectsNonWav()
    {
        Assert.Throws<InvalidDataException>(() => WavReader.Read(new MemoryStream(Encoding.ASCII.GetBytes("not a wav file"))));
    }
}
