using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// ユーザーが指定したコード区間。判別時は確定ノードとして扱う。
/// </summary>
public sealed record ChordOverride(int Start, int Length, Chord Chord)
{
    public int End => Start + Length;
}

/// <summary>
/// 判別結果の1区間。
/// </summary>
public sealed record ChordSegment(int Start, int Length, Chord Chord, bool IsOverride)
{
    public int End => Start + Length;
}

/// <summary>
/// 判別に必要な曲の情報。
/// </summary>
public sealed record ChordDetectionContext(Key Key, int Ppq, int TicksPerBar);

/// <summary>
/// ベース（任意でメロディ）からコード進行を判別する。
/// 区間ごとにダイアトニックコードをスコアリングし、遷移スコアつきの Viterbi で全体最適を取る。
/// </summary>
public static class ChordDetector
{
    private const double BassRootScore = 2.0;
    private const double BassThirdScore = 1.0;
    private const double BassFifthScore = 1.2;
    private const double BassNonChordScore = -1.0;
    private const double MelodyChordToneScore = 1.0;
    private const double MelodyNonChordScore = -0.5;
    private const double SegmentHeadAccent = 1.5;
    private const double OnBeatAccent = 1.2;
    private const double LeadingToneBonus = 1.0;
    private const double SubtonicPenalty = -1.0;

    /// <summary>当てはまり度（重み平均）に掛ける係数。遷移スコアとの釣り合いを決める</summary>
    private const double EmissionScale = 2.0;

    /// <summary>
    /// [0, endTick) のコード進行を判別する。
    /// </summary>
    public static IReadOnlyList<ChordSegment> Detect(
        IReadOnlyList<Note> bass,
        IReadOnlyList<Note> melody,
        int endTick,
        ChordDetectionContext context,
        SmartChordSettings settings,
        IReadOnlyList<ChordOverride> overrides)
    {
        var grid = settings.SegmentTicks > 0 ? settings.SegmentTicks : context.TicksPerBar;
        var slots = BuildSlots(endTick, grid, overrides);
        var candidates = DiatonicChords.GetCandidates(context.Key, settings.MinorDominant);

        // 空でない区間とオーバーライド区間だけを Viterbi のノードにする
        var nodes = new List<Node>();
        foreach (var slot in slots)
        {
            if (slot.Override is { } ov)
            {
                nodes.Add(new Node(slot, [ov.Chord], [0]));
                continue;
            }

            if (!HasNotes(bass, melody, slot)) continue;

            var chords = candidates.Select(c => c.Chord).ToArray();
            var scores = candidates.Select(c => Emission(c, bass, melody, slot, context, settings)).ToArray();
            nodes.Add(new Node(slot, chords, scores));
        }

        if (nodes.Count == 0) return [];

        var path = Viterbi(nodes, context.Key);
        return BuildResult(slots, nodes, path, bass);
    }

    #region 区間分割

    private readonly record struct Slot(int Start, int End, ChordOverride? Override)
    {
        public int Length => End - Start;
    }

    /// <summary>
    /// オーバーライド区間を確定させ、残りの隙間をグリッドで区切る。
    /// </summary>
    private static List<Slot> BuildSlots(int endTick, int grid, IReadOnlyList<ChordOverride> overrides)
    {
        var slots = new List<Slot>();
        var cursor = 0;
        foreach (var ov in overrides.OrderBy(o => o.Start))
        {
            var start = Math.Max(ov.Start, cursor);
            if (start >= ov.End) continue;

            AddGap(slots, cursor, Math.Min(start, endTick), grid);
            slots.Add(new Slot(start, ov.End, ov));
            cursor = ov.End;
        }

        AddGap(slots, cursor, endTick, grid);
        return slots;
    }

    private static void AddGap(List<Slot> slots, int start, int end, int grid)
    {
        var tick = start;
        while (tick < end)
        {
            var next = Math.Min(end, (tick / grid + 1) * grid);
            slots.Add(new Slot(tick, next, null));
            tick = next;
        }
    }

    #endregion

    #region スコアリング

    private static bool HasNotes(IReadOnlyList<Note> bass, IReadOnlyList<Note> melody, Slot slot) =>
        bass.Any(n => Overlap(n, slot) > 0) || melody.Any(n => Overlap(n, slot) > 0);

    /// <summary>
    /// 区間に対する候補コードの当てはまり度。ノーツの重み合計で正規化した平均値を使う。
    /// 区間長や音数に依存せず、遷移スコアと同じ尺度で比較できるようにするため。
    /// </summary>
    private static double Emission(ChordCandidate candidate, IReadOnlyList<Note> bass, IReadOnlyList<Note> melody,
        Slot slot, ChordDetectionContext context, SmartChordSettings settings)
    {
        var tones = candidate.Chord.GetPitchClasses();
        var score = 0.0;
        var totalWeight = 0.0;

        foreach (var note in bass)
        {
            var w = Weight(note, slot, context.Ppq);
            if (w <= 0) continue;

            totalWeight += w;
            var pc = note.PitchClass;
            if (pc == tones[0]) score += BassRootScore * w;
            else if (pc == tones[1]) score += BassThirdScore * w;
            else if (pc == tones[2]) score += BassFifthScore * w;
            else score += BassNonChordScore * w;
        }

        foreach (var note in melody)
        {
            var w = Weight(note, slot, context.Ppq) * settings.MelodyWeight;
            if (w <= 0) continue;

            totalWeight += w;
            score += (tones.Contains(note.PitchClass) ? MelodyChordToneScore : MelodyNonChordScore) * w;
        }

        if (totalWeight > 0)
            score = score / totalWeight * EmissionScale;
        score += candidate.Prior;

        if (candidate.IsBorrowedDominant && settings.MinorDominant == MinorDominantMode.Auto)
            score += BorrowedDominantEvidence(bass, melody, slot, context.Key);

        return score;
    }

    /// <summary>
    /// 借用 V の判断材料。導音があれば加点、自然短音階の7度（下主音）があれば減点。
    /// </summary>
    private static double BorrowedDominantEvidence(IReadOnlyList<Note> bass, IReadOnlyList<Note> melody, Slot slot,
        Key key)
    {
        var leadingTone = (key.Tonic + 11) % 12;
        var subtonic = (key.Tonic + 10) % 12;
        var notes = bass.Concat(melody).Where(n => Overlap(n, slot) > 0).Select(n => n.PitchClass).ToHashSet();

        var evidence = 0.0;
        if (notes.Contains(leadingTone)) evidence += LeadingToneBonus;
        if (notes.Contains(subtonic)) evidence += SubtonicPenalty;
        return evidence;
    }

    /// <summary>
    /// ノートの重み。区間内で鳴っている拍数に、区間頭・拍頭のアクセントを掛ける。
    /// </summary>
    private static double Weight(Note note, Slot slot, int ppq)
    {
        var overlap = Overlap(note, slot);
        if (overlap <= 0) return 0;

        var w = (double)overlap / ppq;
        if (note.Tick == slot.Start) w *= SegmentHeadAccent;
        else if (note.Tick >= slot.Start && note.Tick % ppq == 0) w *= OnBeatAccent;
        return w;
    }

    private static int Overlap(Note note, Slot slot) =>
        Math.Min(note.EndTick, slot.End) - Math.Max(note.Tick, slot.Start);

    #endregion

    #region Viterbi

    private sealed record Node(Slot Slot, Chord[] Chords, double[] Scores);

    private static int[] Viterbi(List<Node> nodes, Key key)
    {
        var dp = new double[nodes.Count][];
        var back = new int[nodes.Count][];

        var first = nodes[0];
        dp[0] = first.Chords.Select((c, i) => first.Scores[i] + ChordTransitions.Score(key, null, c)).ToArray();
        back[0] = new int[first.Chords.Length];

        for (var n = 1; n < nodes.Count; n++)
        {
            var prev = nodes[n - 1];
            var cur = nodes[n];
            dp[n] = new double[cur.Chords.Length];
            back[n] = new int[cur.Chords.Length];

            for (var c = 0; c < cur.Chords.Length; c++)
            {
                var best = double.NegativeInfinity;
                var bestPrev = 0;
                for (var p = 0; p < prev.Chords.Length; p++)
                {
                    var s = dp[n - 1][p] + ChordTransitions.Score(key, prev.Chords[p], cur.Chords[c]);
                    if (s > best)
                    {
                        best = s;
                        bestPrev = p;
                    }
                }

                dp[n][c] = best + cur.Scores[c];
                back[n][c] = bestPrev;
            }
        }

        var path = new int[nodes.Count];
        var last = nodes.Count - 1;
        path[last] = Array.IndexOf(dp[last], dp[last].Max());
        for (var n = last; n > 0; n--)
            path[n - 1] = back[n][path[n]];
        return path;
    }

    #endregion

    #region 結果の組み立て

    /// <summary>
    /// Viterbi の結果を全区間に展開する。音のない区間は直前（曲頭なら直後）のコードを引き継ぐ。
    /// </summary>
    private static IReadOnlyList<ChordSegment> BuildResult(List<Slot> slots, List<Node> nodes, int[] path,
        IReadOnlyList<Note> bass)
    {
        var decided = new Dictionary<int, Chord>();
        for (var n = 0; n < nodes.Count; n++)
            decided[nodes[n].Slot.Start] = nodes[n].Chords[path[n]];

        var result = new List<ChordSegment>();
        Chord? current = nodes[0].Chords[path[0]];
        foreach (var slot in slots)
        {
            if (decided.TryGetValue(slot.Start, out var chord))
            {
                current = chord;
                if (slot.Override is null)
                    chord = WithInversion(chord, bass, slot);
                result.Add(new ChordSegment(slot.Start, slot.Length, chord, slot.Override is not null));
            }
            else
            {
                result.Add(new ChordSegment(slot.Start, slot.Length, current.Value.RootPosition, false));
            }
        }

        return result;
    }

    /// <summary>
    /// 区間頭で鳴っているベース音が根音以外の構成音なら、転回形として扱う。
    /// </summary>
    private static Chord WithInversion(Chord chord, IReadOnlyList<Note> bass, Slot slot)
    {
        var head = bass
            .Where(n => Overlap(n, slot) > 0)
            .OrderBy(n => Math.Max(n.Tick, slot.Start))
            .ThenBy(n => n.Pitch)
            .Select(n => (Note?)n)
            .FirstOrDefault();

        if (head is not { } note) return chord;

        var pc = note.PitchClass;
        return pc != chord.Root && chord.GetPitchClasses().Contains(pc) ? chord with { Bass = pc } : chord;
    }

    #endregion
}
