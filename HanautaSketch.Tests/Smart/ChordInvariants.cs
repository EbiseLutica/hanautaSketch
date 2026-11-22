using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Tests.Smart;

/// <summary>
/// 判別結果が、アルゴリズムの調整に関係なく満たすべき約束事。
/// </summary>
internal static class ChordInvariants
{
    /// <param name="overrides">重なりのない override</param>
    public static void AssertValid(IReadOnlyList<ChordSegment> segments, int endTick, IReadOnlyList<ChordOverride> overrides)
    {
        // [0, endTick) を隙間・重なりなく覆う
        Assert.NotEmpty(segments);
        Assert.Equal(0, segments[0].Start);
        Assert.All(segments, s => Assert.True(s.Length > 0, $"長さ 0 の区間: {s}"));
        for (var i = 1; i < segments.Count; i++)
            Assert.True(segments[i - 1].End == segments[i].Start, $"区間が連続していない: {segments[i - 1]} → {segments[i]}");
        Assert.Equal(endTick, segments[^1].End);

        // override の区間は、範囲もコードもそのまま出る
        foreach (var o in overrides.Where(o => o.Start < endTick))
        {
            var end = Math.Min(o.End, endTick);
            var covering = segments.Where(s => s.Start < end && o.Start < s.End).ToList();
            Assert.All(covering, s =>
            {
                Assert.True(s.IsOverride, $"override {o} の範囲に自動判別の区間がある: {s}");
                Assert.Equal(o.Chord, s.Chord);
            });
            Assert.Equal(o.Start, covering.Min(s => s.Start));
            Assert.Equal(end, covering.Max(s => s.End));
        }

        // override 以外の区間に IsOverride が立たない
        Assert.All(segments.Where(s => s.IsOverride), s =>
            Assert.Contains(overrides, o => o.Start <= s.Start && s.End <= o.End));
    }
}

/// <summary>
/// すべてのフィクスチャで、判別結果が約束事を満たすか確かめる。期待値は要らない。
/// </summary>
public class ChordInvariantFixtureTests
{
    public static TheoryData<string> Fixtures => new(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.hsk")
            .Select(Path.GetFileName)
            .Order()!);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Fixture_SatisfiesInvariants(string file)
    {
        var song = (await ChordFixtureTests.LoadFixture(file)).Song;
        var tracks = song.Tracks.OfType<SmartChordTrack>().ToList();
        if (tracks.Count == 0) tracks.Add(ChordFixtureTests.AddDefaultChordTrack(song));

        foreach (var track in tracks)
        {
            // SmartChordTrack.Regenerate と同じ入力で、制限で覆えない状況を例外にして判別する
            var endTick = song.EndBarTick(track.Overrides.Select(o => o.End).DefaultIfEmpty(0).Max());
            var segments = ChordDetector.Detect(track.BassSource?.Notes ?? [], track.MelodySource?.Notes ?? [],
                endTick, new ChordDetectionContext(song.Conductor), track.ChordSettings, track.Overrides, strict: true);

            ChordInvariants.AssertValid(segments, endTick, track.Overrides);
        }
    }
}
