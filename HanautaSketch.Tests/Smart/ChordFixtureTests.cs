using System.Text.RegularExpressions;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Tests.Smart;

/// <summary>
/// 実際に打ち込んだ .hsk（Fixtures/）に対して、期待するコード進行が判別されるかを確かめる。
/// 期待値は MML 風に書く: 「コード名 + 音価」をスペース区切りで並べる。
/// 音価は 1/2/4/8/16 と付点「.」、タイは「&amp;」か「+」でつなぐ（例: Eb8&amp;2 = 8分＋2分）。
/// 転回形は区別しない。
/// </summary>
public partial class ChordFixtureTests
{
    [Theory]
    [InlineData("test1-canon.hsk", "D2 A2 Bm2 F#m2 G2 D2 G2 A2")]
    [InlineData("test2-syncopation.hsk", "Ab2 Bb2 Cm8. Ddim8. Eb8&2 Fm2 Bb2 Cm8. Bb8. Cm8+2")]
    public async Task Fixture_MatchesExpectedProgression(string file, string expected)
    {
        var song = (await LoadFixture(file)).Song;
        var track = song.Tracks.OfType<SmartChordTrack>().FirstOrDefault() ?? AddDefaultChordTrack(song);
        track.Regenerate(song);

        Assert.Equal(Format(ParseExpected(expected, song.Ppq), song.Ppq), Format(track.Chords, song.Key, song.Ppq));
    }

    /// <summary>
    /// スマートコードトラックのないフィクスチャ用。先頭を Melody、2本目を Bass としてソースにする。
    /// </summary>
    private static SmartChordTrack AddDefaultChordTrack(Song song)
    {
        var noteTracks = song.Tracks.OfType<NoteTrack>().ToList();
        var track = new SmartChordTrack { BassSource = noteTracks[1], MelodySource = noteTracks[0] };
        song.Tracks.Add(track);
        return track;
    }

    private static async Task<ProjectDocument> LoadFixture(string file)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        return await ProjectSerializer.LoadAsync(stream);
    }

    #region 期待値の表記

    [GeneratedRegex(@"^(?<name>[A-G][#b]?(?:m|dim|aug)?)(?<length>[0-9.&+]+)$")]
    private static partial Regex TokenPattern();

    /// <summary>
    /// MML 風の期待値を (コード名, 開始 tick, 長さ) の列にする。
    /// </summary>
    private static List<(string Name, int Start, int Length)> ParseExpected(string mml, int ppq)
    {
        var result = new List<(string, int, int)>();
        var tick = 0;
        foreach (var token in mml.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = TokenPattern().Match(token);
            if (!match.Success) throw new FormatException($"期待値の書式が不正: {token}");

            var length = match.Groups["length"].Value.Split('&', '+').Sum(part => ParseLength(part, ppq));
            result.Add((match.Groups["name"].Value, tick, length));
            tick += length;
        }
        return result;
    }

    /// <summary>
    /// 「8.」のような音価を tick に変換する。
    /// </summary>
    private static int ParseLength(string text, int ppq)
    {
        var dots = text.Count(c => c == '.');
        var division = int.Parse(text.TrimEnd('.'));
        var length = ppq * 4 / division;
        var total = length;
        for (var i = 0; i < dots; i++)
        {
            length /= 2;
            total += length;
        }
        return total;
    }

    /// <summary>
    /// 失敗時に差分が読みやすいよう、「コード名@開始拍+長さ拍」の文字列にそろえる。
    /// </summary>
    private static string[] Format(IEnumerable<(string Name, int Start, int Length)> segments, int ppq) =>
        segments.Select(s => $"{s.Name}@{(double)s.Start / ppq:0.##}+{(double)s.Length / ppq:0.##}").ToArray();

    private static string[] Format(IReadOnlyList<ChordSegment> segments, Key key, int ppq) =>
        Format(segments.Select(s => (s.Chord.RootPosition.GetName(key), s.Start, s.Length)), ppq);

    #endregion
}
