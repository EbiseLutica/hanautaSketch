// 配布物に含むライブラリ（推移的な依存を含む）と .NET SDK のライセンス本文を集め、
// アプリに埋め込む HanautaSketch/Licenses/third-party.txt を作る。
//
//   dotnet run tools/LicenseCollector/CollectLicenses.cs
//
// 本文は NuGet キャッシュ（~/.nuget/packages）のライセンス・通知ファイルをそのまま使う。
// ライセンスファイルがないパッケージは nuspec のライセンス式から templates/ の本文を組み立てる（MIT と LGPL-2.0 のみ）。
// 知らないライセンスは作らずに止める。出力形式は HanautaSketch/Services/OpenSourceLicenses.cs の読み込みと対応している。

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var toolDir = Path.GetDirectoryName(Path.GetFullPath(
    AppContext.GetData("EntryPointFilePath") as string ?? "tools/LicenseCollector/CollectLicenses.cs"))!;
var repoDir = Path.GetFullPath(Path.Combine(toolDir, "../.."));
var projectPath = Path.Combine(repoDir, "HanautaSketch", "HanautaSketch.csproj");
var outputPath = Path.Combine(repoDir, "HanautaSketch", "Licenses", "third-party.txt");
var templateDir = Path.Combine(toolDir, "templates");
var nugetRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

var mitBody = ReadText(Path.Combine(templateDir, "MIT.txt"));
var lgplBody = ReadText(Path.Combine(templateDir, "LGPL-2.0.txt"));
var licenseFileName = new Regex(@"^(LICEN[CS]E|COPYING|NOTICE|THIRD[-_ ]?PARTY|ThirdPartyNotices)", RegexOptions.IgnoreCase);
var licenseOnlyFileName = new Regex(@"^(LICEN[CS]E|COPYING)", RegexOptions.IgnoreCase);

var entries = new List<Entry>();

// .NET SDK（ランタイムを含む）。NativeAOT の出力にはランタイムが静的に入るので、その通知も含める
var dotnetRoot = FindDotnetRoot();
var sdkVersion = Run("dotnet", "--version").Trim();
entries.Add(new Entry("Microsoft .NET (SDK / runtime)", sdkVersion, "Microsoft Software License Terms (+ third-party notices)", [
    new Text("LICENSE.txt", ReadText(Path.Combine(dotnetRoot, "LICENSE.txt"))),
    new Text("ThirdPartyNotices.txt", ReadText(Path.Combine(dotnetRoot, "ThirdPartyNotices.txt"))),
]));

// 配布物に入るパッケージ（テスト専用は含まない）
var listJson = Run("dotnet", $"list \"{projectPath}\" package --include-transitive --format json");
using var listDoc = JsonDocument.Parse(listJson[listJson.IndexOf('{')..]);
var resolved = new HashSet<(string Id, string Version)>();
foreach (var project in listDoc.RootElement.GetProperty("projects").EnumerateArray())
    foreach (var framework in project.GetProperty("frameworks").EnumerateArray())
        foreach (var key in new[] { "topLevelPackages", "transitivePackages" })
            if (framework.TryGetProperty(key, out var packages))
                foreach (var package in packages.EnumerateArray())
                    resolved.Add((package.GetProperty("id").GetString()!, package.GetProperty("resolvedVersion").GetString()!));

foreach (var (id, version) in resolved.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
{
    var dir = Path.Combine(nugetRoot, id.ToLowerInvariant(), version);
    var nuspecPath = Path.Combine(dir, id.ToLowerInvariant() + ".nuspec");
    if (!File.Exists(nuspecPath)) throw new InvalidOperationException($"{id} {version}: NuGet キャッシュがない（dotnet restore を確認）");
    var nuspec = XDocument.Load(nuspecPath);

    var licenseElement = nuspec.Descendants().FirstOrDefault(e => e.Name.LocalName == "license");
    var licenseUrl = Meta(nuspec, "licenseUrl") ?? "";
    var expression = licenseElement?.Attribute("type")?.Value == "expression"
        ? licenseElement.Value.Trim()
        : licenseUrl.StartsWith("https://licenses.nuget.org/", StringComparison.Ordinal) ? licenseUrl.Split('/').Last() : null;

    var licenseFiles = Directory.EnumerateFiles(dir)
        .Where(f => licenseFileName.IsMatch(Path.GetFileName(f)))
        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
        .ToList();
    var texts = licenseFiles.Select(f => new Text(Path.GetFileName(f), ReadText(f))).ToList();

    var hasLicenseFile = licenseFiles.Any(f => licenseOnlyFileName.IsMatch(Path.GetFileName(f)));
    if (!hasLicenseFile)
    {
        var generated = expression switch
        {
            "MIT" => new Text("MIT License", $"{CopyrightLine(nuspec)}\n\n{mitBody}"),
            "LGPL-2.0-or-later" or "LGPL-2.0-only" or "LGPL-2.0" => new Text("LGPL-2.0", lgplBody),
            _ => throw new InvalidOperationException($"{id} {version}: ライセンス本文がなく、{expression ?? "(不明)"} は組み立てられない"),
        };
        texts.Insert(0, generated);
    }

    entries.Add(new Entry(id, version, expression ?? "license file", texts));
}

// 本文は同じものを1回だけ書き、エントリからは番号で参照する（ネイティブ版の同じ通知が RID ごとに並ぶため）
var bodyIds = new Dictionary<string, int>();
var bodies = new StringBuilder();
var index = new StringBuilder();
foreach (var entry in entries)
{
    index.Append("@@PACKAGE\t").Append(entry.Name).Append('\t').Append(entry.Version).Append('\t').Append(entry.License).Append('\n');
    foreach (var text in entry.Texts)
    {
        if (text.Body.Split('\n').Any(line => line.StartsWith("@@", StringComparison.Ordinal)))
            throw new InvalidOperationException($"{entry.Name}: 本文に区切り行 @@ がある（{text.Title}）");
        if (!bodyIds.TryGetValue(text.Body, out var bodyId))
        {
            bodyId = bodyIds.Count;
            bodyIds.Add(text.Body, bodyId);
            bodies.Append("@@BODY\t").Append(bodyId).Append('\n').Append(text.Body).Append('\n');
        }
        index.Append("@@TEXT\t").Append(text.Title).Append('\t').Append(bodyId).Append('\n');
    }
}

var output = bodies.Append(index).ToString();
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, output, new UTF8Encoding(false));
Console.WriteLine($"{entries.Count} 件（.NET 含む）、本文 {bodyIds.Count} 種、{output.Length:N0} 文字 → {outputPath}");

// 著作権表示は nuspec の copyright、なければ authors から作る
static string CopyrightLine(XDocument nuspec)
{
    var copyright = Meta(nuspec, "copyright");
    return string.IsNullOrEmpty(copyright) ? "Copyright (c) " + Meta(nuspec, "authors") : copyright;
}

static string? Meta(XDocument nuspec, string name) =>
    nuspec.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;

// PATH から dotnet 本体のあるフォルダを探す（LICENSE.txt と ThirdPartyNotices.txt がそこにある）
static string FindDotnetRoot()
{
    var exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        if (File.Exists(Path.Combine(dir, exe)) && File.Exists(Path.Combine(dir, "ThirdPartyNotices.txt"))) return dir;
    throw new InvalidOperationException("PATH に .NET SDK の dotnet がない（ThirdPartyNotices.txt のあるフォルダを探せない）");
}

static string Run(string file, string args)
{
    var psi = new ProcessStartInfo(file, args)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        StandardOutputEncoding = Encoding.UTF8,
    };
    using var process = Process.Start(psi)!;
    var stderr = process.StandardError.ReadToEndAsync();
    var stdout = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{file} {args} が失敗: {stderr.Result}");
    return stdout;
}

static string ReadText(string path) =>
    File.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();

record Text(string Title, string Body);

record Entry(string Name, string Version, string License, List<Text> Texts);
