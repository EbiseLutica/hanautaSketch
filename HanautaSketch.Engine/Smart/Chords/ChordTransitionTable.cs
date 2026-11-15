using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords.Corpus;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// 実際の曲のコード進行から集計した「前のコード → 次のコード」の出やすさ。
/// 同梱の表は McGill Billboard（CC0）だけから作ったもの（tools/ChordStats）。
/// Chordonomicon（CC BY-NC 4.0）を足した表は、利用者が同意したときにアプリが作る。
/// 判別（<see cref="ChordTransitions"/>）には使わず、override の候補を並べるのに使う。
/// </summary>
public sealed class ChordTransitionTable
{
    /// <summary>未知の遷移を、全体での出やすさで埋める強さ（擬似的な回数）</summary>
    private const double BackoffWeight = 20;

    private const string ResourceName = "HanautaSketch.Engine.Smart.Chords.Data.chord-transitions.json";

    private static readonly Lazy<ChordTransitionTable> LazyBundled = new(LoadBundled);

    private readonly ChordTransitionCounts _counts;
    private readonly Dictionary<Mode, long> _totals;

    public ChordTransitionTable(ChordTransitionCounts counts)
    {
        _counts = counts;
        _totals = new Dictionary<Mode, long>
        {
            [Mode.Major] = counts[Mode.Major].Unigram.Values.Sum(),
            [Mode.Minor] = counts[Mode.Minor].Unigram.Values.Sum(),
        };
    }

    /// <summary>同梱の表（Billboard のみ）</summary>
    public static ChordTransitionTable Default => LazyBundled.Value;

    /// <summary>同梱の表の元の回数（追加の集計と足し合わせるのに使う）</summary>
    public static ChordTransitionCounts LoadBundledCounts()
    {
        using var stream = typeof(ChordTransitionTable).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"埋め込みリソース {ResourceName} がない");
        return ChordTransitionCounts.Read(stream);
    }

    /// <summary>集計元のデータセット（出典表示用）</summary>
    public IReadOnlyList<ChordCorpusSource> Sources => _counts.Sources;

    /// <summary>
    /// previous の次に chord が来る確率の推定値。previous が null（曲頭・N.C. の後）なら全体での出やすさ。
    /// 分数コードのベースは見ない。
    /// </summary>
    public double Probability(Key key, Chord? previous, Chord chord)
    {
        var table = _counts[key.Mode];
        var to = ChordTransitionCounts.LabelOf(key, chord);
        var unigram = table.Unigram.GetValueOrDefault(to) / Math.Max(1.0, _totals[key.Mode]);
        if (previous is not { } prev) return unigram;

        var from = ChordTransitionCounts.LabelOf(key, prev);
        var fromCount = table.Unigram.GetValueOrDefault(from);
        var count = table.Bigram.TryGetValue(from, out var row) ? row.GetValueOrDefault(to) : 0;
        return (count + BackoffWeight * unigram) / (fromCount + BackoffWeight);
    }

    /// <summary>JSON（<see cref="ChordTransitionCounts.Write"/> の出力）から読む</summary>
    public static ChordTransitionTable Parse(Stream json) => new(ChordTransitionCounts.Read(json));

    private static ChordTransitionTable LoadBundled() => new(LoadBundledCounts());
}
