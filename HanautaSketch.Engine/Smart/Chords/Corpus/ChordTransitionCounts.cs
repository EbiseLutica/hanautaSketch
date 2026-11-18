using System.Text.Json;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords.Corpus;

/// <summary>集計元のデータセット（出典表示用）</summary>
public sealed record ChordCorpusSource(string Name, string License, int Songs);

/// <summary>
/// コード進行から数えた「前のコード → 次のコード」の回数。長調・短調で別に持つ。
/// ラベルは度数（主音からの半音数）と和音の種類の組（<see cref="Label"/>）。
/// </summary>
public sealed class ChordTransitionCounts
{
    /// <summary>書き出す遷移: 1つの前のコードにつき、回数の多い順にこの数まで</summary>
    private const int MaxTransitionsPerChord = 40;

    /// <summary>書き出す遷移・前のコードの最低回数</summary>
    private const int MinTransitionCount = 3;

    private readonly Dictionary<Mode, ModeCounts> _modes = new() { [Mode.Major] = new(), [Mode.Minor] = new() };

    public List<ChordCorpusSource> Sources { get; } = [];

    public static string Label(int degree, ChordQuality quality) => $"{degree}:{quality}";

    public static string LabelOf(Key key, Chord chord) => Label((chord.Root - key.Tonic + 12) % 12, chord.Quality);

    internal ModeCounts this[Mode mode] => _modes[mode];

    /// <summary>
    /// 1曲（または1つのキーの区間）のコード列を数える。同じコードの連続は1つにまとめる。分数コードのベースは見ない。
    /// </summary>
    public void Add(Key key, IReadOnlyList<Chord> chords)
    {
        var mode = _modes[key.Mode];
        string? previous = null;
        foreach (var chord in chords)
        {
            var label = LabelOf(key, chord);
            if (label == previous) continue;

            mode.Unigram[label] = mode.Unigram.GetValueOrDefault(label) + 1;
            if (previous is not null)
            {
                if (!mode.Bigram.TryGetValue(previous, out var row)) mode.Bigram[previous] = row = [];
                row[label] = row.GetValueOrDefault(label) + 1;
            }
            previous = label;
        }
    }

    /// <summary>別の集計を足し合わせる（出典も引き継ぐ）</summary>
    public void Merge(ChordTransitionCounts other)
    {
        foreach (var (mode, counts) in other._modes)
        {
            var target = _modes[mode];
            foreach (var (label, count) in counts.Unigram)
                target.Unigram[label] = target.Unigram.GetValueOrDefault(label) + count;
            foreach (var (from, row) in counts.Bigram)
            {
                if (!target.Bigram.TryGetValue(from, out var targetRow)) target.Bigram[from] = targetRow = [];
                foreach (var (to, count) in row)
                    targetRow[to] = targetRow.GetValueOrDefault(to) + count;
            }
        }
        Sources.AddRange(other.Sources);
    }

    #region JSON

    /// <summary>
    /// JSON に書き出す。回数の少ない遷移は捨て、1つの前のコードにつき上位だけを残す。
    /// </summary>
    public void Write(Stream stream)
    {
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();

        writer.WriteStartArray("sources");
        foreach (var source in Sources)
        {
            writer.WriteStartObject();
            writer.WriteString("name", source.Name);
            writer.WriteString("license", source.License);
            writer.WriteNumber("songs", source.Songs);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        WriteMode(writer, "major", _modes[Mode.Major]);
        WriteMode(writer, "minor", _modes[Mode.Minor]);
        writer.WriteEndObject();
    }

    private static void WriteMode(Utf8JsonWriter writer, string name, ModeCounts mode)
    {
        writer.WriteStartObject(name);

        writer.WriteStartObject("unigram");
        foreach (var (label, count) in mode.Unigram.OrderByDescending(p => p.Value))
            writer.WriteNumber(label, count);
        writer.WriteEndObject();

        writer.WriteStartObject("bigram");
        foreach (var (from, row) in mode.Bigram.Where(p => mode.Unigram.GetValueOrDefault(p.Key) >= MinTransitionCount))
        {
            writer.WriteStartObject(from);
            foreach (var (to, count) in row.Where(t => t.Value >= MinTransitionCount)
                         .OrderByDescending(t => t.Value).Take(MaxTransitionsPerChord))
                writer.WriteNumber(to, count);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    public static ChordTransitionCounts Read(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var counts = new ChordTransitionCounts();
        foreach (var source in root.GetProperty("sources").EnumerateArray())
        {
            counts.Sources.Add(new ChordCorpusSource(source.GetProperty("name").GetString() ?? "",
                source.GetProperty("license").GetString() ?? "", source.GetProperty("songs").GetInt32()));
        }
        ReadMode(root.GetProperty("major"), counts._modes[Mode.Major]);
        ReadMode(root.GetProperty("minor"), counts._modes[Mode.Minor]);
        return counts;
    }

    private static void ReadMode(JsonElement element, ModeCounts mode)
    {
        foreach (var p in element.GetProperty("unigram").EnumerateObject())
            mode.Unigram[p.Name] = p.Value.GetInt64();
        foreach (var p in element.GetProperty("bigram").EnumerateObject())
            mode.Bigram[p.Name] = p.Value.EnumerateObject().ToDictionary(t => t.Name, t => t.Value.GetInt64());
    }

    #endregion

    internal sealed class ModeCounts
    {
        public Dictionary<string, long> Unigram { get; } = [];
        public Dictionary<string, Dictionary<string, long>> Bigram { get; } = [];
    }
}
