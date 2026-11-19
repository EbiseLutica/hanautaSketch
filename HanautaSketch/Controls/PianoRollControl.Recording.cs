using Avalonia;
using Avalonia.Media;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.Controls;

/// <summary>
/// 録音中の音量の1区間分。
/// </summary>
/// <param name="StartTick">区間の先頭</param>
/// <param name="EndTick">区間の終わり</param>
/// <param name="Level">音量（0〜1。dB を割り当てたもの）</param>
public readonly record struct RecordingLevel(double StartTick, double EndTick, double Level);

/// <summary>
/// 録音まわり：録音中の音量のオーバーレイと、録音後に採譜したノーツを選択する。
/// </summary>
public partial class PianoRollControl
{
    /// <summary>オーバーレイの、いちばん大きい音での片側の高さ（ノート編集領域の高さに対する割合）</summary>
    private const double RecordingLevelMaxRatio = 0.4;

    public static readonly StyledProperty<IReadOnlyList<RecordingLevel>?> RecordingLevelsProperty =
        AvaloniaProperty.Register<PianoRollControl, IReadOnlyList<RecordingLevel>?>(nameof(RecordingLevels));

    public static readonly StyledProperty<IReadOnlyCollection<Note>?> NotesToSelectProperty =
        AvaloniaProperty.Register<PianoRollControl, IReadOnlyCollection<Note>?>(nameof(NotesToSelect));

    /// <summary>録音中の音量。null なら描かない</summary>
    public IReadOnlyList<RecordingLevel>? RecordingLevels
    {
        get => GetValue(RecordingLevelsProperty);
        set => SetValue(RecordingLevelsProperty, value);
    }

    /// <summary>
    /// <see cref="NotesRevision"/> が変わったときに選択するノーツ。先にこれを設定してから NotesRevision を変える
    /// </summary>
    public IReadOnlyCollection<Note>? NotesToSelect
    {
        get => GetValue(NotesToSelectProperty);
        set => SetValue(NotesToSelectProperty, value);
    }

    /// <summary>NotesRevision の変化で選択を解除したあと、<see cref="NotesToSelect"/> のノーツを選択し直す</summary>
    private void SelectRequestedNotes()
    {
        if (NotesToSelect is not { Count: > 0 } targets || EditTrack is null) return;
        var notes = EditTrack.EditableNotes;
        for (var i = 0; i < notes.Count; i++)
        {
            if (targets.Contains(notes[i])) _selection.Add(i);
        }
    }

    /// <summary>
    /// 録音中の音量を、ノート編集領域の縦中央を 0 として上下に伸びる棒で描く。
    /// </summary>
    private void RenderRecordingLevels(DrawingContext context, Rect area)
    {
        if (RecordingLevels is not { Count: > 0 } levels) return;
        var center = area.Y + area.Height / 2;
        var maxHalf = area.Height * RecordingLevelMaxRatio;
        foreach (var level in levels)
        {
            var left = TickToX(level.StartTick);
            var right = TickToX(level.EndTick);
            if (right < area.X || left > area.Right) continue;
            var half = Math.Max(0.5, Math.Clamp(level.Level, 0, 1) * maxHalf);
            context.FillRectangle(Palette.RecordingLevel, new Rect(left, center - half, Math.Max(1, right - left - 1), half * 2));
        }
    }
}
