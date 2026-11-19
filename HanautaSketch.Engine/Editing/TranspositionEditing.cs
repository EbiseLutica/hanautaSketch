using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Engine.Editing;

/// <summary>
/// 曲全体の移調。ノートトラックのノーツ・キーの変更点・スマートコードの override の根音と転回音を半音単位で動かす。
/// ドラムとスマートトラックの生成結果は対象外（前者は音程を持たず、後者はソースの移調後に再生成する）。
/// </summary>
public static class TranspositionEditing
{
    public const int MinPitch = 0;
    public const int MaxPitch = 127;

    /// <summary>一度に動かせる最大の半音数（1オクターブ）</summary>
    public const int MaxSemitones = 12;

    /// <summary>移調してもすべてのノーツが音域（0〜127）に収まるか</summary>
    public static bool CanTranspose(Song song, int semitones) =>
        Math.Abs(semitones) <= MaxSemitones
        && PitchedTracks(song).All(t => t.Notes.All(n => n.Pitch + semitones is >= MinPitch and <= MaxPitch));

    /// <summary>
    /// 曲を移調する。音域に収まらないときや 0 半音のときは何もせず false を返す。
    /// </summary>
    public static bool Transpose(Song song, int semitones)
    {
        if (semitones == 0 || !CanTranspose(song, semitones)) return false;

        foreach (var track in PitchedTracks(song))
            for (var i = 0; i < track.Notes.Count; i++)
                track.Notes[i] = track.Notes[i] with { Pitch = track.Notes[i].Pitch + semitones };

        var conductor = song.Conductor;
        foreach (var change in conductor.Keys.ToList())
            conductor.SetKey(change.Tick, change.Key with { Tonic = Mod12(change.Key.Tonic + semitones) });

        foreach (var smart in song.Tracks.OfType<SmartChordTrack>())
            for (var i = 0; i < smart.Overrides.Count; i++)
                if (smart.Overrides[i].Chord is { } chord)
                    smart.Overrides[i] = smart.Overrides[i] with { Chord = Shift(chord, semitones) };

        return true;
    }

    private static Chord Shift(Chord chord, int semitones) => chord with
    {
        Root = Mod12(chord.Root + semitones),
        Bass = chord.Bass is { } bass ? Mod12(bass + semitones) : null,
    };

    private static int Mod12(int value) => ((value % 12) + 12) % 12;

    /// <summary>移調するノートトラック（ドラムパートは音程を持たないので除く）</summary>
    private static IEnumerable<NoteTrack> PitchedTracks(Song song) => song.Tracks.OfType<NoteTrack>().Where(t => !t.IsDrum);
}
