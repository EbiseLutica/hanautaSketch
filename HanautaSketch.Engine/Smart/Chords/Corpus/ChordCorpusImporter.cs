using System.Formats.Tar;
using System.IO.Compression;

namespace HanautaSketch.Engine.Smart.Chords.Corpus;

/// <summary>Chordonomicon の取得・集計の進み具合</summary>
/// <param name="BytesRead">読んだバイト数</param>
/// <param name="TotalBytes">全体のバイト数（不明なら null）</param>
/// <param name="Songs">集計した曲数</param>
public readonly record struct ChordImportProgress(long BytesRead, long? TotalBytes, int Songs);

/// <summary>
/// データセットを読んで <see cref="ChordTransitionCounts"/> にする。
/// </summary>
public static class ChordCorpusImporter
{
    /// <summary>McGill Billboard の salami_chords（tar.gz）。CC0</summary>
    public const string BillboardUrl = "https://www.dropbox.com/s/2lvny9ves8kns4o/billboard-2.0-salami_chords.tar.gz?dl=1";

    /// <summary>Chordonomicon の CSV。毎回同じ表になるよう、リビジョン（2025-05-15）を固定する。CC BY-NC 4.0</summary>
    public const string ChordonomiconUrl =
        "https://huggingface.co/datasets/ailsntua/Chordonomicon/resolve/f3a8b678bd65639c01caa342921733f1db01be23/chordonomicon_v2.csv";

    public static ChordCorpusSource BillboardSource(int songs) => new("McGill Billboard Project", "CC0", songs);

    public static ChordCorpusSource ChordonomiconSource(int songs) =>
        new("Chordonomicon (Kantarelis et al., 2024)", "CC BY-NC 4.0", songs);

    /// <summary>
    /// Billboard の tar.gz を読む。
    /// </summary>
    public static ChordTransitionCounts ImportBillboard(Stream tarGz, IDictionary<string, int>? unknown = null)
    {
        var counts = new ChordTransitionCounts();
        using var gzip = new GZipStream(tarGz, CompressionMode.Decompress, leaveOpen: true);
        using var tar = new TarReader(gzip);
        var songs = 0;
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.DataStream is null || !entry.Name.EndsWith("salami_chords.txt")) continue;
            using var reader = new StreamReader(entry.DataStream);
            if (ChordCorpusParser.ParseBillboard(reader.ReadToEnd(), unknown) is not var (key, chords)) continue;
            counts.Add(key, chords);
            songs++;
        }
        counts.Sources.Add(BillboardSource(songs));
        return counts;
    }

    /// <summary>
    /// Chordonomicon の CSV を読む。キーがはっきりしない曲は使わない。
    /// </summary>
    /// <param name="totalBytes">進み具合の表示用。不明なら null</param>
    public static async Task<ChordTransitionCounts> ImportChordonomiconAsync(Stream csv, long? totalBytes,
        IProgress<ChordImportProgress>? progress = null, IDictionary<string, int>? unknown = null,
        CancellationToken ct = default)
    {
        var counts = new ChordTransitionCounts();
        var counting = new CountingStream(csv);
        using var reader = new StreamReader(counting);
        await reader.ReadLineAsync(ct); // ヘッダー

        var songs = 0;
        var lastReport = 0L;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var chords = ChordCorpusParser.ParseChordonomiconLine(line, unknown);
            if (chords.Count >= 2 && ChordCorpusParser.EstimateKey(chords) is { } key)
            {
                counts.Add(key, chords);
                songs++;
            }

            // 1MB ごとに知らせる
            if (progress is not null && counting.BytesRead - lastReport >= 1 << 20)
            {
                lastReport = counting.BytesRead;
                progress.Report(new ChordImportProgress(counting.BytesRead, totalBytes, songs));
            }
        }

        progress?.Report(new ChordImportProgress(counting.BytesRead, totalBytes, songs));
        counts.Sources.Add(ChordonomiconSource(songs));
        return counts;
    }

    /// <summary>
    /// Chordonomicon をネットワークから読みながら集計する（元の CSV はディスクに保存しない）。
    /// </summary>
    public static async Task<ChordTransitionCounts> DownloadChordonomiconAsync(HttpClient http,
        IProgress<ChordImportProgress>? progress = null, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(ChordonomiconUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await ImportChordonomiconAsync(stream, response.Content.Headers.ContentLength, progress, null, ct);
    }

    /// <summary>読んだバイト数を数えるだけの読み取りストリーム</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            Count(await inner.ReadAsync(buffer, ct));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        private int Count(int read)
        {
            BytesRead += read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
