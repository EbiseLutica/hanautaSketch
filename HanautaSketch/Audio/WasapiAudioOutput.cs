using HanautaSketch.Engine.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HanautaSketch.Audio;

/// <summary>
/// <see cref="PlaybackEngine"/> の出力を WASAPI（共有モード）で鳴らす（Windows 用）。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WasapiAudioOutput : IAudioOutput
{
    private const int LatencyMilliseconds = 50;

    private readonly WasapiOut _output;

    private WasapiAudioOutput(PlaybackEngine engine, WasapiOut output)
    {
        Engine = engine;
        _output = output;
    }

    public PlaybackEngine Engine { get; }

    /// <summary>
    /// 既定の出力デバイスのサンプルレートでエンジンを作り、出力を開始する。SF2 の読み込みは重いので別スレッドで呼ぶ。
    /// </summary>
    public static WasapiAudioOutput Create(string soundFontPath)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sampleRate = device.AudioClient.MixFormat.SampleRate;

        var engine = new PlaybackEngine(soundFontPath, sampleRate);
        var output = new WasapiOut(device, AudioClientShareMode.Shared, true, LatencyMilliseconds);
        output.Init(new EngineSampleProvider(engine));
        output.Play();
        return new WasapiAudioOutput(engine, output);
    }

    public void Dispose()
    {
        _output.Stop();
        _output.Dispose();
    }

    /// <summary>
    /// エンジンの左右チャンネルをインターリーブされたステレオ float に変換する。
    /// </summary>
    private sealed class EngineSampleProvider(PlaybackEngine engine) : ISampleProvider
    {
        private float[] _left = [];
        private float[] _right = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(engine.SampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            var frames = count / 2;
            if (_left.Length < frames)
            {
                _left = new float[frames];
                _right = new float[frames];
            }

            engine.Render(_left.AsSpan(0, frames), _right.AsSpan(0, frames));
            for (var i = 0; i < frames; i++)
            {
                buffer[offset + i * 2] = _left[i];
                buffer[offset + i * 2 + 1] = _right[i];
            }

            return frames * 2;
        }
    }
}
