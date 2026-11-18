using HanautaSketch.Engine.Audio;

namespace HanautaSketch.Audio;

/// <summary>
/// <see cref="PlaybackEngine"/> の出力を OS のオーディオ API に流す。
/// 出力は常時開きっぱなしにし、停止中は無音を流す。
/// </summary>
public interface IAudioOutput : IDisposable
{
    PlaybackEngine Engine { get; }
}

/// <summary>
/// <see cref="IAudioOutput"/> を作る。全 OS で OpenAL Soft（Silk.NET）を使う（Windows では内部で WASAPI に出力される）。
/// </summary>
public static class AudioOutput
{
    /// <summary>
    /// 既定の出力デバイスのサンプルレートでエンジンを作り、出力を開始する。SF2 の読み込みは重いので別スレッドで呼ぶ。
    /// </summary>
    public static IAudioOutput Create(string soundFontPath) => OpenAlAudioOutput.Create(soundFontPath);
}
