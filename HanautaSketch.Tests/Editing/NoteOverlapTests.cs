using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Editing;

namespace HanautaSketch.Tests.Editing;

public class NoteOverlapTests
{
    [Fact]
    public void EditedNote_TrimsCoversAndShortensSamePitchNotes()
    {
        List<Note> notes =
        [
            new(0, 480, 60, 100),    // 頭が前 → 240 で切る
            new(300, 60, 60, 100),   // 覆われる → 消す
            new(400, 400, 60, 100),  // 後ろにはみ出す → 720 から
            new(240, 480, 64, 100),  // 別の音高 → そのまま
            new(240, 480, 60, 90),   // 編集したノート
        ];

        var edited = NoteOverlap.Resolve(notes, [4]);

        Assert.Equal(
            [new(0, 240, 60, 100), new(720, 80, 60, 100), new(240, 480, 64, 100), new(240, 480, 60, 90)],
            notes);
        Assert.Equal([3], edited);
    }

    [Fact]
    public void EditedNote_SplitsNoteThatSpansIt()
    {
        List<Note> notes = [new(0, 960, 60, 100), new(240, 240, 60, 90)];

        NoteOverlap.Resolve(notes, [1]);

        Assert.Equal([new(0, 240, 60, 100), new(480, 480, 60, 100), new(240, 240, 60, 90)], notes);
    }

    [Fact]
    public void ExactDuplicates_AreMerged()
    {
        // 同じ位置・音高のノーツを貼り付けた場合
        List<Note> notes = [new(0, 240, 60, 100), new(0, 240, 60, 100), new(0, 240, 60, 100)];

        var edited = NoteOverlap.Resolve(notes, [1, 2]);

        Assert.Equal([new Note(0, 240, 60, 100)], notes);
        Assert.Equal([0], edited);
    }

    /// <summary>
    /// 編集したノーツどうしが同じ音高で一部重なるときは、前のノートの終わりを後ろのノートの頭で切ることを確かめる。
    /// スケールロックで C と C# がどちらも C に寄った場合など。重ならない編集ノーツはそのまま。
    /// </summary>
    [Fact]
    public void EditedNotes_PartiallyOverlappingSamePitch_AreTrimmed()
    {
        List<Note> notes = [new(0, 960, 60, 100), new(480, 960, 60, 90), new(0, 480, 64, 80)];

        var edited = NoteOverlap.Resolve(notes, [0, 1, 2]);

        Assert.Equal([new(0, 480, 60, 100), new(480, 960, 60, 90), new(0, 480, 64, 80)], notes);
        Assert.Equal([0, 1, 2], edited);
    }

    /// <summary>
    /// 編集ノーツの並びが時間順でなくても、時間で前にあるノートの方を切り詰め、並びは保つことを確かめる。
    /// </summary>
    [Fact]
    public void EditedNotes_PartialOverlap_TrimsEarlierNoteRegardlessOfOrder()
    {
        List<Note> notes = [new(480, 960, 60, 90), new(0, 960, 60, 100)];

        NoteOverlap.Resolve(notes, [0, 1]);

        Assert.Equal([new(480, 960, 60, 90), new(0, 480, 60, 100)], notes);
    }

    /// <summary>
    /// 後ろの編集ノートが前の編集ノートの中に収まるときも、前のノートを後ろのノートの頭で切ることと、
    /// 編集していないノーツは切り詰めたあとの長さで削られる（切り詰め前の長さでは削られない）ことを確かめる。
    /// </summary>
    [Fact]
    public void EditedNotes_NestedOverlap_TrimsBeforeCuttingOthers()
    {
        List<Note> notes = [new(0, 1920, 60, 100), new(480, 240, 60, 90), new(960, 480, 60, 80)];

        var edited = NoteOverlap.Resolve(notes, [0, 1]);

        Assert.Equal([new(960, 480, 60, 80), new(0, 480, 60, 100), new(480, 240, 60, 90)], notes);
        Assert.Equal([1, 2], edited);
    }
}
