namespace HanautaSketch.Engine.Editing;

/// <summary>
/// 状態のスナップショットを積む Undo/Redo 履歴。
/// 変更の直前の状態を <see cref="Push"/> し、戻すときは現在の状態と引き換えに前の状態を受け取る。
/// </summary>
public sealed class UndoHistory<T>(int capacity = 200)
{
    private readonly List<T> _undo = [];
    private readonly Stack<T> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// 変更前の状態を記録する。やり直し履歴は破棄する。
    /// </summary>
    public void Push(T before)
    {
        _undo.Add(before);
        if (_undo.Count > capacity) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>
    /// 1つ前の状態を返す。current はやり直し用に記録する。
    /// </summary>
    public T Undo(T current)
    {
        if (!CanUndo) throw new InvalidOperationException("元に戻せる履歴がない。");
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Push(current);
        return previous;
    }

    /// <summary>
    /// 元に戻す前の状態を返す。current は元に戻す用に記録する。
    /// </summary>
    public T Redo(T current)
    {
        if (!CanRedo) throw new InvalidOperationException("やり直せる履歴がない。");
        var next = _redo.Pop();
        _undo.Add(current);
        return next;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
