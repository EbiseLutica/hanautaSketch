namespace HanautaSketch.Engine.Core;

/// <summary>
/// タップの時刻（秒）から BPM を求める。直近 <see cref="WindowSize"/> 回のタップ間隔の平均を使い、
/// 前のタップから <see cref="RestartGapSeconds"/> 秒以上空いたら測り直す。
/// </summary>
public sealed class TapTempo
{
    /// <summary>平均に使う直近のタップ数</summary>
    public const int WindowSize = 8;

    /// <summary>この秒数以上タップが空いたら、前の測定を捨てて測り直す</summary>
    public const double RestartGapSeconds = 2.0;

    private readonly List<double> _taps = [];

    /// <summary>今の測定に使われているタップ数</summary>
    public int TapCount => _taps.Count;

    /// <summary>候補 BPM（小数第1位まで）。タップが2回未満のときは null</summary>
    public double? Bpm
    {
        get
        {
            if (_taps.Count < 2) return null;
            var span = _taps[^1] - _taps[0];
            if (span <= 0) return null;
            return Math.Round(60.0 * (_taps.Count - 1) / span, 1);
        }
    }

    /// <summary>タップを記録する。<paramref name="seconds"/> は単調増加する時刻</summary>
    public void Tap(double seconds)
    {
        if (_taps.Count > 0 && seconds - _taps[^1] > RestartGapSeconds) _taps.Clear();

        _taps.Add(seconds);
        if (_taps.Count > WindowSize) _taps.RemoveAt(0);
    }

    /// <summary>測定を捨てる</summary>
    public void Reset() => _taps.Clear();
}
