using HanautaSketch.Engine.Editing;

namespace HanautaSketch.Tests.Editing;

public class UndoHistoryTests
{
    [Fact]
    public void UndoThenRedo_RestoresStates()
    {
        var history = new UndoHistory<string>();
        history.Push("a");
        history.Push("b");

        Assert.Equal("b", history.Undo("c"));
        Assert.Equal("a", history.Undo("b"));
        Assert.False(history.CanUndo);

        Assert.Equal("b", history.Redo("a"));
        Assert.Equal("c", history.Redo("b"));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_ClearsRedo()
    {
        var history = new UndoHistory<string>();
        history.Push("a");
        history.Undo("b");
        history.Push("a");

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_DropsOldestBeyondCapacity()
    {
        var history = new UndoHistory<int>(capacity: 2);
        history.Push(1);
        history.Push(2);
        history.Push(3);

        Assert.Equal(3, history.Undo(4));
        Assert.Equal(2, history.Undo(3));
        Assert.False(history.CanUndo);
    }
}
