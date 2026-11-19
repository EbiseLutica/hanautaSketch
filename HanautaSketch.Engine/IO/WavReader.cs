using System.Buffers.Binary;
using System.Text;

namespace HanautaSketch.Engine.IO;

/// <summary>
/// モノラルに混ぜた音声データ。
/// </summary>
/// <param name="Samples">サンプル（-1〜1）</param>
/// <param name="SampleRate">サンプルレート（Hz）</param>
public sealed record AudioClip(float[] Samples, int SampleRate)
{
    /// <summary>長さ（秒）</summary>
    public double DurationSeconds => (double)Samples.Length / SampleRate;
}

/// <summary>
/// WAV ファイルを読み込む。PCM 8/16/24/32bit と 32bit float に対応し、複数チャンネルは平均してモノラルにする。
/// </summary>
public static class WavReader
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    public static AudioClip Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static AudioClip Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (ReadTag(reader) != "RIFF") throw new InvalidDataException("Not a RIFF file.");
        reader.ReadUInt32();
        if (ReadTag(reader) != "WAVE") throw new InvalidDataException("Not a WAVE file.");

        ushort format = 0, channels = 0, bitsPerSample = 0;
        var sampleRate = 0;
        byte[]? data = null;

        // チャンクを順に読み、fmt と data だけ拾う
        while (stream.Position + 8 <= stream.Length)
        {
            var tag = ReadTag(reader);
            var size = reader.ReadUInt32();
            var next = stream.Position + size + (size & 1);
            switch (tag)
            {
                case "fmt ":
                    format = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt16();
                    bitsPerSample = reader.ReadUInt16();
                    if (format == FormatExtensible && size >= 26)
                    {
                        reader.ReadUInt16();
                        reader.ReadUInt16();
                        reader.ReadUInt32();
                        // SubFormat GUID の先頭2バイトが実際の形式
                        format = reader.ReadUInt16();
                    }
                    break;
                case "data":
                    var length = (int)Math.Min(size, stream.Length - stream.Position);
                    data = reader.ReadBytes(length);
                    break;
            }
            if (data != null && format != 0) break;
            stream.Position = Math.Min(next, stream.Length);
        }

        if (format == 0) throw new InvalidDataException("Missing fmt chunk.");
        if (data == null) throw new InvalidDataException("Missing data chunk.");
        if (channels == 0 || sampleRate <= 0) throw new InvalidDataException("Invalid fmt chunk.");

        Func<byte[], int, float> decode = (format, bitsPerSample) switch
        {
            (FormatPcm, 8) => (b, i) => (b[i] - 128) / 128f,
            (FormatPcm, 16) => (b, i) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(i)) / 32768f,
            (FormatPcm, 24) => (b, i) => ((b[i] | (b[i + 1] << 8) | ((sbyte)b[i + 2] << 16))) / 8388608f,
            (FormatPcm, 32) => (b, i) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i)) / 2147483648f,
            (FormatFloat, 32) => (b, i) => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(i)),
            _ => throw new InvalidDataException($"Unsupported WAV format: format={format}, bits={bitsPerSample}."),
        };

        var bytesPerSample = bitsPerSample / 8;
        var frameSize = bytesPerSample * channels;
        var frames = data.Length / frameSize;
        var samples = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++) sum += decode(data, f * frameSize + c * bytesPerSample);
            samples[f] = sum / channels;
        }
        return new AudioClip(samples, sampleRate);
    }

    private static string ReadTag(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
}
