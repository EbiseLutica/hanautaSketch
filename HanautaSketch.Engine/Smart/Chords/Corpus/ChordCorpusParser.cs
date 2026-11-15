using System.Text.RegularExpressions;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords.Corpus;

/// <summary>
/// コード進行のデータセットの表記を読む。
/// - McGill Billboard: Harte 表記（A:min7, C:maj/3）。主音はヘッダーの「# tonic:」
/// - Chordonomicon: Csmin7, Bb/D のような表記（# は s）。キーの注記はない
/// 対応しない種類は読み飛ばす（<paramref name="unknown"/> に数える）。
/// </summary>
public static partial class ChordCorpusParser
{
    /// <summary>
    /// 「| A:min | A:min C:maj |」の小節線の間にあるコード（根音と種類。分数コードのベースは捨てる）。
    /// </summary>
    [GeneratedRegex(@"(?<=[\s|])([A-G][#b]?):([^\s|,/]+)(?:/[^\s|,]+)?")]
    private static partial Regex HarteChord();

    /// <summary>「Csmin7」「Bb/D」。sus の s を # と読まないよう、s の後に us が続くときは # としない</summary>
    [GeneratedRegex(@"^([A-G])(s(?!us)|b)?([^/]*)")]
    private static partial Regex ChordonomiconChord();

    #region Billboard

    /// <summary>
    /// Billboard の salami_chords.txt 1曲分を読む。主音がない・コードが2つ未満なら null。
    /// 主音の注記には長調・短調の区別がないので、主音上の和音が短三和音系の方が多ければ短調とみなす。
    /// </summary>
    public static (Key Key, List<Chord> Chords)? ParseBillboard(string text, IDictionary<string, int>? unknown = null)
    {
        int? tonic = null;
        var chords = new List<Chord>();
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("# tonic:"))
            {
                tonic = ParseRoot(line["# tonic:".Length..].Trim());
                continue;
            }

            foreach (Match m in HarteChord().Matches(line))
            {
                if (ParseRoot(m.Groups[1].Value) is { } root && MapHarte(m.Groups[2].Value, unknown) is { } quality)
                    chords.Add(new Chord(root, quality));
            }
        }

        if (tonic is not { } t || chords.Count < 2) return null;

        var onTonic = chords.Where(c => c.Root == t).ToList();
        var minor = onTonic.Count(c => IsMinorish(c.Quality)) > onTonic.Count(c => !IsMinorish(c.Quality));
        return (new Key(t, minor ? Mode.Minor : Mode.Major), chords);
    }

    private static ChordQuality? MapHarte(string quality, IDictionary<string, int>? unknown)
    {
        ChordQuality? result = quality switch
        {
            "maj" or "5" or "1" => ChordQuality.Major,
            "min" => ChordQuality.Minor,
            "dim" => ChordQuality.Diminished,
            "aug" => ChordQuality.Augmented,
            "sus2" => ChordQuality.Sus2,
            "sus4" => ChordQuality.Sus4,
            "sus4(b7" or "sus4(b7)" => ChordQuality.Dominant7Sus4,
            "5(b7" or "5(b7)" => ChordQuality.Dominant7,
            "maj6" => ChordQuality.Sixth,
            "min6" => ChordQuality.MinorSixth,
            "7" => ChordQuality.Dominant7,
            "maj7" => ChordQuality.Major7,
            "min7" => ChordQuality.Minor7,
            "hdim7" => ChordQuality.HalfDiminished7,
            "dim7" => ChordQuality.Diminished7,
            "minmaj7" => ChordQuality.MinorMajor7,
            "maj(9)" or "maj(2)" => ChordQuality.Add9,
            "min(9)" => ChordQuality.MinorAdd9,
            "maj6(9)" => ChordQuality.SixNine,
            "9" => ChordQuality.Dominant9,
            "maj9" => ChordQuality.Major9,
            "min9" => ChordQuality.Minor9,
            "7(b9)" => ChordQuality.Dominant7Flat9,
            "7(#9)" => ChordQuality.Dominant7Sharp9,
            "7(#11)" => ChordQuality.Dominant7Sharp11,
            "7(b13)" => ChordQuality.Dominant7Flat13,
            "11" => ChordQuality.Dominant11,
            "min11" => ChordQuality.Minor11,
            "maj7(#11)" => ChordQuality.Major7Sharp11,
            "13" => ChordQuality.Dominant13,
            _ => null,
        };
        if (result is null && unknown is not null) unknown["harte:" + quality] = unknown.TryGetValue("harte:" + quality, out var c) ? c + 1 : 1;
        return result;
    }

    #endregion

    #region Chordonomicon

    /// <summary>
    /// Chordonomicon の CSV の1行（id,chords,...）からコード列を読む。セクション見出し（&lt;verse_1&gt;）は飛ばす。
    /// </summary>
    public static List<Chord> ParseChordonomiconLine(string line, IDictionary<string, int>? unknown = null)
    {
        var chords = new List<Chord>();

        // 2列目。コード列にカンマは含まれない
        var first = line.IndexOf(',');
        var second = first < 0 ? -1 : line.IndexOf(',', first + 1);
        if (second < 0) return chords;

        foreach (var token in line[(first + 1)..second].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token[0] == '<') continue;
            if (ParseChordonomiconChord(token, unknown) is { } chord) chords.Add(chord);
        }
        return chords;
    }

    public static Chord? ParseChordonomiconChord(string token, IDictionary<string, int>? unknown = null)
    {
        var m = ChordonomiconChord().Match(token);
        if (!m.Success) return null;

        var root = (ParseRoot(m.Groups[1].Value)!.Value + (m.Groups[2].Value switch { "s" => 1, "b" => 11, _ => 0 })) % 12;
        var suffix = m.Groups[3].Value;
        ChordQuality? quality = suffix switch
        {
            "" or "5" or "no3d" or "add11" => ChordQuality.Major,
            "min" or "minadd11" => ChordQuality.Minor,
            "dim" => ChordQuality.Diminished,
            "aug" => ChordQuality.Augmented,
            "sus2" => ChordQuality.Sus2,
            "sus4" or "sus" => ChordQuality.Sus4,
            "7sus4" or "sus47" => ChordQuality.Dominant7Sus4,
            "6" or "maj6" or "add13" => ChordQuality.Sixth,
            "min6" or "minadd13" => ChordQuality.MinorSixth,
            "7" => ChordQuality.Dominant7,
            "maj7" => ChordQuality.Major7,
            "min7" => ChordQuality.Minor7,
            "hdim7" or "min7b5" or "m7b5" => ChordQuality.HalfDiminished7,
            "dim7" => ChordQuality.Diminished7,
            "minmaj7" => ChordQuality.MinorMajor7,
            "aug7" => ChordQuality.Augmented7,
            "add9" or "add2" => ChordQuality.Add9,
            "minadd9" or "minadd2" => ChordQuality.MinorAdd9,
            "69" or "6add9" => ChordQuality.SixNine,
            "9" => ChordQuality.Dominant9,
            "maj9" or "maj13" => ChordQuality.Major9,
            "min9" or "min13" => ChordQuality.Minor9,
            "7b9" => ChordQuality.Dominant7Flat9,
            "7s9" => ChordQuality.Dominant7Sharp9,
            "7s11" => ChordQuality.Dominant7Sharp11,
            "7b13" => ChordQuality.Dominant7Flat13,
            "11" => ChordQuality.Dominant11,
            "min11" => ChordQuality.Minor11,
            "maj7s11" => ChordQuality.Major7Sharp11,
            "13" => ChordQuality.Dominant13,
            _ => null,
        };
        if (quality is null)
        {
            if (unknown is not null) unknown["chordonomicon:" + suffix] = unknown.TryGetValue("chordonomicon:" + suffix, out var c) ? c + 1 : 1;
            return null;
        }
        return new Chord(root, quality.Value);
    }

    #endregion

    #region キーの推定

    /// <summary>長調の各主音のスケール（12ビットのピッチクラス集合）</summary>
    private static readonly int[] MajorScaleMasks = Enumerable.Range(0, 12)
        .Select(t => new Key(t, Mode.Major).GetScalePitchClasses().Aggregate(0, (m, pc) => m | 1 << pc))
        .ToArray();

    /// <summary>種類ごとの、根音 C での根音・3度・5度（三和音部分）のピッチクラス集合</summary>
    private static readonly Dictionary<ChordQuality, int> TriadMasks = ChordQualities.All.ToDictionary(
        q => q.Quality, q => q.Intervals.Take(3).Aggregate(0, (m, i) => m | 1 << (i % 12)));

    /// <summary>
    /// コードの並びからキーを推定する。長調の12キーのうち、三和音部分がスケールに収まる和音が最も多いものを選び
    /// （主音上の長三和音系に少し加点）、vi が I より多く、かつ曲頭か曲末が vi なら、その平行短調とみなす。
    /// ダイアトニックな和音が6割未満の曲（転調が多いなど）は null。
    /// </summary>
    public static Key? EstimateKey(IReadOnlyList<Chord> chords)
    {
        Span<int> masks = chords.Count <= 512 ? stackalloc int[chords.Count] : new int[chords.Count];
        for (var i = 0; i < chords.Count; i++)
            masks[i] = Rotate(TriadMasks[chords[i].Quality], chords[i].Root);

        var best = -1;
        var bestScore = 0.0;
        for (var tonic = 0; tonic < 12; tonic++)
        {
            var scale = MajorScaleMasks[tonic];
            var score = 0.0;
            for (var i = 0; i < chords.Count; i++)
            {
                if ((masks[i] & ~scale) == 0) score += 1;
                if (chords[i].Root == tonic && !IsMinorish(chords[i].Quality)) score += 0.5;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = tonic;
            }
        }
        if (best < 0 || bestScore < chords.Count * 0.6) return null;

        var relative = (best + 9) % 12;
        var tonicCount = 0;
        var relativeCount = 0;
        foreach (var chord in chords)
        {
            if (chord.Root == best && !IsMinorish(chord.Quality)) tonicCount++;
            if (chord.Root == relative && IsMinorish(chord.Quality)) relativeCount++;
        }
        var endsOnRelative = chords[0].Root == relative || chords[^1].Root == relative;
        return relativeCount > tonicCount && endsOnRelative ? new Key(relative, Mode.Minor) : new Key(best, Mode.Major);
    }

    private static int Rotate(int mask, int root) => (mask << root | mask >> (12 - root)) & 0xFFF;

    #endregion

    private static bool IsMinorish(ChordQuality q) => q is ChordQuality.Minor or ChordQuality.Minor7
        or ChordQuality.MinorSixth or ChordQuality.Minor9 or ChordQuality.Minor11 or ChordQuality.MinorAdd9
        or ChordQuality.MinorMajor7;

    private static int? ParseRoot(string name)
    {
        if (name.Length == 0) return null;
        int? pc = name[0] switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => null };
        if (pc is null) return null;
        var accidental = name.Length > 1 ? name[1] switch { '#' => 1, 'b' => -1, _ => 0 } : 0;
        return (pc.Value + accidental + 12) % 12;
    }
}
