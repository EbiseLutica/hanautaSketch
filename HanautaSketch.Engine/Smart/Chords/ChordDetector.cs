using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Rhythm;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// ユーザーが指定したコード区間。判別時は確定区間として扱う。
/// </summary>
/// <param name="Chord">指定したコード。null なら N.C.（鳴らさない）</param>
public sealed record ChordOverride(int Start, int Length, Chord? Chord)
{
    public int End => Start + Length;
}

/// <summary>
/// 判別結果の1区間。同じコードが続く区間は連結済み。
/// </summary>
/// <param name="Chord">区間のコード。null なら N.C.（鳴らさない）</param>
public sealed record ChordSegment(int Start, int Length, Chord? Chord, bool IsOverride)
{
    public int End => Start + Length;

    /// <summary>コード名。N.C. なら "N.C."</summary>
    public string GetName(Key key) => Chord?.GetName(key) ?? NoChordName;

    public const string NoChordName = "N.C.";
}

/// <summary>
/// 判別に必要な曲の情報（キー・拍子の変化）。
/// </summary>
public sealed record ChordDetectionContext(Conductor Conductor)
{
    /// <summary>キーと拍子が曲全体で一定の場合</summary>
    public ChordDetectionContext(Key key, int ppq, int ticksPerBar) : this(CreateConductor(key, ppq, ticksPerBar))
    {
    }

    private static Conductor CreateConductor(Key key, int ppq, int ticksPerBar)
    {
        var conductor = new Conductor(ppq);
        conductor.Reset(120, ticksPerBar / ppq, key);
        return conductor;
    }
}

/// <summary>
/// ベース（任意でメロディ）からコード進行と、その区切りを判別する。
/// 区間の長さも探索対象にした semi-Markov Viterbi で、
/// 「区間全体の当てはまり度」「コード遷移」「区切り位置の区切れやすさ」の合計を最大化する。
/// </summary>
public static class ChordDetector
{
    private const double BassRootScore = 2.0;
    private const double BassThirdScore = 1.0;
    private const double BassFifthScore = 1.2;
    private const double BassNonChordScore = -1.0;

    /// <summary>同じ半小節のベースにルートがないとき、3度・5度の得点に掛ける係数</summary>
    private const double RootlessInversionFactor = 0.25;
    private const double MelodyChordToneScore = 1.0;
    private const double MelodyNonChordScore = -0.5;
    private const double SegmentHeadAccent = 1.5;
    private const double OnBeatAccent = 1.2;
    private const double LeadingToneBonus = 1.0;
    private const double SubtonicPenalty = -1.0;

    /// <summary>当てはまり度（重み平均）に掛ける係数</summary>
    private const double EmissionScale = 2.0;

    /// <summary>
    /// コードを切り替える基本コスト。区切り位置の区切れやすさ（拍節＋ベースの発音＋シンコペーション）を差し引いて払う。
    /// </summary>
    private const double ChangeCost = 3.0;

    /// <summary>区切れやすさがどれだけ高くても最低限払うコスト</summary>
    private const double MinChangeCost = 0.5;

    /// <summary>
    /// 小節頭以外を区切り候補にするのに必要な区切れやすさ。
    /// 発音のある半小節とシンコペーションは超え、オンビートの普通の発音（ウォーキングの2・4拍目など）は超えない。
    /// </summary>
    private const double CandidateThreshold = 1.9;

    /// <summary>
    /// [0, endTick) のコード進行を判別する。キーの変わり目で区切り、区間ごとにそのキーで判別する。
    /// </summary>
    public static IReadOnlyList<ChordSegment> Detect(
        IReadOnlyList<Note> bass,
        IReadOnlyList<Note> melody,
        int endTick,
        ChordDetectionContext context,
        SmartChordSettings settings,
        IReadOnlyList<ChordOverride> overrides) =>
        Detect(bass, melody, endTick, context, settings, overrides, strict: false);

    /// <summary>
    /// <paramref name="strict"/> が false なら、区間の最大長の制限で曲を覆えないとき制限を外して解き直す。
    /// true なら例外にする（テストで、制限で覆えない状況を見落とさないため）。
    /// </summary>
    internal static IReadOnlyList<ChordSegment> Detect(
        IReadOnlyList<Note> bass,
        IReadOnlyList<Note> melody,
        int endTick,
        ChordDetectionContext context,
        SmartChordSettings settings,
        IReadOnlyList<ChordOverride> overrides,
        bool strict)
    {
        if (endTick <= 0) return [];

        var validOverrides = NormalizeOverrides(overrides, endTick);
        if (bass.Count == 0 && melody.Count == 0)
            return validOverrides.Select(o => new ChordSegment(o.Start, o.Length, o.Chord, true)).ToList();

        var result = new List<ChordSegment>();
        foreach (var (start, end, key) in context.Conductor.KeyRegions(endTick))
        {
            var regionOverrides = validOverrides
                .Where(o => o.Start < end && start < o.End)
                .Select(o => o with { Start = Math.Max(o.Start, start), Length = Math.Min(o.End, end) - Math.Max(o.Start, start) })
                .ToList();
            var problem = new Problem(bass, melody, start, end, key, context.Conductor, settings, regionOverrides);
            var path = problem.Solve(limitLength: true);
            if (path is null)
            {
                if (strict)
                    throw new InvalidOperationException($"No chord path covers [{start}, {end}) within the maximum segment length.");
                path = problem.Solve(limitLength: false)
                    ?? throw new InvalidOperationException($"No chord path covers [{start}, {end}).");
            }
            result.AddRange(BuildResult(path, bass, context.Conductor));
        }
        return result;
    }

    /// <summary>
    /// 重なりを除き、曲の範囲に収める。
    /// </summary>
    private static List<ChordOverride> NormalizeOverrides(IReadOnlyList<ChordOverride> overrides, int endTick)
    {
        var result = new List<ChordOverride>();
        var cursor = 0;
        foreach (var ov in overrides.OrderBy(o => o.Start))
        {
            var start = Math.Max(ov.Start, cursor);
            var end = Math.Min(ov.End, endTick);
            if (start >= end) continue;

            result.Add(ov with { Start = start, Length = end - start });
            cursor = end;
        }
        return result;
    }

    #region 探索

    private readonly record struct PathSegment(int Start, int End, Chord? Chord, bool IsOverride);

    /// <summary>
    /// 1回の判別に必要な前計算と、semi-Markov Viterbi 本体。
    /// </summary>
    private sealed class Problem
    {
        private readonly IReadOnlyList<Note> _bass;
        private readonly IReadOnlyList<Note> _melody;
        private readonly Conductor _conductor;
        private readonly Key _key;
        private readonly int _ppq;
        private readonly SmartChordSettings _settings;
        private readonly List<ChordOverride> _overrides;
        private readonly IReadOnlyList<ChordCandidate> _candidates;
        /// <summary>状態として扱うコード。null は N.C. の override 専用の状態</summary>
        private readonly List<Chord?> _chords;
        private readonly int[] _positions;
        private readonly int _minLength;
        private readonly int _maxLength;

        /// <summary>[startTick, endTick) を key で判別する問題</summary>
        public Problem(IReadOnlyList<Note> bass, IReadOnlyList<Note> melody, int startTick, int endTick, Key key,
            Conductor conductor, SmartChordSettings settings, List<ChordOverride> overrides)
        {
            _bass = bass;
            _melody = melody;
            _conductor = conductor;
            _key = key;
            _ppq = conductor.Ppq;
            _settings = settings;
            _overrides = overrides;
            _candidates = DiatonicChords.GetCandidates(key, settings.MinorDominant);

            // 状態として扱うコード: 候補 → オーバーライド専用コード（N.C. を含む）の順
            _chords = _candidates.Select(c => (Chord?)c.Chord).ToList();
            foreach (var ov in overrides.Where(ov => !_chords.Contains(ov.Chord)))
                _chords.Add(ov.Chord);

            var unit = settings.BoundaryUnitTicks > 0 ? settings.BoundaryUnitTicks : _ppq;
            _minLength = settings.MinSegmentTicks > 0 ? settings.MinSegmentTicks : _ppq / 2;
            // 既定は2小節。区間内で一番長い小節で測る（曲頭のアウフタクトなど、短い小節を基準にすると曲を覆えなくなる）
            _maxLength = settings.MaxSegmentTicks > 0
                ? settings.MaxSegmentTicks
                : conductor.BarStarts(startTick, endTick).Append(startTick).Max(conductor.TicksPerBarAt) * 2;

            // 区切り候補位置: 小節頭＋リズム的に区切れやすい位置（グリッド・ベースの発音位置から選ぶ）＋オーバーライド境界。
            // オーバーライド内部は除く
            var positions = new SortedSet<int>(conductor.BarStarts(startTick, endTick)) { startTick, endTick };

            var rhythmic = new SortedSet<int>(BassRhythmAnalyzer.GetOnsets(bass));
            for (var t = (startTick / unit + 1) * unit; t < endTick; t += unit) rhythmic.Add(t);
            foreach (var t in rhythmic.Where(t => startTick < t && t < endTick))
            {
                if (BassRhythmAnalyzer.BoundaryStrength(bass, t, conductor) >= CandidateThreshold)
                    positions.Add(t);
            }

            foreach (var ov in overrides)
            {
                positions.Add(ov.Start);
                positions.Add(ov.End);
            }
            _positions = positions.Where(p => !overrides.Any(o => o.Start < p && p < o.End)).ToArray();
        }

        /// <summary>
        /// 最良の経路を返す。曲を覆う経路がなければ null。
        /// </summary>
        /// <param name="limitLength">区間の最大長の制限をかけるか</param>
        public List<PathSegment>? Solve(bool limitLength)
        {
            var p = _positions.Length;
            var c = _chords.Count;
            var dp = new double[p, c];
            var back = new (int Pos, int Chord)[p, c];
            for (var i = 0; i < p; i++)
            for (var k = 0; k < c; k++)
                dp[i, k] = double.NegativeInfinity;

            for (var j = 1; j < p; j++)
            {
                for (var i = j - 1; i >= 0; i--)
                {
                    var start = _positions[i];
                    var end = _positions[j];
                    var forced = _overrides.FirstOrDefault(o => o.Start == start && o.End == end);

                    if (forced is null)
                    {
                        // オーバーライドをまたぐ区間は不可。i を戻すほど長くなるので打ち切る
                        if (_overrides.Any(o => o.Start < end && start < o.End)) break;
                        if (limitLength && end - start > _maxLength) break;
                        if (end - start < _minLength && !AllowsShortSegment(i, j)) continue;
                    }

                    var segment = new SegmentEvidence(this, start, end);
                    for (var k = 0; k < c; k++)
                    {
                        double score;
                        if (forced is not null)
                        {
                            if (_chords[k] != forced.Chord) continue;
                            score = 0;
                        }
                        else
                        {
                            if (k >= _candidates.Count) continue;
                            score = segment.Score(_candidates[k]);
                        }

                        Relax(dp, back, i, j, k, score, forced is not null);
                    }
                }
            }

            return Backtrack(dp, back);
        }

        /// <summary>
        /// 位置 i で終わる最良の状態から、区間 (i, j) をコード k で繋いだときの値で dp[j, k] を更新する。
        /// </summary>
        private void Relax(double[,] dp, (int Pos, int Chord)[,] back, int i, int j, int k, double score, bool isOverride)
        {
            if (i == 0)
            {
                var s = score + TransitionScore(null, _chords[k]);
                if (s > dp[j, k])
                {
                    dp[j, k] = s;
                    back[j, k] = (-1, -1);
                }
                return;
            }

            var start = _positions[i];
            var boundaryCost = isOverride || IsOverrideEdge(start)
                ? 0
                : Math.Max(MinChangeCost,
                    ChangeCost - BassRhythmAnalyzer.BoundaryStrength(_bass, start, _conductor));

            for (var prev = 0; prev < _chords.Count; prev++)
            {
                if (double.IsNegativeInfinity(dp[i, prev])) continue;

                // 同じコードの継続は区切りとみなさない（結果上で連結される）
                var change = _chords[prev] == _chords[k]
                    ? 0
                    : TransitionScore(_chords[prev], _chords[k]) - boundaryCost;
                var s = dp[i, prev] + change + score;
                if (s > dp[j, k])
                {
                    dp[j, k] = s;
                    back[j, k] = (i, prev);
                }
            }
        }

        /// <summary>
        /// コード遷移のスコア。N.C. の後は曲頭と同じ扱いにし、N.C. へ入る遷移は評価しない。
        /// </summary>
        private double TransitionScore(Chord? from, Chord? to) =>
            to is { } next ? ChordTransitions.Score(_key, from, next) : 0;

        private List<PathSegment>? Backtrack(double[,] dp, (int Pos, int Chord)[,] back)
        {
            var last = _positions.Length - 1;
            var bestChord = 0;
            for (var k = 1; k < _chords.Count; k++)
            {
                if (dp[last, k] > dp[last, bestChord]) bestChord = k;
            }
            if (double.IsNegativeInfinity(dp[last, bestChord])) return null;

            var path = new List<PathSegment>();
            var (j, chord) = (last, bestChord);
            while (j > 0)
            {
                var (i, prevChord) = back[j, chord];
                var start = i < 0 ? _positions[0] : _positions[i];
                var isOverride = _overrides.Any(o => o.Start == start && o.End == _positions[j]);
                path.Add(new PathSegment(start, _positions[j], _chords[chord], isOverride));
                if (i < 0) break;
                (j, chord) = (i, prevChord);
            }

            path.Reverse();
            return path;
        }

        /// <summary>
        /// 最小長未満の区間を許すか。オーバーライドに接していて半端が出る場合と、曲全体が最小長未満の場合のみ。
        /// </summary>
        private bool AllowsShortSegment(int i, int j) =>
            (i == 0 && j == _positions.Length - 1)
            || _overrides.Any(o => o.End == _positions[i] || o.Start == _positions[j]);

        private bool IsOverrideEdge(int tick) => _overrides.Any(o => o.Start == tick || o.End == tick);

        /// <summary>
        /// 1区間に含まれるノーツの重みと、借用 V の判断材料をまとめて保持する。
        /// </summary>
        private readonly struct SegmentEvidence
        {
            private readonly Problem _problem;
            private readonly List<(int PitchClass, double Weight, bool IsBass, int Window)> _notes = [];
            private readonly double _totalWeight;
            private readonly double _beats;
            private readonly double _dominantEvidence;

            public SegmentEvidence(Problem problem, int start, int end)
            {
                _problem = problem;
                _beats = (double)(end - start) / problem._ppq;

                // ルートの有無を判定する局所窓（半小節）。ベース音は発音位置（区間頭より前なら区間頭）で窓に割り当てる

                foreach (var note in problem._bass)
                {
                    var w = Weight(note, start, end, problem._ppq);
                    if (w > 0) _notes.Add((note.PitchClass, w, true, HalfBarIndex(problem._conductor, Math.Max(note.Tick, start))));
                }

                foreach (var note in problem._melody)
                {
                    var w = Weight(note, start, end, problem._ppq) * problem._settings.MelodyWeight;
                    if (w > 0) _notes.Add((note.PitchClass, w, false, -1));
                }

                _totalWeight = _notes.Sum(n => n.Weight);

                var key = problem._key;
                var leadingTone = (key.Tonic + 11) % 12;
                var subtonic = (key.Tonic + 10) % 12;
                if (_notes.Any(n => n.PitchClass == leadingTone)) _dominantEvidence += LeadingToneBonus;
                if (_notes.Any(n => n.PitchClass == subtonic)) _dominantEvidence += SubtonicPenalty;
            }

            /// <summary>
            /// 区間に候補コードを当てたときのスコア。1拍あたりの当てはまり度 × 拍数。
            /// </summary>
            public double Score(ChordCandidate candidate)
            {
                var tones = candidate.Chord.GetPitchClasses();

                // 同じ半小節のベースにルートがない3度・5度の音は、そのコードの根拠としてあまり認めない。
                // 「D を繰り返す」ような箇所を、前後にルートがある長い区間の5度として吸収させないため
                var root = tones[0];
                var windowsWithRoot = _notes.Where(n => n.IsBass && n.PitchClass == root).Select(n => n.Window).ToHashSet();

                var fit = 0.0;
                foreach (var (pc, w, isBass, window) in _notes)
                {
                    if (isBass)
                    {
                        var inversionFactor = windowsWithRoot.Contains(window) ? 1.0 : RootlessInversionFactor;
                        if (pc == root) fit += BassRootScore * w;
                        else if (pc == tones[1]) fit += BassThirdScore * inversionFactor * w;
                        else if (pc == tones[2]) fit += BassFifthScore * inversionFactor * w;
                        else fit += BassNonChordScore * w;
                    }
                    else
                    {
                        fit += (tones.Contains(pc) ? MelodyChordToneScore : MelodyNonChordScore) * w;
                    }
                }

                var perBeat = (_totalWeight > 0 ? fit / _totalWeight * EmissionScale : 0) + candidate.Prior;
                if (candidate.IsBorrowedDominant && _problem._settings.MinorDominant == MinorDominantMode.Auto)
                    perBeat += _dominantEvidence;

                return perBeat * _beats;
            }
        }
    }

    /// <summary>
    /// 半小節単位の通し番号。ルートの有無を見る局所窓に使う。
    /// </summary>
    private static int HalfBarIndex(Conductor conductor, int tick)
    {
        var half = Math.Max(1, conductor.TicksPerBarAt(tick) / 2);
        return conductor.BarAt(tick) * 2 + Math.Min(1, conductor.PositionInBar(tick) / half);
    }

    /// <summary>
    /// ノートの重み。区間内で鳴っている拍数に、区間頭・拍頭のアクセントを掛ける。
    /// </summary>
    private static double Weight(Note note, int start, int end, int ppq)
    {
        var overlap = Math.Min(note.EndTick, end) - Math.Max(note.Tick, start);
        if (overlap <= 0) return 0;

        var w = (double)overlap / ppq;
        if (note.Tick == start) w *= SegmentHeadAccent;
        else if (note.Tick > start && note.Tick % ppq == 0) w *= OnBeatAccent;
        return w;
    }

    #endregion

    #region 結果の組み立て

    /// <summary>
    /// 同じコードが続く区間を連結し、ベースの休み（1小節以上）の境目で区切り直し、区間頭のベース音から転回形を付ける。
    /// </summary>
    private static IReadOnlyList<ChordSegment> BuildResult(List<PathSegment> path, IReadOnlyList<Note> bass,
        Conductor conductor)
    {
        var merged = new List<PathSegment>();
        foreach (var seg in path)
        {
            if (merged.Count > 0 && !seg.IsOverride && !merged[^1].IsOverride && merged[^1].Chord == seg.Chord)
                merged[^1] = merged[^1] with { End = seg.End };
            else
                merged.Add(seg);
        }

        merged = SplitAtRests(merged, bass, conductor);

        return merged
            .Select(s => new ChordSegment(s.Start, s.End - s.Start,
                s.IsOverride || s.Chord is not { } chord ? s.Chord : WithInversion(chord, bass, s.Start, s.End),
                s.IsOverride))
            .ToList();
    }

    /// <summary>
    /// ベースが1小節以上休んでいる区間の両端（休み始めは次の拍頭に寄せる）で、判別した区間を分ける。
    /// 休みの区間だけを N.C. などで指定し直せるようにするため。override の区間は分けない。
    /// </summary>
    private static List<PathSegment> SplitAtRests(List<PathSegment> segments, IReadOnlyList<Note> bass,
        Conductor conductor)
    {
        if (segments.Count == 0) return segments;

        var cuts = new SortedSet<int>();
        var cursor = 0;
        foreach (var note in bass.OrderBy(n => n.Tick))
        {
            if (note.Tick > cursor) AddRest(cursor, note.Tick);
            cursor = Math.Max(cursor, note.EndTick);
        }
        AddRest(cursor, segments[^1].End);

        var result = new List<PathSegment>();
        foreach (var seg in segments)
        {
            var start = seg.Start;
            if (!seg.IsOverride)
            {
                foreach (var cut in cuts.GetViewBetween(seg.Start + 1, seg.End - 1))
                {
                    result.Add(seg with { Start = start, End = cut });
                    start = cut;
                }
            }
            result.Add(seg with { Start = start });
        }
        return result;

        void AddRest(int from, int to)
        {
            var ppq = conductor.Ppq;
            var start = (from + ppq - 1) / ppq * ppq;
            if (to - start < conductor.TicksPerBarAt(start)) return;
            cuts.Add(start);
            cuts.Add(to);
        }
    }

    /// <summary>
    /// 区間頭で鳴っているベース音が根音以外の構成音なら、転回形として扱う。
    /// </summary>
    private static Chord WithInversion(Chord chord, IReadOnlyList<Note> bass, int start, int end)
    {
        var head = bass
            .Where(n => n.EndTick > start && n.Tick < end)
            .OrderBy(n => Math.Max(n.Tick, start))
            .ThenBy(n => n.Pitch)
            .Select(n => (Note?)n)
            .FirstOrDefault();

        if (head is not { } note) return chord;

        var pc = note.PitchClass;
        return pc != chord.Root && chord.GetPitchClasses().Contains(pc) ? chord with { Bass = pc } : chord;
    }

    #endregion
}
