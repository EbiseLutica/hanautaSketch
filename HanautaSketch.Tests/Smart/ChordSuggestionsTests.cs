using System.Text;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Tests.Smart;

public class ChordSuggestionsTests
{
    private const int Bar = 1920;
    private static readonly Key CMajor = new(0, Mode.Major);

    /// <summary>
    /// 長調で V(G) → I(C) が多く、V → V7 が少しある小さな表
    /// </summary>
    private static ChordTransitionTable SmallTable() => ChordTransitionTable.Parse(new MemoryStream(Encoding.UTF8.GetBytes("""
        {
          "sources": [ { "name": "test", "license": "CC0", "songs": 1 } ],
          "major": {
            "unigram": { "0:Major": 100, "7:Major": 80, "5:Major": 60, "7:Dominant7": 10, "9:Minor": 40 },
            "bigram": { "7:Major": { "0:Major": 60, "9:Minor": 10, "7:Dominant7": 5 } }
          },
          "minor": { "unigram": { "0:Minor": 10 }, "bigram": {} }
        }
        """)));

    [Fact]
    public void Probability_UsesTransitionsAndFallsBackToUnigram()
    {
        var table = SmallTable();
        var g = new Chord(7, ChordQuality.Major);

        var toC = table.Probability(CMajor, g, new Chord(0, ChordQuality.Major));
        var toF = table.Probability(CMajor, g, new Chord(5, ChordQuality.Major));
        Assert.True(toC > toF, $"{toC} > {toF}");

        // 遷移のデータがない組み合わせも、全体での出やすさで0より大きくなる
        Assert.True(toF > 0);

        // 前のコードがなければ全体での出やすさ
        Assert.Equal(100.0 / 290, table.Probability(CMajor, null, new Chord(0, ChordQuality.Major)), 6);
    }

    [Fact]
    public void Probability_IsRelativeToKey()
    {
        // D major の A → D は C major の G → C と同じ度数
        var table = SmallTable();
        var dMajor = new Key(2, Mode.Major);
        Assert.Equal(
            table.Probability(CMajor, new Chord(7, ChordQuality.Major), new Chord(0, ChordQuality.Major)),
            table.Probability(dMajor, new Chord(9, ChordQuality.Major), new Chord(2, ChordQuality.Major)), 9);
    }

    [Fact]
    public void Suggestions_RankByPreviousChordAndMarkBassClash()
    {
        // ベースは区間中ずっと E
        List<Note> bass = [new(0, Bar, 40, 100)];
        var suggestions = new ChordSuggestions(CMajor, new Chord(7, ChordQuality.Major), bass, 0, Bar, SmallTable());

        var roots = suggestions.OtherRoots(7, 3);
        Assert.Equal("C", roots[0].Chord.ToString());
        Assert.Equal("Am", roots[1].Chord.ToString());
        Assert.False(roots[0].ClashesWithBass); // E は C の3度
        Assert.True(suggestions.Evaluate(new Chord(5, ChordQuality.Major)).ClashesWithBass); // E は F に入らない

        var qualities = suggestions.QualitiesOf(7, 2);
        // 同じコードの連続は数えていないので、G の次は G より G7 が来やすい
        Assert.Equal(["G7", "G"], qualities.Select(s => s.Chord.ToString()).ToArray());
    }

    [Fact]
    public void BundledTable_FavorsDominantResolution()
    {
        var table = ChordTransitionTable.Default;
        Assert.NotEmpty(table.Sources);

        var g7 = new Chord(7, ChordQuality.Dominant7);
        var best = Enumerable.Range(0, 12)
            .SelectMany(r => ChordQualities.All.Select(q => new Chord(r, q.Quality)))
            .MaxBy(c => table.Probability(CMajor, g7, c));
        Assert.Equal(new Chord(0, ChordQuality.Major), best);
    }

    [Fact]
    public void ExtendedChord_HasNameAndTones()
    {
        var chord = new Chord(7, ChordQuality.Dominant7Sharp9);
        Assert.Equal("G7(♯9)", chord.GetName(CMajor));
        Assert.Equal([7, 11, 2, 5, 10], chord.GetPitchClasses());
        Assert.DoesNotContain(2, chord.GetVoicingPitchClasses()); // 5音以上は5度を省く
    }
}
