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
/// 無声の区間、半音以上の安定した音高の変化、音量の谷（同じ音の歌い直し）で区切る。
/// </summary>
public static class NoteSegmenter
{
    /// <summary>オクターブ誤りと単発の外れ値を均す中央値フィルタの幅（フレーム数）</summary>
    private const int SmoothingWidth = 5;

    /// <summary>今の音からこれ以上離れたら別の音の候補とみなす（半音）</summary>
    private const double PitchChangeSemitones = 0.6;

    /// <summary>音高の変化がこのフレーム数続いたら区切る</summary>
    private const int PitchChangeFrames = 4;

    /// <summary>この長さ以下の無声の切れ目はつなげる（フレーム数）。子音や息継ぎの揺れを吸収する</summary>
    private const int MaxBridgedGapFrames = 2;

    /// <summary>前後の山に対し、音量がこの割合を下回る谷で区切る</summary>
    private const double DipRatio = 0.55;

    /// <summary>谷の前後の山を探す範囲（フレーム数）</summary>
    private const int DipSearchFrames = 12;

    /// <summary>これより短い音は捨てる（秒）</summary>
    public const double DefaultMinDuration = 0.06;

    public static IReadOnlyList<SungNote> Segment(IReadOnlyList<PitchFrame> frames, double minDuration = DefaultMinDuration)
    {
        if (frames.Count == 0) return [];
        var hop = frames.Count > 1 ? frames[1].Time - frames[0].Time : PitchTracker.HopSize / (double)PitchTracker.AnalysisRate;
        var midi = Smooth(frames);

        var notes = new List<SungNote>();
        foreach (var (first, last) in VoicedRuns(midi))
        {
            foreach (var (s, e) in SplitRun(frames, midi, first, last))
            {
                var pitches = Enumerable.Range(s, e - s + 1).Where(i => midi[i].HasValue).Select(i => midi[i]!.Value).ToList();
                if (pitches.Count == 0) continue;
                var start = frames[s].Time - hop / 2;
                var end = frames[e].Time + hop / 2;
                if (end - start < minDuration) continue;
                notes.Add(new SungNote(start, end, Median(pitches)));
            }
        }
        return notes;
    }

    /// <summary>
    /// 有声フレームの音高を中央値フィルタで均す。周りから1オクターブずれたフレームは先に周りへ寄せる。
    /// </summary>
    private static double?[] Smooth(IReadOnlyList<PitchFrame> frames)
    {
        var result = new double?[frames.Count];
        var half = SmoothingWidth / 2;
        for (var i = 0; i < frames.Count; i++)
        {
            if (!frames[i].IsVoiced) continue;
            var neighbors = new List<double>();
            for (var j = Math.Max(0, i - half); j <= Math.Min(frames.Count - 1, i + half); j++)
            {
                if (frames[j].Midi is { } m) neighbors.Add(m);
            }
            var median = Median(neighbors);
            result[i] = Median(neighbors.Select(m => m + 12 * Math.Round((median - m) / 12)).ToList());
        }
        return result;
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

    /// <summary>有声区間を、音高の変化と音量の谷でさらに区切る</summary>
    private static IEnumerable<(int First, int Last)> SplitRun(IReadOnlyList<PitchFrame> frames, double?[] midi, int first, int last)
    {
        var cuts = new SortedSet<int>();
        foreach (var dip in VolumeDips(frames, first, last)) cuts.Add(dip);

        // 今の音の基準（区切り後の数フレームの中央値）から離れた状態が続いたら区切る
        var segmentStart = first;
        var reference = Reference(midi, first, last);
        var deviating = 0;
        for (var i = first; i <= last; i++)
        {
            if (cuts.Contains(i))
            {
                segmentStart = i;
                reference = Reference(midi, i, last);
                deviating = 0;
                continue;
            }
            if (midi[i] is not { } m || reference is not { } r) continue;
            if (Math.Abs(m - r) < PitchChangeSemitones)
            {
                deviating = 0;
                continue;
            }
            if (++deviating < PitchChangeFrames) continue;

            var cut = i - PitchChangeFrames + 1;
            if (cut > segmentStart) cuts.Add(cut);
            segmentStart = cut;
            reference = Reference(midi, cut, last);
            deviating = 0;
        }

        var start = first;
        foreach (var cut in cuts.Where(c => c > first && c <= last))
        {
            yield return (start, cut - 1);
            start = cut;
        }
        yield return (start, last);
    }

    private static double? Reference(double?[] midi, int from, int last)
    {
        var values = new List<double>();
        for (var i = from; i <= last && values.Count < PitchChangeFrames; i++)
        {
            if (midi[i] is { } m) values.Add(m);
        }
        return values.Count > 0 ? Median(values) : null;
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
