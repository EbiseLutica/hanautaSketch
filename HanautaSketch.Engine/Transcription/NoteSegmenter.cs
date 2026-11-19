namespace HanautaSketch.Engine.Transcription;

/// <summary>
/// ピッチの流れから切り出した音1つ分。時間は音声の先頭からの秒。
/// </summary>
/// <param name="Start">開始（秒）</param>
/// <param name="End">終了（秒、排他）</param>
/// <param name="Midi">音高（MIDI ノート番号の小数。フレームの中央値）</param>
public readonly record struct SungNote(double Start, double End, double Midi)
{
    public double Duration => End - Start;
}

/// <summary>
/// ピッチ検出のフレーム列を音ごとに区切る。
/// 無声の区間、半音以上の安定した音高の変化、音量の深い谷（同じ音の歌い直し）で区切る。
/// </summary>
public static class NoteSegmenter
{
    /// <summary>
    /// オクターブ誤りを直すときに見る前後の幅（フレーム数）。
    /// 前後 5 フレームの多数派に寄せるので、5 フレーム（50ms）続く誤りまで直せる
    /// </summary>
    private const int OctaveFoldHalfWidth = 5;

    /// <summary>単発の外れ値を均す中央値フィルタの前後の幅（フレーム数）</summary>
    private const int SmoothingHalfWidth = 2;

    /// <summary>
    /// 音高の変化を判定する前に、ビブラートを均す中央値フィルタの前後の幅（フレーム数）。
    /// 前後 8 フレームで約 170ms。ビブラート（4.5〜7Hz）の1周期ほどを覆う
    /// </summary>
    private const int TrendHalfWidth = 8;

    /// <summary>今の音からこれ以上離れたら別の音の候補とみなす（半音）</summary>
    private const double PitchChangeSemitones = 0.6;

    /// <summary>今の音の基準にする、直近のフレーム数（400ms）。長く伸ばした音でも計算量を抑える</summary>
    private const int ReferenceFrames = 40;

    /// <summary>音高の変化がこのフレーム数続いたら区切る</summary>
    private const int PitchChangeFrames = 4;

    /// <summary>この長さ以下の無声の切れ目はつなげる（フレーム数）。音量を保ったまま周期性だけ乱れたフレームを吸収する</summary>
    private const int MaxBridgedGapFrames = 2;

    /// <summary>
    /// 前後の山に対し、音量がこの割合を下回る谷で区切る。
    /// 声の自然な音量の揺れ（トレモロ）では区切らないよう、歌い直しで生じる深い谷だけを拾う
    /// </summary>
    private const double DipRatio = 0.35;

    /// <summary>谷の前後の山を探す範囲（フレーム数）</summary>
    private const int DipSearchFrames = 12;

    /// <summary>区切りからこのフレーム数以内の谷では区切らない</summary>
    private const int MinDipSpacingFrames = 8;

    /// <summary>これより短い音は捨てる（秒）</summary>
    public const double DefaultMinDuration = 0.06;

    public static IReadOnlyList<SungNote> Segment(IReadOnlyList<PitchFrame> frames, double minDuration = DefaultMinDuration)
    {
        if (frames.Count == 0) return [];
        var hop = frames.Count > 1 ? frames[1].Time - frames[0].Time : PitchTracker.HopSize / (double)PitchTracker.AnalysisRate;
        var raw = frames.Select(f => f.Midi).ToArray();

        var notes = new List<SungNote>();
        foreach (var (first, last) in VoicedRuns(raw))
        {
            var folded = FoldOctaves(raw, first, last);
            var pitch = MedianFilter(folded, first, last, SmoothingHalfWidth);
            var trend = MedianFilter(pitch, first, last, TrendHalfWidth);

            foreach (var (s, e) in SplitRun(frames, trend, first, last))
            {
                var pitches = Enumerable.Range(s, e - s + 1).Where(i => pitch[i].HasValue).Select(i => pitch[i]!.Value).ToList();
                if (pitches.Count == 0) continue;
                var start = frames[s].Time - hop / 2;
                var end = frames[e].Time + hop / 2;
                if (end - start < minDuration) continue;
                notes.Add(new SungNote(start, end, Median(pitches)));
            }
        }
        return notes;
    }

    /// <summary>有声フレームが続く範囲（短い切れ目はつなげる）</summary>
    private static IEnumerable<(int First, int Last)> VoicedRuns(double?[] midi)
    {
        var i = 0;
        while (i < midi.Length)
        {
            if (!midi[i].HasValue) { i++; continue; }
            var first = i;
            var last = i;
            var gap = 0;
            for (i++; i < midi.Length; i++)
            {
                if (midi[i].HasValue)
                {
                    last = i;
                    gap = 0;
                }
                else if (++gap > MaxBridgedGapFrames)
                {
                    break;
                }
            }
            yield return (first, last);
            i = last + 1;
        }
    }

    /// <summary>各フレームを、前後の中央値に最も近いオクターブへ寄せる（区間の外は見ない）</summary>
    private static double?[] FoldOctaves(double?[] midi, int first, int last)
    {
        var result = new double?[midi.Length];
        for (var i = first; i <= last; i++)
        {
            if (midi[i] is not { } own) continue;
            var median = Median(Window(midi, first, last, i, OctaveFoldHalfWidth));
            result[i] = own + 12 * Math.Round((median - own) / 12);
        }
        return result;
    }

    /// <summary>有声フレームだけで取る中央値フィルタ（区間の外は見ない）</summary>
    private static double?[] MedianFilter(double?[] midi, int first, int last, int half)
    {
        var result = new double?[midi.Length];
        for (var i = first; i <= last; i++)
        {
            if (midi[i].HasValue) result[i] = Median(Window(midi, first, last, i, half));
        }
        return result;
    }

    private static List<double> Window(double?[] midi, int first, int last, int center, int half)
    {
        var values = new List<double>();
        for (var j = Math.Max(first, center - half); j <= Math.Min(last, center + half); j++)
        {
            if (midi[j] is { } m) values.Add(m);
        }
        return values;
    }

    /// <summary>有声区間を、音高の変化と音量の谷でさらに区切る</summary>
    private static IEnumerable<(int First, int Last)> SplitRun(IReadOnlyList<PitchFrame> frames, double?[] trend, int first, int last)
    {
        var dips = VolumeDips(frames, first, last).ToHashSet();
        var cuts = new List<int>();

        // 区切り後の直近のフレームの中央値を今の音の基準にし、そこから離れた状態が続いたら区切る。
        // 離れている間のフレームは基準に入れない
        var reference = new Queue<double>();
        var lastCut = first;
        var deviating = 0;
        for (var i = first; i <= last; i++)
        {
            if (dips.Contains(i) && i - lastCut >= MinDipSpacingFrames)
            {
                cuts.Add(i);
                lastCut = i;
                reference.Clear();
                deviating = 0;
            }
            if (trend[i] is not { } m) continue;
            if (reference.Count == 0 || Math.Abs(m - Median(reference.ToArray())) < PitchChangeSemitones)
            {
                reference.Enqueue(m);
                if (reference.Count > ReferenceFrames) reference.Dequeue();
                deviating = 0;
                continue;
            }
            if (++deviating < PitchChangeFrames) continue;

            var cut = i - PitchChangeFrames + 1;
            cuts.Add(cut);
            lastCut = cut;
            reference.Clear();
            for (var j = cut; j <= i; j++)
            {
                if (trend[j] is { } t) reference.Enqueue(t);
            }
            deviating = 0;
        }

        var start = first;
        foreach (var cut in cuts.Where(c => c > first && c <= last).Distinct().Order())
        {
            yield return (start, cut - 1);
            start = cut;
        }
        yield return (start, last);
    }

    /// <summary>前後に十分大きい山を持つ音量の谷（区切りはその直後のフレーム）</summary>
    private static IEnumerable<int> VolumeDips(IReadOnlyList<PitchFrame> frames, int first, int last)
    {
        for (var i = first + 1; i < last; i++)
        {
            var rms = frames[i].Rms;
            if (rms > frames[i - 1].Rms || rms > frames[i + 1].Rms) continue;
            var before = MaxRms(frames, Math.Max(first, i - DipSearchFrames), i - 1);
            var after = MaxRms(frames, i + 1, Math.Min(last, i + DipSearchFrames));
            if (rms < before * DipRatio && rms < after * DipRatio) yield return i + 1;
        }
    }

    private static double MaxRms(IReadOnlyList<PitchFrame> frames, int from, int to)
    {
        var max = 0.0;
        for (var i = from; i <= to; i++) max = Math.Max(max, frames[i].Rms);
        return max;
    }

    internal static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
