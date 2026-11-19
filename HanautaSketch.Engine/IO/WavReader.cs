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
/// 壊れたファイルは <see cref="InvalidDataException"/> を投げる。
/// </summary>
public static class WavReader
{
    public static AudioClip Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static AudioClip Read(Stream stream)
    {
        try
        {
            return ReadCore(stream);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("Unexpected end of WAV file.", e);
        }
    }

    private static AudioClip ReadCore(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (ReadTag(reader) != "RIFF") throw new InvalidDataException("Not a RIFF file.");
        reader.ReadUInt32();
        if (ReadTag(reader) != "WAVE") throw new InvalidDataException("Not a WAVE file.");

        ushort format = 0, channels = 0, blockAlign = 0, bitsPerSample = 0;
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
                    if (size < 16) throw new InvalidDataException("fmt chunk is too short.");
                    format = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadUInt32();
                    blockAlign = reader.ReadUInt16();
                    bitsPerSample = reader.ReadUInt16();
                    if (format == WavFormatCodes.Extensible && size >= 26)
                    {
                        reader.ReadUInt16();
                        reader.ReadUInt16();
                        reader.ReadUInt32();
                        // SubFormat GUID の先頭2バイトが実際の形式
                        format = reader.ReadUInt16();
                    }
                    break;
                case "data":
                    var length = Math.Min(size, stream.Length - stream.Position);
                    if (length > Array.MaxLength) throw new InvalidDataException("WAV data is too large.");
                    data = reader.ReadBytes((int)length);
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
            (WavFormatCodes.Pcm, 8) => (b, i) => (b[i] - 128) / 128f,
            (WavFormatCodes.Pcm, 16) => (b, i) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(i)) / 32768f,
            (WavFormatCodes.Pcm, 24) => (b, i) => ((b[i] | (b[i + 1] << 8) | ((sbyte)b[i + 2] << 16))) / 8388608f,
            (WavFormatCodes.Pcm, 32) => (b, i) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i)) / 2147483648f,
            (WavFormatCodes.Float, 32) => (b, i) => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(i)),
            _ => throw new InvalidDataException($"Unsupported WAV format: format={format}, bits={bitsPerSample}."),
        };

        // 1フレームの長さは blockAlign に従う（サンプルの後ろに詰め物があるファイルもある）
        var bytesPerSample = bitsPerSample / 8;
        if (blockAlign < bytesPerSample * channels) throw new InvalidDataException("Invalid block alignment.");
        var frames = data.Length / blockAlign;
        var samples = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++) sum += decode(data, f * blockAlign + c * bytesPerSample);
            var value = sum / channels;
            samples[f] = float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0f;
        }
        return new AudioClip(samples, sampleRate);
    }

    private static string ReadTag(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length < 4) throw new EndOfStreamException();
        return Encoding.ASCII.GetString(bytes);
    }
}
