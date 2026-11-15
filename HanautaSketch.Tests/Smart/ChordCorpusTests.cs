using System.Text;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Chords.Corpus;

namespace HanautaSketch.Tests.Smart;

public class ChordCorpusTests
{
    [Fact]
    public void Billboard_ReadsTonicChordsAndMode()
    {
        const string text = """
            # title: Test
            # metre: 4/4
            # tonic: A

            0.0	silence
            0.1	A, intro, | A:min | A:min7 C:maj/3 |, (voice
            5.0	| D:min | E:7 | A:min | x2
            9.0	end
            """;

        var (key, chords) = ChordCorpusParser.ParseBillboard(text)!.Value;

        Assert.Equal(new Key(9, Mode.Minor), key);
        Assert.Equal(["Am", "Am7", "C", "Dm", "E7", "Am"], chords.Select(c => c.ToString()).ToArray());
    }

    [Theory]
    [InlineData("Csmin7", "C#m7")]
    [InlineData("Asus4", "Asus4")]
    [InlineData("Cssus4", "C#sus4")]
    [InlineData("Bb/D", "A#")] // 分数コードのベースは捨てる（表記は C major の調号）
    [InlineData("G7s9", "G7(♯9)")]
    [InlineData("Dadd13", "D6")]
    public void Chordonomicon_ParsesTokens(string token, string expected)
    {
        Assert.Equal(expected, ChordCorpusParser.ParseChordonomiconChord(token)?.ToString());
    }

    [Fact]
    public void Chordonomicon_SkipsSectionsAndUnknownSuffixes()
    {
        var unknown = new Dictionary<string, int>();
        var chords = ChordCorpusParser.ParseChordonomiconLine("1,<intro_1> C <verse_1> F Gweird Amin,2003,'pop'", unknown);

        Assert.Equal(["C", "F", "Am"], chords.Select(c => c.ToString()).ToArray());
        Assert.Equal(1, unknown["chordonomicon:weird"]);
    }

    [Theory]
    [InlineData("C F G C Amin F G C", 0, Mode.Major)]
    [InlineData("Amin F C G Amin Dmin E Amin", 9, Mode.Minor)]
    [InlineData("D G A D Bmin G A D", 2, Mode.Major)]
    public void EstimateKey_PicksDiatonicKeyAndRelativeMinor(string progression, int tonic, Mode mode)
    {
        var chords = progression.Split(' ').Select(t => ChordCorpusParser.ParseChordonomiconChord(t)!.Value).ToList();
        Assert.Equal(new Key(tonic, mode), ChordCorpusParser.EstimateKey(chords));
    }

    [Fact]
    public void EstimateKey_RejectsChromaticProgressions()
    {
        var chords = "C Cs D Ds E F Fs G".Split(' ').Select(t => ChordCorpusParser.ParseChordonomiconChord(t)!.Value).ToList();
        Assert.Null(ChordCorpusParser.EstimateKey(chords));
    }

    [Fact]
    public async Task ImportChordonomicon_CountsTransitionsAndReportsProgress()
    {
        const string csv = """
            id,chords,release_date
            1,<verse_1> C G C G Amin F G C,2003
            2,<verse_1> D A D A Bmin G A D,2004
            """;
        var reports = new List<ChordImportProgress>();
        var counts = await ChordCorpusImporter.ImportChordonomiconAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(csv)), csv.Length, new SyncProgress(reports));

        Assert.Equal(2, counts.Sources.Single().Songs);
        Assert.Equal(2, reports.Last().Songs);

        // 2曲とも I → V が2回ずつ（キーに相対で数える）
        var table = new ChordTransitionTable(counts);
        var c = new Key(0, Mode.Major);
        Assert.True(table.Probability(c, new Chord(0, ChordQuality.Major), new Chord(7, ChordQuality.Major))
                    > table.Probability(c, new Chord(0, ChordQuality.Major), new Chord(5, ChordQuality.Major)));
    }

    [Fact]
    public void Counts_RoundTripAndMerge()
    {
        var key = new Key(0, Mode.Major);
        List<Chord> progression = [new(7, ChordQuality.Major), new(0, ChordQuality.Major)];
        var a = new ChordTransitionCounts();
        for (var i = 0; i < 3; i++) a.Add(key, progression);
        a.Sources.Add(new ChordCorpusSource("A", "CC0", 3));

        var stream = new MemoryStream();
        a.Write(stream);
        stream.Position = 0;
        var read = ChordTransitionCounts.Read(stream);
        read.Merge(a);

        Assert.Equal(["A", "A"], read.Sources.Select(s => s.Name).ToArray());
        // G → C が 3 + 3 = 6 回、G は 6 回
        var table = new ChordTransitionTable(read);
        var p = table.Probability(key, progression[0], progression[1]);
        Assert.Equal((6 + 20 * 0.5) / (6 + 20), p, 9);
    }

    [Fact]
    public void BundledTable_IsBillboardOnly()
    {
        var source = Assert.Single(ChordTransitionTable.Default.Sources);
        Assert.Equal("CC0", source.License);
    }

    /// <summary>Progress&lt;T&gt; は同期コンテキストに投げるので、テストでは即座に記録する実装を使う</summary>
    private sealed class SyncProgress(List<ChordImportProgress> reports) : IProgress<ChordImportProgress>
    {
        public void Report(ChordImportProgress value) => reports.Add(value);
    }
}
