namespace HanautaSketch.Audio;

/// <summary>
/// マイクからの録音。開始するとすぐ取り込みを始め、モノラルのサンプルを溜め続ける。
/// </summary>
public interface IAudioInput : IDisposable
{
    int SampleRate { get; }

    /// <summary>取り込み済みのサンプル数（デバイスに溜まっていて、まだ引き取っていない分も含む）</summary>
    long CapturedSampleCount { get; }

    /// <summary>引き取ったサンプルのうち、from 以降をコピーして返す</summary>
    float[] CopySamples(int from);

    /// <summary>取り込みを止め、残りを引き取って全サンプルを返す</summary>
    float[] Stop();
}

/// <summary>
/// <see cref="IAudioInput"/> を作る。全 OS で OpenAL Soft のキャプチャを使う。
/// </summary>
public static class AudioInput
{
    /// <summary>録音に使える入力デバイスの名前</summary>
    public static IReadOnlyList<string> GetDeviceNames() => OpenAlAudioInput.GetDeviceNames();

    /// <summary>入力デバイスを開いて録音を始める。deviceName が null か見つからなければ既定のデバイスを使う</summary>
    public static IAudioInput Start(string? deviceName) => OpenAlAudioInput.Start(deviceName);
}
