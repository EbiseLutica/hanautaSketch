namespace HanautaSketch.Engine.Core;

/// <summary>テンポの変更点。変更点から次の変更点まで一定（段差で切り替わる）</summary>
public readonly record struct TempoChange(int Tick, double Bpm);

/// <summary>拍子の変更点。小節頭にだけ置けるので、小節番号（0始まり）で持つ。拍子は n/4 のみ</summary>
public readonly record struct MeterChange(int Bar, int BeatsPerBar);

/// <summary>キーの変更点</summary>
public readonly record struct KeyChange(int Tick, Key Key);

/// <summary>変更点の種類</summary>
public enum ConductorChangeKind
{
    Meter,
    Tempo,
    Key,
}

/// <summary>
/// 曲のテンポ・拍子・キーの変化（SMF のコンダクタートラック相当）。
/// どの種類も曲頭（tick 0 / 小節 0）に必ず1つ変更点を持つ。曲頭の変更点は消せない。
/// 小節と tick、tick と秒の変換もここで行う。
/// </summary>
public sealed class Conductor
{
    public const double MinBpm = 20;
    public const double MaxBpm = 400;
    public const int MinBeatsPerBar = 1;
    public const int MaxBeatsPerBar = 16;

    private readonly List<TempoChange> _tempos = [new(0, 120)];
    private readonly List<MeterChange> _meters = [new(0, 4)];
    private readonly List<KeyChange> _keys = [new(0, new Key(0, Mode.Major))];

    public Conductor(int ppq = Song.DefaultPpq)
    {
        Ppq = ppq;
    }

    /// <summary>4分音符あたりの tick 数</summary>
    public int Ppq { get; }

    /// <summary>テンポの変更点（tick の昇順）</summary>
    public IReadOnlyList<TempoChange> Tempos => _tempos;

    /// <summary>拍子の変更点（小節の昇順）</summary>
    public IReadOnlyList<MeterChange> Meters => _meters;

    /// <summary>キーの変更点（tick の昇順）</summary>
    public IReadOnlyList<KeyChange> Keys => _keys;

    #region 変更点の編集

    /// <summary>tick の位置にテンポの変更点を置く。同じ位置にあれば値を差し替える</summary>
    public void SetTempo(int tick, double bpm) =>
        Upsert(_tempos, new TempoChange(Math.Max(0, tick), Math.Clamp(bpm, MinBpm, MaxBpm)), c => c.Tick);

    /// <summary>小節頭に拍子の変更点を置く。同じ小節にあれば値を差し替える</summary>
    public void SetMeter(int bar, int beatsPerBar) =>
        Upsert(_meters, new MeterChange(Math.Max(0, bar), Math.Clamp(beatsPerBar, MinBeatsPerBar, MaxBeatsPerBar)),
            c => c.Bar);

    /// <summary>tick の位置にキーの変更点を置く。同じ位置にあれば値を差し替える</summary>
    public void SetKey(int tick, Key key) => Upsert(_keys, new KeyChange(Math.Max(0, tick), key), c => c.Tick);

    /// <summary>テンポの変更点を消す。曲頭のものは消せない</summary>
    public bool RemoveTempo(int tick) => tick > 0 && _tempos.RemoveAll(c => c.Tick == tick) > 0;

    public bool RemoveMeter(int bar) => bar > 0 && _meters.RemoveAll(c => c.Bar == bar) > 0;

    public bool RemoveKey(int tick) => tick > 0 && _keys.RemoveAll(c => c.Tick == tick) > 0;

    /// <summary>すべての変更点を消し、曲頭の値だけにする</summary>
    public void Reset(double bpm, int beatsPerBar, Key key)
    {
        _tempos.Clear();
        _meters.Clear();
        _keys.Clear();
        SetTempo(0, bpm);
        SetMeter(0, beatsPerBar);
        SetKey(0, key);
    }

    public void CopyFrom(Conductor other)
    {
        _tempos.Clear();
        _tempos.AddRange(other._tempos);
        _meters.Clear();
        _meters.AddRange(other._meters);
        _keys.Clear();
        _keys.AddRange(other._keys);
    }

    public Conductor Clone()
    {
        var clone = new Conductor(Ppq);
        clone.CopyFrom(this);
        return clone;
    }

    public bool ContentEquals(Conductor other) =>
        _tempos.SequenceEqual(other._tempos) && _meters.SequenceEqual(other._meters) && _keys.SequenceEqual(other._keys);

    private static void Upsert<T>(List<T> list, T change, Func<T, int> position)
    {
        var index = list.FindIndex(c => position(c) >= position(change));
        if (index < 0)
            list.Add(change);
        else if (position(list[index]) == position(change))
            list[index] = change;
        else
            list.Insert(index, change);
    }

    #endregion

    #region 値の参照

    public double BpmAt(int tick) => _tempos.Last(c => c.Tick <= Math.Max(0, tick)).Bpm;

    public Key KeyAt(int tick) => _keys.Last(c => c.Tick <= Math.Max(0, tick)).Key;

    public int BeatsPerBarAt(int tick) => MeterOfBar(BarAt(tick)).BeatsPerBar;

    public int TicksPerBarAt(int tick) => BeatsPerBarAt(tick) * Ppq;

    /// <summary>
    /// [0, endTick) をキーの変わり目で区切った区間。
    /// </summary>
    public IEnumerable<(int Start, int End, Key Key)> KeyRegions(int endTick)
    {
        for (var i = 0; i < _keys.Count && _keys[i].Tick < endTick; i++)
        {
            var end = i + 1 < _keys.Count ? Math.Min(_keys[i + 1].Tick, endTick) : endTick;
            yield return (_keys[i].Tick, end, _keys[i].Key);
        }
    }

    #endregion

    #region 小節

    private MeterChange MeterOfBar(int bar) => _meters.Last(c => c.Bar <= Math.Max(0, bar));

    /// <summary>
    /// 拍子の区間（開始小節・開始 tick・1小節の tick 数）を順に返す。
    /// </summary>
    private IEnumerable<(int Bar, int Tick, int TicksPerBar)> MeterSpans()
    {
        var tick = 0;
        for (var i = 0; i < _meters.Count; i++)
        {
            var ticksPerBar = _meters[i].BeatsPerBar * Ppq;
            yield return (_meters[i].Bar, tick, ticksPerBar);
            if (i + 1 < _meters.Count) tick += (_meters[i + 1].Bar - _meters[i].Bar) * ticksPerBar;
        }
    }

    /// <summary>tick を含む小節の番号（0始まり）</summary>
    public int BarAt(int tick)
    {
        tick = Math.Max(0, tick);
        var span = MeterSpans().Last(s => s.Tick <= tick);
        return span.Bar + (tick - span.Tick) / span.TicksPerBar;
    }

    /// <summary>小節頭の tick</summary>
    public int BarToTick(int bar)
    {
        bar = Math.Max(0, bar);
        var span = MeterSpans().Last(s => s.Bar <= bar);
        return span.Tick + (bar - span.Bar) * span.TicksPerBar;
    }

    /// <summary>tick を含む小節の頭</summary>
    public int BarStartAt(int tick) => BarToTick(BarAt(tick));

    /// <summary>tick 以上で最初の小節頭</summary>
    public int CeilToBar(int tick)
    {
        var start = BarStartAt(tick);
        return start == tick ? tick : BarToTick(BarAt(tick) + 1);
    }

    /// <summary>tick に最も近い小節頭</summary>
    public int RoundToBar(double tick)
    {
        var bar = BarAt((int)Math.Max(0, tick));
        var (start, next) = (BarToTick(bar), BarToTick(bar + 1));
        return tick - start < next - tick ? start : next;
    }

    /// <summary>小節の中での位置（tick）</summary>
    public int PositionInBar(int tick) => tick - BarStartAt(tick);

    /// <summary>[from, to) に含まれる小節頭</summary>
    public IEnumerable<int> BarStarts(int from, int to)
    {
        for (var bar = BarAt(from); ; bar++)
        {
            var tick = BarToTick(bar);
            if (tick >= to) yield break;
            if (tick >= from) yield return tick;
        }
    }

    #endregion

    #region 時間

    /// <summary>曲頭からの秒数</summary>
    public double TickToSeconds(double tick)
    {
        var seconds = 0.0;
        for (var i = 0; i < _tempos.Count; i++)
        {
            var start = _tempos[i].Tick;
            if (tick <= start) break;
            var end = i + 1 < _tempos.Count ? Math.Min(tick, _tempos[i + 1].Tick) : tick;
            seconds += (end - start) / Ppq * 60.0 / _tempos[i].Bpm;
        }
        return seconds;
    }

    /// <summary>曲頭からの秒数に対応する tick</summary>
    public double SecondsToTick(double seconds)
    {
        var elapsed = 0.0;
        for (var i = 0; i < _tempos.Count; i++)
        {
            var secondsPerTick = 60.0 / _tempos[i].Bpm / Ppq;
            if (i + 1 < _tempos.Count)
            {
                var span = (_tempos[i + 1].Tick - _tempos[i].Tick) * secondsPerTick;
                if (seconds < elapsed + span) return _tempos[i].Tick + (seconds - elapsed) / secondsPerTick;
                elapsed += span;
            }
            else
            {
                return _tempos[i].Tick + (seconds - elapsed) / secondsPerTick;
            }
        }
        return 0;
    }

    #endregion
}
