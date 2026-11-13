using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// コード進行から伴奏のノーツを作る。凝ったことはせず、密集配置の三和音を前のコードから近い転回形で鳴らす。
/// </summary>
public static class ChordAccompaniment
{
    /// <summary>ボイシングの最低音の下限（F3）</summary>
    private const int LowestPitch = 53;

    /// <summary>ボイシングの最高音の上限（D5）</summary>
    private const int HighestPitch = 74;

    /// <summary>最初のコードのボイシングで中心に寄せる音高（D4 付近）</summary>
    private const double InitialCenter = 62;

    public static IReadOnlyList<Note> Generate(IReadOnlyList<ChordSegment> chords, IReadOnlyList<Note> bass,
        AccompanimentSettings settings, int ppq)
    {
        var division = settings.DivisionTicks > 0 ? settings.DivisionTicks : ppq / 2;
        var result = new List<Note>();
        int[]? previous = null;

        foreach (var segment in chords)
        {
            var voicing = Voice(segment.Chord, previous);
            previous = voicing;

            switch (settings.Pattern)
            {
                case AccompanimentPattern.Sustain:
                    AddChord(result, voicing, segment.Start, segment.Length, settings.Velocity);
                    break;
                case AccompanimentPattern.Beat:
                    for (var t = segment.Start; t < segment.End; t += division)
                        AddChord(result, voicing, t, Math.Min(division, segment.End - t), settings.Velocity);
                    break;
                case AccompanimentPattern.FollowBass:
                    AddFollowingBass(result, voicing, segment, bass, settings.Velocity);
                    break;
                case AccompanimentPattern.Arpeggio:
                    AddArpeggio(result, voicing, segment, division, settings.Velocity);
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// 密集配置の三和音を、音域内で前のボイシングから最も動きの少ない転回形・オクターブで選ぶ。
    /// </summary>
    public static int[] Voice(Chord chord, int[]? previous)
    {
        var tones = chord.GetPitchClasses();
        int[]? best = null;
        var bestCost = double.MaxValue;

        for (var inversion = 0; inversion < tones.Count; inversion++)
        {
            for (var octave = 2; octave <= 6; octave++)
            {
                var candidate = Build(tones, inversion, octave);
                if (candidate[0] < LowestPitch || candidate[^1] > HighestPitch) continue;

                var cost = previous is null
                    ? Math.Abs(candidate.Average() - InitialCenter)
                    : candidate.Zip(previous).Sum(p => Math.Abs(p.First - p.Second));
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = candidate;
                }
            }
        }

        return best ?? Build(tones, 0, 4);
    }

    /// <summary>
    /// 指定の転回形で、最低音が octave（MIDI のオクターブ番号）に入る密集配置を作る。
    /// </summary>
    private static int[] Build(IReadOnlyList<int> tones, int inversion, int octave)
    {
        var pitches = new int[tones.Count];
        var current = (octave + 1) * 12 + tones[inversion];
        pitches[0] = current;
        for (var i = 1; i < tones.Count; i++)
        {
            var pc = tones[(inversion + i) % tones.Count];
            var next = current - current % 12 + pc;
            if (next <= current) next += 12;
            pitches[i] = current = next;
        }
        return pitches;
    }

    private static void AddChord(List<Note> result, int[] voicing, int tick, int length, int velocity)
    {
        foreach (var pitch in voicing)
            result.Add(new Note(tick, length, pitch, velocity));
    }

    /// <summary>
    /// 区間内のベースの発音位置ごとに和音を鳴らす。区間頭に発音がなければ区間頭からも鳴らす。
    /// </summary>
    private static void AddFollowingBass(List<Note> result, int[] voicing, ChordSegment segment,
        IReadOnlyList<Note> bass, int velocity)
    {
        var onsets = bass.Select(n => n.Tick).Where(t => t > segment.Start && t < segment.End)
            .Prepend(segment.Start).Distinct().Order().ToList();
        for (var i = 0; i < onsets.Count; i++)
        {
            var end = i + 1 < onsets.Count ? onsets[i + 1] : segment.End;
            AddChord(result, voicing, onsets[i], end - onsets[i], velocity);
        }
    }

    /// <summary>
    /// 構成音＋オクターブ上の根音を下から順に繰り返す。
    /// </summary>
    private static void AddArpeggio(List<Note> result, int[] voicing, ChordSegment segment, int division, int velocity)
    {
        int[] sequence = [.. voicing, voicing[0] + 12];
        var step = 0;
        for (var t = segment.Start; t < segment.End; t += division)
        {
            result.Add(new Note(t, Math.Min(division, segment.End - t), sequence[step % sequence.Length], velocity));
            step++;
        }
    }
}
