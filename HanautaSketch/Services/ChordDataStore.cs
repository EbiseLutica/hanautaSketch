using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Chords.Corpus;

namespace HanautaSketch.Services;

/// <summary>
/// 利用者が同意して作った、Chordonomicon を足したコード遷移の表（%APPDATA%\hanautaSketch）。
/// なければ同梱の表（Billboard のみ）を使う。
/// </summary>
public static class ChordDataStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hanautaSketch", "chord-transitions-extended.json");

    public static bool HasExtended => File.Exists(FilePath);

    /// <summary>使う表。追加の表が読めなければ同梱の表</summary>
    public static ChordTransitionTable LoadActive()
    {
        try
        {
            if (!File.Exists(FilePath)) return ChordTransitionTable.Default;
            using var stream = File.OpenRead(FilePath);
            return ChordTransitionTable.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                       or KeyNotFoundException or InvalidOperationException)
        {
            return ChordTransitionTable.Default;
        }
    }

    public static void DeleteExtended()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    /// <summary>
    /// Chordonomicon を取得しながら集計し、同梱の表と足し合わせて保存する。元の CSV は保存しない。
    /// 途中で止めたら（キャンセル・失敗）何も残さない。
    /// </summary>
    public static async Task<ChordTransitionTable> BuildExtendedAsync(IProgress<ChordImportProgress> progress,
        CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var counts = ChordTransitionTable.LoadBundledCounts();
        counts.Merge(await Task.Run(() => ChordCorpusImporter.DownloadChordonomiconAsync(http, progress, ct), ct));

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".part";
        await using (var file = File.Create(temp))
            counts.Write(file);
        File.Move(temp, FilePath, true);
        return new ChordTransitionTable(counts);
    }
}
