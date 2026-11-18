using System.Text;

namespace HanautaSketch.Services;

/// <summary>ライセンス一覧の1項目（パッケージや本体、データの出典）</summary>
public sealed record LicenseEntry(string Name, string Version, string License, IReadOnlyList<LicenseText> Texts);

/// <summary>1項目に付くライセンス本文（ファイル名や種類を見出しにする）</summary>
public sealed record LicenseText(string Title, string Body);

/// <summary>
/// 配布物に含むライセンスの一覧。依存パッケージは tools/LicenseCollector が作る third-party.txt を埋め込みから読む。
/// </summary>
public static class OpenSourceLicenses
{
    /// <summary>
    /// ビルドに埋め込んだ LICENSE（MIT）の本文を返す。埋め込みがなければ空。
    /// </summary>
    public static string ReadAppLicense() => ReadEmbedded("LICENSE");

    /// <summary>
    /// 埋め込んだ依存パッケージ（.NET SDK を含む）のライセンス一覧を読む。
    /// </summary>
    public static IReadOnlyList<LicenseEntry> LoadPackages() => Parse(ReadEmbedded("third-party.txt"));

    /// <summary>
    /// third-party.txt の形式（tools/LicenseCollector/CollectLicenses.cs が書く）を読む。
    /// 先頭に本文の一覧（@@BODY）、続けてパッケージ（@@PACKAGE）と、番号で本文を参照する @@TEXT が並ぶ。
    /// </summary>
    public static IReadOnlyList<LicenseEntry> Parse(string text)
    {
        var bodies = new Dictionary<int, string>();
        var entries = new List<LicenseEntry>();
        var texts = new List<LicenseText>();
        string? name = null;
        var version = "";
        var license = "";
        int? bodyId = null;
        var body = new StringBuilder();

        void FlushBody()
        {
            if (bodyId is { } id) bodies[id] = body.ToString().TrimEnd();
            bodyId = null;
            body.Clear();
        }

        void FlushEntry()
        {
            if (name is null) return;
            entries.Add(new LicenseEntry(name, version, license, texts.ToArray()));
            texts.Clear();
            name = null;
        }

        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("@@BODY\t", StringComparison.Ordinal))
            {
                FlushBody();
                bodyId = int.Parse(line["@@BODY\t".Length..]);
            }
            else if (line.StartsWith("@@PACKAGE\t", StringComparison.Ordinal))
            {
                FlushBody();
                FlushEntry();
                var fields = line.Split('\t');
                name = fields[1];
                version = fields[2];
                license = fields[3];
            }
            else if (line.StartsWith("@@TEXT\t", StringComparison.Ordinal))
            {
                var fields = line.Split('\t');
                texts.Add(new LicenseText(fields[1], bodies[int.Parse(fields[2])]));
            }
            else if (bodyId is not null)
            {
                body.Append(line).Append('\n');
            }
        }

        FlushBody();
        FlushEntry();
        return entries;
    }

    private static string ReadEmbedded(string resourceName)
    {
        using var stream = typeof(OpenSourceLicenses).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
