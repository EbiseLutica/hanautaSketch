using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Audio;

/// <summary>
/// クリック1回分。
/// </summary>
/// <param name="Seconds">再生開始位置からの秒（カウントインは負）</param>
/// <param name="IsAccent">小節の頭（カウントインでは最初の拍）か</param>
public readonly record struct MetronomeClick(double Seconds, bool IsAccent);

/// <summary>
/// カウントインとメトロノームのクリックの位置を決める。
/// </summary>
public static class MetronomeClicks
{
    /// <summary>小節の頭で鳴らす GM ドラムの音（High Wood Block）</summary>
    public const int AccentPitch = 76;

    /// <summary>それ以外の拍で鳴らす GM ドラムの音（Low Wood Block）</summary>
    public const int BeatPitch = 77;

    /// <summary>
    /// 再生開始位置を基準にしたクリックの並び（時刻順）を返す。
    /// カウントインは開始位置のテンポで、開始位置の手前に拍数分を刻む。
    /// メトロノームは開始位置以降の拍（tick が PPQ の倍数の位置）に、endTick の手前まで刻む。
    /// </summary>
    public static IReadOnlyList<MetronomeClick> Build(Conductor conductor, int startTick, PlaybackOptions options, int endTick)
    {
        var clicks = new List<MetronomeClick>();
        var secondsPerBeat = 60.0 / conductor.BpmAt(startTick);
        for (var i = 0; i < options.CountInBeats; i++)
            clicks.Add(new MetronomeClick(-(options.CountInBeats - i) * secondsPerBeat, i == 0));

        if (!options.Metronome) return clicks;

        var ppq = conductor.Ppq;
        var startSeconds = conductor.TickToSeconds(startTick);
        var firstBeat = (startTick + ppq - 1) / ppq * ppq;
        for (var tick = firstBeat; tick < endTick; tick += ppq)
        {
            clicks.Add(new MetronomeClick(conductor.TickToSeconds(tick) - startSeconds, conductor.PositionInBar(tick) == 0));
        }
        return clicks;
    }
}
