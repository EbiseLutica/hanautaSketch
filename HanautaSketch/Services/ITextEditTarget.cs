namespace HanautaSketch.Services;

/// <summary>
/// 文字の入力欄の取り消し・やり直し。入力欄にフォーカスがある間は、編集メニューとショートカットの
/// 「取り消す」「やり直す」を曲の履歴ではなくこちらに向ける（macOS・Windows のアプリの慣例）。
/// </summary>
public interface ITextEditTarget
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    void Undo();
    void Redo();
}
