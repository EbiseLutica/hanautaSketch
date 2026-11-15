#:project ../../HanautaSketch.Engine/HanautaSketch.Engine.csproj

// アプリに同梱するコード遷移の表（McGill Billboard だけから集計、CC0）を作り直す。
//
//   dotnet run tools/ChordStats/ChordStats.cs
//
// 集計の中身はエンジン（HanautaSketch.Engine/Smart/Chords/Corpus）にあり、
// Chordonomicon（CC BY-NC 4.0）の取得・集計はアプリが利用者の同意を得てから行う。
// 元データは tools/ChordStats/.cache に取得する（git 管理外）。

using HanautaSketch.Engine.Smart.Chords.Corpus;

var toolDir = Path.GetDirectoryName(Path.GetFullPath(
    AppContext.GetData("EntryPointFilePath") as string ?? "tools/ChordStats/ChordStats.cs"))!;
var cacheDir = Path.Combine(toolDir, ".cache");
var archivePath = Path.Combine(cacheDir, "billboard.tar.gz");
var outputPath = Path.GetFullPath(Path.Combine(toolDir,
    "../../HanautaSketch.Engine/Smart/Chords/Data/chord-transitions.json"));

Directory.CreateDirectory(cacheDir);
if (!File.Exists(archivePath))
{
    Console.WriteLine($"取得中: {ChordCorpusImporter.BillboardUrl}");
    using var http = new HttpClient();
    await using var source = await http.GetStreamAsync(ChordCorpusImporter.BillboardUrl);
    await using var file = File.Create(archivePath);
    await source.CopyToAsync(file);
}

var unknown = new Dictionary<string, int>();
ChordTransitionCounts counts;
await using (var archive = File.OpenRead(archivePath))
    counts = ChordCorpusImporter.ImportBillboard(archive, unknown);

Console.WriteLine(string.Join(Environment.NewLine, counts.Sources));
Console.WriteLine("対応していない種類（多い順）:");
foreach (var (suffix, count) in unknown.OrderByDescending(p => p.Value).Take(15))
    Console.WriteLine($"  {suffix}: {count}");

await using (var output = File.Create(outputPath))
    counts.Write(output);
Console.WriteLine($"書き出し: {outputPath}");
