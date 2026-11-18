using CommunityToolkit.Mvvm.Input;
using HanautaSketch.Engine.Editing;
using HanautaSketch.Resources;
using HanautaSketch.Services;

namespace HanautaSketch.ViewModels;

/// <summary>
/// 曲全体の移調。1回の操作が Undo の1ステップになる。
/// </summary>
public partial class MainWindowViewModel
{
    [RelayCommand]
    private void TransposeUp() => Transpose(1);

    [RelayCommand]
    private void TransposeDown() => Transpose(-1);

    [RelayCommand]
    private void TransposeOctaveUp() => Transpose(TranspositionEditing.MaxSemitones);

    [RelayCommand]
    private void TransposeOctaveDown() => Transpose(-TranspositionEditing.MaxSemitones);

    /// <summary>
    /// 曲全体を移調する。音域外になるノーツがあれば何も変えず、その旨を知らせる。
    /// </summary>
    private void Transpose(int semitones)
    {
        if (!TranspositionEditing.CanTranspose(Song, semitones))
        {
            Message = Strings.TransposeOutOfRange;
            return;
        }

        OnEditStarted();
        TranspositionEditing.Transpose(Song, semitones);
        OnEditCompleted();

        NotesRevision++;
        OnConductorChanged();
        Message = Strings.Format(Strings.Transposed, semitones.ToString("+0;-0"), PlatformKeys.CommandName);
    }
}
