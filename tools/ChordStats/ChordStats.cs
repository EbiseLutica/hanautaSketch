#:project ../../HanautaSketch.Engine/HanautaSketch.Engine.csproj
#:property PublishAot=false

// コード進行のデータセットから「前のコード → 次のコード」の出やすさを集計し、
// スマートコードの候補ランキング用の表（JSON）を書き出す。
//
//   dotnet run tools/ChordStats/ChordStats.cs
//
// 元データは tools/ChordStats/.cache に取得する（git 管理外）。
// - McGill Billboard（CC0）: 主音の注記つき。約890曲
// - Chordonomicon（CC BY-NC 4.0）: 約66万曲。キーは注記がないので、構成から推定する
// 度数（主音からの半音数）と和音の種類の組で数えるので、どのキーにも使い回せる。

using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

const string BillboardUrl = "https://www.dropbox.com/s/2lvny9ves8kns4o/billboard-2.0-salami_chords.tar.gz?dl=1";
const string ChordonomiconUrl = "https://huggingface.co/datasets/ailsntua/Chordonomicon/resolve/main/chordonomicon_v2.csv";

// 表に残す遷移: 1つの前のコードにつき、回数の多い順にこの数まで。回数がこれ未満のものは捨てる
const int MaxTransitionsPerChord = 40;
const int MinTransitionCount = 3;

var toolDir = Path.GetDirectoryName(Path.GetFullPath(AppContext.GetData("EntryPointFilePath") as string ?? "tools/ChordStats/ChordStats.cs"))!;
var cacheDir = Path.Combine(toolDir, ".cache");
var outputPath = Path.GetFullPath(Path.Combine(toolDir, "../../HanautaSketch.Engine/Smart/Chords/Data/chord-transitions.json"));
Directory.CreateDirectory(cacheDir);

var stats = new Dictionary<Mode, ModeStats> { [Mode.Major] = new(), [Mode.Minor] = new() };
var unknownSuffixes = new Dictionary<string, int>();

var billboard = await Download(BillboardUrl, Path.Combine(cacheDir, "billboard.tar.gz"));
var billboardSongs = ReadBillboard(billboard);
Console.WriteLine($"Billboard: {billboardSongs} 曲");

var chordonomicon = await Download(ChordonomiconUrl, Path.Combine(cacheDir, "chordonomicon_v2.csv"));
var chordonomiconSongs = ReadChordonomicon(chordonomicon);
Console.WriteLine($"Chordonomicon: {chordonomiconSongs} 曲");

Console.WriteLine("対応していない種類（多い順）:");
foreach (var (suffix, count) in unknownSuffixes.OrderByDescending(p => p.Value).Take(25))
    Console.WriteLine($"  {suffix}: {count}");

WriteTable();
Console.WriteLine($"書き出し: {outputPath}");
return;

#region 取得

async Task<string> Download(string url, string path)
{
    if (File.Exists(path)) return path;

    Console.WriteLine($"取得中: {url}");
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    await using var source = await http.GetStreamAsync(url);
    var temp = path + ".part";
    await using (var file = File.Create(temp))
        await source.CopyToAsync(file);
    File.Move(temp, path, true);
    return path;
}

#endregion

#region Billboard

int ReadBillboard(string archive)
{
    using var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
    using var tar = new TarReader(gzip);
    var songs = 0;
    while (tar.GetNextEntry() is { } entry)
    {
        if (entry.DataStream is null || !entry.Name.EndsWith("salami_chords.txt")) continue;
        using var reader = new StreamReader(entry.DataStream);
        if (ReadBillboardSong(reader.ReadToEnd())) songs++;
    }
    return songs;
}

bool ReadBillboardSong(string text)
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

        // 「| A:min | A:min C:maj |」の小節線の間にあるコード
        foreach (Match m in Regex.Matches(line, @"(?<=[\s|])([A-G][#b]?):([^\s|,/]+)(?:/[^\s|,]+)?"))
        {
            if (ParseRoot(m.Groups[1].Value) is { } root && MapHarte(m.Groups[2].Value) is { } quality)
                chords.Add(new Chord(root, quality));
        }
    }

    if (tonic is not { } t || chords.Count < 2) return false;

    // 主音の注記には長調・短調の区別がないので、主音上の和音が短三和音系の方が多ければ短調とみなす
    var onTonic = chords.Where(c => c.Root == t).ToList();
    var minor = onTonic.Count(c => IsMinorish(c.Quality)) > onTonic.Count(c => !IsMinorish(c.Quality));
    Count(new Key(t, minor ? Mode.Minor : Mode.Major), chords);
    return true;
}

/// <summary>Harte 表記の種類（min7, maj(9), 7(b9) など）を対応する種類にする。対応しないものは null</summary>
ChordQuality? MapHarte(string quality)
{
    var result = quality switch
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
        _ => (ChordQuality?)null,
    };
    if (result is null) Bump("harte:" + quality);
    return result;
}

#endregion

#region Chordonomicon

int ReadChordonomicon(string csv)
{
    using var reader = new StreamReader(csv);
    reader.ReadLine(); // ヘッダー
    var songs = 0;
    while (reader.ReadLine() is { } line)
    {
        // id,chords,... の2列目。コード列にカンマは含まれない
        var first = line.IndexOf(',');
        var second = first < 0 ? -1 : line.IndexOf(',', first + 1);
        if (second < 0) continue;

        var chords = new List<Chord>();
        foreach (var token in line[(first + 1)..second].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith('<')) continue; // <verse_1> などのセクション
            if (ParseChordonomicon(token) is { } chord) chords.Add(chord);
        }

        if (chords.Count < 2 || EstimateKey(chords) is not { } key) continue;
        Count(key, chords);
        songs++;
        if (songs % 100_000 == 0) Console.WriteLine($"  {songs} 曲…");
    }
    return songs;
}

/// <summary>「Csmin7」「Bb/D」のような表記を読む。# は s で書かれている。分数コードのベースは捨てる</summary>
Chord? ParseChordonomicon(string token)
{
    // 「sus」の s を # と読まないよう、s の後に us が続くときは # としない
    var m = Regex.Match(token, @"^([A-G])(s(?!us)|b)?([^/]*)");
    if (!m.Success) return null;

    var root = (ParseRoot(m.Groups[1].Value)!.Value + (m.Groups[2].Value switch { "s" => 1, "b" => 11, _ => 0 })) % 12;
    var quality = m.Groups[3].Value switch
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
        var other => Unknown(other),
    };
    return quality is { } q ? new Chord(root, q) : null;

    ChordQuality? Unknown(string suffix)
    {
        Bump("chordonomicon:" + suffix);
        return null;
    }
}

/// <summary>
/// コードの並びからキーを推定する。長調の12キーのうちダイアトニックな和音が最も多いものを選び、
/// vi が I より多く、かつ曲頭か曲末が vi なら、その平行短調とみなす。
/// </summary>
Key? EstimateKey(List<Chord> chords)
{
    var best = -1;
    var bestScore = 0.0;
    for (var tonic = 0; tonic < 12; tonic++)
    {
        var key = new Key(tonic, Mode.Major);
        var score = chords.Count(c => IsDiatonic(key, c)) + 0.5 * chords.Count(c => c.Root == tonic && !IsMinorish(c.Quality));
        if (score > bestScore)
        {
            bestScore = score;
            best = tonic;
        }
    }
    if (best < 0 || bestScore < chords.Count * 0.6) return null; // 転調が多いなど、キーがはっきりしない曲は使わない

    var relative = (best + 9) % 12;
    var tonicCount = chords.Count(c => c.Root == best && !IsMinorish(c.Quality));
    var relativeCount = chords.Count(c => c.Root == relative && IsMinorish(c.Quality));
    var endsOnRelative = chords[0].Root == relative || chords[^1].Root == relative;
    return relativeCount > tonicCount && endsOnRelative ? new Key(relative, Mode.Minor) : new Key(best, Mode.Major);
}

bool IsDiatonic(Key key, Chord chord)
{
    var scale = key.GetScalePitchClasses();
    var tones = chord.GetPitchClasses();
    return tones.Take(3).All(scale.Contains);
}

#endregion

#region 集計と書き出し

static bool IsMinorish(ChordQuality q) => q is ChordQuality.Minor or ChordQuality.Minor7 or ChordQuality.MinorSixth
    or ChordQuality.Minor9 or ChordQuality.Minor11 or ChordQuality.MinorAdd9 or ChordQuality.MinorMajor7;

static int? ParseRoot(string name)
{
    if (name.Length == 0) return null;
    int? pc = name[0] switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => null };
    if (pc is null) return null;
    var accidental = name.Length > 1 ? name[1] switch { '#' => 1, 'b' => -1, _ => 0 } : 0;
    return (pc.Value + accidental + 12) % 12;
}

void Bump(string key) => unknownSuffixes[key] = unknownSuffixes.GetValueOrDefault(key) + 1;

/// <summary>同じコードの連続はまとめ、度数＋種類のラベルで「前 → 次」を数える</summary>
void Count(Key key, List<Chord> chords)
{
    var mode = stats[key.Mode];
    string? previous = null;
    foreach (var chord in chords)
    {
        var label = ChordTransitionTable.Label((chord.Root - key.Tonic + 12) % 12, chord.Quality);
        if (label == previous) continue;

        mode.Unigram[label] = mode.Unigram.GetValueOrDefault(label) + 1;
        if (previous is not null)
        {
            var row = mode.Bigram.TryGetValue(previous, out var r) ? r : mode.Bigram[previous] = [];
            row[label] = row.GetValueOrDefault(label) + 1;
        }
        previous = label;
    }
}

void WriteTable()
{
    var table = new
    {
        sources = new[]
        {
            new { name = "McGill Billboard Project", license = "CC0", songs = billboardSongs },
            new { name = "Chordonomicon (Kantarelis et al., 2024)", license = "CC BY-NC 4.0", songs = chordonomiconSongs },
        },
        major = Serialize(stats[Mode.Major]),
        minor = Serialize(stats[Mode.Minor]),
    };

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    File.WriteAllText(outputPath, JsonSerializer.Serialize(table, options));
}

static object Serialize(ModeStats mode) => new
{
    unigram = mode.Unigram.OrderByDescending(p => p.Value).ToDictionary(),
    bigram = mode.Bigram
        .Where(p => mode.Unigram.GetValueOrDefault(p.Key) >= MinTransitionCount)
        .ToDictionary(p => p.Key, p => p.Value
            .Where(t => t.Value >= MinTransitionCount)
            .OrderByDescending(t => t.Value)
            .Take(MaxTransitionsPerChord)
            .ToDictionary()),
};

sealed class ModeStats
{
    public Dictionary<string, long> Unigram { get; } = [];
    public Dictionary<string, Dictionary<string, long>> Bigram { get; } = [];
}

#endregion
