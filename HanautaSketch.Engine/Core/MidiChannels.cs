namespace HanautaSketch.Engine.Core;

/// <summary>
/// トラックへの MIDI チャンネル（0〜15）の割り当て。再生と書き出しで同じ規則を使う。
/// ドラムトラックは 10ch（9）、それ以外はトラック順に空いているチャンネルを使う。
/// </summary>
public static class MidiChannels
{
    public const int Drum = 9;
    public const int Count = 16;

    /// <param name="reserved">使わないチャンネル（再生時のプレビュー用など）</param>
    /// <returns>割り当てたトラックとチャンネル。チャンネルが足りなければ、あふれたトラックは最後のチャンネルを共有する</returns>
    public static IEnumerable<(Track Track, int Channel)> Assign(IEnumerable<Track> tracks, params int[] reserved)
    {
        var available = Enumerable.Range(0, Count).Where(c => c != Drum && !reserved.Contains(c)).ToList();
        var next = 0;
        foreach (var track in tracks)
        {
            if (track.IsDrum)
            {
                yield return (track, Drum);
                continue;
            }

            yield return (track, available[Math.Min(next, available.Count - 1)]);
            next++;
        }
    }
}
