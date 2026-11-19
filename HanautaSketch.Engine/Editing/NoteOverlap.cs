using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Editing;

/// <summary>
/// 同じ音高のノーツの重なりを解消する。編集したノーツを優先し、重なる既存のノーツを削る。
/// </summary>
public static class NoteOverlap
{
    /// <summary>
    /// edited のノーツと重なる、同じ音高の他のノーツから重なる部分を取り除く。
    /// 頭が前にあるものは終わりを切り詰め、後ろにはみ出たものは残りの部分にし、完全に覆われたものは消す。
    /// 編集したノーツどうしの完全な重複（位置・音高が同じ）は1つにまとめ、同じ音高で一部が重なるものは前のノートの終わりを切り詰める
    /// （スケールロックで別々の音高が同じ音に寄ったときなど）。
    /// </summary>
    /// <param name="notes">ノーツ。その場で並び替わる</param>
    /// <param name="edited">編集したノーツのインデックス</param>
    /// <returns>並び替え後の、編集したノーツのインデックス</returns>
    public static HashSet<int> Resolve(List<Note> notes, IReadOnlyCollection<int> edited)
    {
        var editedNotes = TrimEachOther(edited.Where(i => i >= 0 && i < notes.Count).Distinct().Order()
            .Select(i => notes[i])
            .DistinctBy(n => (n.Tick, n.Pitch))
            .ToList());
        var editedSet = edited.ToHashSet();

        var result = new List<Note>();
        for (var i = 0; i < notes.Count; i++)
        {
            if (editedSet.Contains(i)) continue;
            result.AddRange(Subtract(notes[i], editedNotes));
        }

        var start = result.Count;
        result.AddRange(editedNotes);
        notes.Clear();
        notes.AddRange(result);
        return Enumerable.Range(start, editedNotes.Count).ToHashSet();
    }

    /// <summary>
    /// 同じ音高で重なるノーツの、前のノートの終わりを後ろのノートの頭で切る。並びは変えない。
    /// 開始位置が同じものは事前にまとめてある前提。
    /// </summary>
    private static List<Note> TrimEachOther(List<Note> notes)
    {
        var result = notes.ToList();
        foreach (var group in result.Select((note, index) => (Note: note, Index: index)).GroupBy(x => x.Note.Pitch))
        {
            var ordered = group.OrderBy(x => x.Note.Tick).ToList();
            for (var k = 0; k + 1 < ordered.Count; k++)
            {
                var (note, index) = ordered[k];
                var nextTick = ordered[k + 1].Note.Tick;
                if (note.EndTick > nextTick) result[index] = note with { Length = nextTick - note.Tick };
            }
        }
        return result;
    }

    /// <summary>
    /// note から、同じ音高の cutters と重なる区間を除いた残りを返す。
    /// </summary>
    private static IEnumerable<Note> Subtract(Note note, List<Note> cutters)
    {
        var pieces = new List<(int Start, int End)> { (note.Tick, note.EndTick) };
        foreach (var cutter in cutters.Where(c => c.Pitch == note.Pitch))
        {
            var next = new List<(int Start, int End)>();
            foreach (var (start, end) in pieces)
            {
                if (cutter.EndTick <= start || end <= cutter.Tick)
                {
                    next.Add((start, end));
                    continue;
                }
                if (start < cutter.Tick) next.Add((start, cutter.Tick));
                if (cutter.EndTick < end) next.Add((cutter.EndTick, end));
            }
            pieces = next;
        }
        return pieces.Select(p => note with { Tick = p.Start, Length = p.End - p.Start });
    }
}
