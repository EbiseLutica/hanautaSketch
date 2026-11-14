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
}
