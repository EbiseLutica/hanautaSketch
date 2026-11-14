using System.Text.Json;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// 実際の曲のコード進行から集計した「前のコード → 次のコード」の出やすさ（tools/ChordStats で作る）。
/// 度数（主音からの半音数）と和音の種類の組で持ち、長調・短調で別に数えてある。
/// 判別（<see cref="ChordTransitions"/>）には使わず、override の候補を並べるのに使う。
/// </summary>
public sealed class ChordTransitionTable
{
    /// <summary>未知の遷移を、全体での出やすさで埋める強さ（擬似的な回数）</summary>
    private const double BackoffWeight = 20;

    private const string ResourceName = "HanautaSketch.Engine.Smart.Chords.Data.chord-transitions.json";

    private static readonly Lazy<ChordTransitionTable> LazyDefault = new(Load);

    private readonly Dictionary<Mode, ModeTable> _modes;

    private ChordTransitionTable(Dictionary<Mode, ModeTable> modes, IReadOnlyList<string> sources)
    {
        _modes = modes;
        Sources = sources;
    }

    /// <summary>同梱の表</summary>
    public static ChordTransitionTable Default => LazyDefault.Value;

    /// <summary>集計元のデータセット（出典表示用）</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>表のラベル。度数（0〜11）と種類の名前</summary>
    public static string Label(int degree, ChordQuality quality) => $"{degree}:{quality}";

    /// <summary>
    /// previous の次に chord が来る確率の推定値。previous が null（曲頭・N.C. の後）なら全体での出やすさ。
    /// 分数コードのベースは見ない。
    /// </summary>
    public double Probability(Key key, Chord? previous, Chord chord)
    {
        var table = _modes[key.Mode];
        var to = LabelOf(key, chord);
        var unigram = table.Unigram.GetValueOrDefault(to) / Math.Max(1.0, table.Total);
        if (previous is not { } prev) return unigram;

        var from = LabelOf(key, prev);
        var fromCount = table.Unigram.GetValueOrDefault(from);
        var count = table.Bigram.TryGetValue(from, out var row) ? row.GetValueOrDefault(to) : 0;
        return (count + BackoffWeight * unigram) / (fromCount + BackoffWeight);
    }

    private static string LabelOf(Key key, Chord chord) => Label((chord.Root - key.Tonic + 12) % 12, chord.Quality);

    /// <summary>JSON（tools/ChordStats の出力）から読む</summary>
    public static ChordTransitionTable Parse(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var sources = root.GetProperty("sources").EnumerateArray()
            .Select(s => $"{s.GetProperty("name").GetString()}（{s.GetProperty("license").GetString()}）")
            .ToList();
        var modes = new Dictionary<Mode, ModeTable>
        {
            [Mode.Major] = ReadMode(root.GetProperty("major")),
            [Mode.Minor] = ReadMode(root.GetProperty("minor")),
        };
        return new ChordTransitionTable(modes, sources);
    }

    private static ChordTransitionTable Load()
    {
        using var stream = typeof(ChordTransitionTable).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"埋め込みリソース {ResourceName} がない");
        return Parse(stream);
    }

    private static ModeTable ReadMode(JsonElement element)
    {
        var unigram = element.GetProperty("unigram").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64());
        var bigram = element.GetProperty("bigram").EnumerateObject().ToDictionary(
            p => p.Name, p => p.Value.EnumerateObject().ToDictionary(t => t.Name, t => t.Value.GetInt64()));
        return new ModeTable(unigram, bigram, unigram.Values.Sum());
    }

    private sealed record ModeTable(
        Dictionary<string, long> Unigram, Dictionary<string, Dictionary<string, long>> Bigram, long Total);
}
