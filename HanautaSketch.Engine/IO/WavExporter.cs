using HanautaSketch.Engine.Audio;
using HanautaSketch.Engine.Core;
using MeltySynth;

namespace HanautaSketch.Engine.IO;

/// <summary>
/// WAV 書き出しの設定。
/// </summary>
public sealed record WavExportOptions
{
    /// <summary>選べるサンプルレート（Hz）</summary>
    public static IReadOnlyList<int> SupportedSampleRates { get; } = [44100, 48000];

    public int SampleRate { get; init; } = 44100;

    public WavSampleFormat Format { get; init; } = WavSampleFormat.Pcm16;

    /// <summary>
    /// 最後の音のあとに余韻（<see cref="PlaybackEngine.ReleaseTailSeconds"/>）を残すか。残さなければ最後のノートオフで切る
    /// </summary>
    public bool IncludeReleaseTail { get; init; }
}

/// <summary>
/// 曲を SoundFont で鳴らした音を、ステレオの WAV として書き出す。
/// 曲頭から、鳴らすトラック（ミュート・ソロを反映）の最後の音まで（設定により余韻も）を書き出す。
/// スマートトラックは生成済みのノーツを使うので、呼び出し前に再生成しておくこと。
/// </summary>
public static class WavExporter
{
    public const string FileExtension = ".wav";

    /// <summary>
    /// 書き出しを準備する。この時点の曲（ノーツ・ミュート・ソロ）をスナップショットとして使う。
    /// 曲を触るので UI スレッドで呼び、時間のかかる <see cref="PreparedWavExport.WriteTo"/> は別スレッドで呼んでよい。
    /// </summary>
    public static PreparedWavExport Prepare(Song song, SoundFont soundFont, WavExportOptions options)
    {
        if (!WavExportOptions.SupportedSampleRates.Contains(options.SampleRate))
            throw new ArgumentOutOfRangeException(nameof(options), options.SampleRate, "Unsupported sample rate.");

        var engine = new PlaybackEngine(soundFont, options.SampleRate);
        engine.Play(song, options: new PlaybackOptions { AudibleTracksOnly = true });
        var tail = options.IncludeReleaseTail ? (long)(PlaybackEngine.ReleaseTailSeconds * options.SampleRate) : 0;
        return new PreparedWavExport(engine, options.Format, engine.LastNoteOffSample + tail);
    }
}

/// <summary>
/// 準備済みの WAV 書き出し。<see cref="WriteTo"/> は1回だけ呼ぶ。
/// </summary>
public sealed class PreparedWavExport
{
    private const int BlockFrames = 4096;

    private readonly PlaybackEngine _engine;
    private readonly WavSampleFormat _format;

    internal PreparedWavExport(PlaybackEngine engine, WavSampleFormat format, long frames)
    {
        _engine = engine;
        _format = format;
        Frames = frames;
    }

    /// <summary>書き出す長さ（フレーム数）</summary>
    public long Frames { get; }

    /// <summary>WAV の上限（4GB）に収まるか。収まらなければ <see cref="WriteTo"/> は <see cref="IOException"/> を投げる</summary>
    public bool FitsInWav => WavStreamWriter.Fits(Frames, _format);

    /// <summary>
    /// 鳴らしながら stream に書き込む。ブロックごとに書くので、長い曲でもメモリは増えない。
    /// </summary>
    public void WriteTo(Stream stream)
    {
        var writer = new WavStreamWriter(stream, _engine.SampleRate, _format, Frames);
        var left = new float[BlockFrames];
        var right = new float[BlockFrames];
        for (var remaining = Frames; remaining > 0;)
        {
            var count = (int)Math.Min(BlockFrames, remaining);
            _engine.Render(left.AsSpan(0, count), right.AsSpan(0, count));
            writer.Write(left.AsSpan(0, count), right.AsSpan(0, count));
            remaining -= count;
        }
        writer.Complete();
    }
}
