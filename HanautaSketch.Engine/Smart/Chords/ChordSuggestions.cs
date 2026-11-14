using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// override で差し替えるコードの候補。前のコードからの来やすさと、区間のベースとぶつかるか。
/// </summary>
/// <param name="ClashesWithBass">区間のベースの半分以上（長さの重み）が構成音に入らない</param>
public sealed record ChordSuggestion(Chord Chord, double Probability, bool ClashesWithBass);

/// <summary>
/// コードレーンの区間に当てるコードの候補を、前のコードからの来やすさ（<see cref="ChordTransitionTable"/>）で並べる。
/// 次のコードは見ない（判別直後は仮のダイアトニックコードで、当てにならないため）。
/// </summary>
public sealed class ChordSuggestions(Key key, Chord? previous, IReadOnlyList<Note> bass, int start, int end,
    ChordTransitionTable? table = null)
{
    private readonly ChordTransitionTable _table = table ?? ChordTransitionTable.Default;

    /// <summary>根音はそのままで種類を変える候補を、来やすい順に</summary>
    public IReadOnlyList<ChordSuggestion> QualitiesOf(int root, int count) =>
        ChordQualities.All.Select(q => Evaluate(new Chord(root, q.Quality)))
            .OrderByDescending(s => s.Probability)
            .Take(count)
            .ToList();

    /// <summary>根音を変える候補（種類は問わない）を、来やすい順に</summary>
    public IReadOnlyList<ChordSuggestion> OtherRoots(int currentRoot, int count) =>
        Enumerable.Range(0, 12).Where(r => r != currentRoot)
            .SelectMany(r => ChordQualities.All.Select(q => Evaluate(new Chord(r, q.Quality))))
            .OrderByDescending(s => s.Probability)
            .Take(count)
            .ToList();

    public ChordSuggestion Evaluate(Chord chord) =>
        new(chord, _table.Probability(key, previous?.RootPosition, chord.RootPosition), ClashesWithBass(chord));

    /// <summary>
    /// 区間のベースのうち、構成音に入らない音の長さが半分を超えるか。ベースがなければ false。
    /// </summary>
    public bool ClashesWithBass(Chord chord)
    {
        var tones = chord.GetPitchClasses();
        var total = 0;
        var outside = 0;
        foreach (var note in bass)
        {
            var overlap = Math.Min(note.EndTick, end) - Math.Max(note.Tick, start);
            if (overlap <= 0) continue;
            total += overlap;
            if (!tones.Contains(note.PitchClass)) outside += overlap;
        }
        return total > 0 && outside * 2 > total;
    }
}
