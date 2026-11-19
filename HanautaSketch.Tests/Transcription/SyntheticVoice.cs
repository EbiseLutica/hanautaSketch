using HanautaSketch.Engine.Transcription;

namespace HanautaSketch.Tests.Transcription;

/// <summary>
/// テスト用に、鼻歌に似た倍音を含む音を合成する。
/// </summary>
internal static class SyntheticVoice
{
    /// <summary>合成する音1つ分（秒と小数の MIDI ノート番号）。Midi が null なら無音</summary>
    public readonly record struct Segment(double Seconds, double? Midi, double Amplitude = 0.3);

    public static float[] Render(int sampleRate, params Segment[] segments)
    {
        var samples = new List<float>();
        var random = new Random(1234);
        var phase = 0.0;
        foreach (var segment in segments)
        {
            var count = (int)Math.Round(segment.Seconds * sampleRate);
            var fade = Math.Min(count / 4, (int)(0.01 * sampleRate));
            for (var i = 0; i < count; i++)
            {
                var noise = (random.NextDouble() * 2 - 1) * 0.002;
                if (segment.Midi is not { } midi)
                {
                    samples.Add((float)noise);
                    continue;
                }
                var frequency = PitchTracker.MidiToFrequency(midi);
                phase += 2 * Math.PI * frequency / sampleRate;
                // 鼻歌らしく基音が強く、倍音が順に弱まる音
                var value = Math.Sin(phase) + 0.5 * Math.Sin(2 * phase) + 0.25 * Math.Sin(3 * phase) + 0.12 * Math.Sin(4 * phase);
                var envelope = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)Math.Max(1, fade));
                samples.Add((float)(segment.Amplitude * envelope * value / 1.87 + noise));
            }
        }
        return samples.ToArray();
    }
}
