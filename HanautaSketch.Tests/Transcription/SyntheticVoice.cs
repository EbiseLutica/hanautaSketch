using HanautaSketch.Engine.Transcription;

namespace HanautaSketch.Tests.Transcription;

/// <summary>
/// テスト用に、鼻歌に似た倍音を含む音を合成する。
/// </summary>
internal static class SyntheticVoice
{
    /// <summary>
    /// 合成する音1つ分。
    /// </summary>
    /// <param name="Seconds">長さ（秒）</param>
    /// <param name="Midi">音高（小数の MIDI ノート番号）。null なら無音（または <paramref name="NoiseBurst"/> のノイズ）</param>
    /// <param name="Amplitude">振幅</param>
    /// <param name="Vibrato">ビブラートの振れ幅（半音、5.5Hz）</param>
    /// <param name="Tremolo">音量の揺れの深さ（0〜1、5Hz）。0.5 なら谷で半分の音量</param>
    /// <param name="Scoop">歌い出しのしゃくり（半音）。この分だけ低い音から 120ms かけて上がる</param>
    /// <param name="NoiseBurst">無音の代わりに鳴らすノイズの振幅（咳などの代わり）</param>
    public readonly record struct Segment(
        double Seconds, double? Midi, double Amplitude = 0.3,
        double Vibrato = 0, double Tremolo = 0, double Scoop = 0, double NoiseBurst = 0);

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
                var t = (double)i / sampleRate;
                var noise = (random.NextDouble() * 2 - 1) * 0.002;
                if (segment.Midi is not { } midi)
                {
                    samples.Add((float)(noise + (random.NextDouble() * 2 - 1) * segment.NoiseBurst));
                    continue;
                }
                var current = midi
                    + segment.Vibrato * Math.Sin(2 * Math.PI * 5.5 * t)
                    - segment.Scoop * Math.Max(0, 1 - t / 0.12);
                phase += 2 * Math.PI * PitchTracker.MidiToFrequency(current) / sampleRate;
                // 鼻歌らしく基音が強く、倍音が順に弱まる音
                var value = Math.Sin(phase) + 0.5 * Math.Sin(2 * phase) + 0.25 * Math.Sin(3 * phase) + 0.12 * Math.Sin(4 * phase);
                var envelope = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)Math.Max(1, fade));
                var tremolo = 1 - segment.Tremolo * (0.5 - 0.5 * Math.Cos(2 * Math.PI * 5 * t));
                samples.Add((float)(segment.Amplitude * envelope * tremolo * value / 1.87 + noise));
            }
        }
        return samples.ToArray();
    }
}
