using System.Text;

namespace HanautaSketch.Engine.IO;

/// <summary>
/// <see cref="AudioClip"/> をモノラル 16bit PCM の WAV ファイルに書き出す。
/// </summary>
public static class WavWriter
{
    public static void Write(string path, AudioClip clip)
    {
        using var stream = File.Create(path);
        Write(stream, clip);
    }

    public static void Write(Stream stream, AudioClip clip)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const short blockAlign = channels * bitsPerSample / 8;
        var dataSize = clip.Samples.Length * blockAlign;

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(clip.SampleRate);
        writer.Write(clip.SampleRate * blockAlign);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);
        foreach (var sample in clip.Samples)
        {
            var value = float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) : 0f;
            writer.Write((short)Math.Round(value * short.MaxValue));
        }
    }
}
