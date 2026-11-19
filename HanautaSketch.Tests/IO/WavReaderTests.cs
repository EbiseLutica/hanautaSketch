using System.Text;
using HanautaSketch.Engine.IO;

namespace HanautaSketch.Tests.IO;

public class WavReaderTests
{
    /// <summary>PCM の WAV を組み立てる。odd チャンクを data の前に挟み、読み飛ばせるか確かめる</summary>
    private static MemoryStream BuildWav(ushort format, ushort channels, int sampleRate, ushort bits, byte[] data)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(0u);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16u);
            writer.Write(format);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write((uint)(sampleRate * channels * bits / 8));
            writer.Write((ushort)(channels * bits / 8));
            writer.Write(bits);
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
    public void RejectsNonWav()
    {
        Assert.Throws<InvalidDataException>(() => WavReader.Read(new MemoryStream(Encoding.ASCII.GetBytes("not a wav file"))));
    }
}
