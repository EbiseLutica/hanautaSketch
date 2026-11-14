namespace HanautaSketch.Engine.Core;

/// <summary>
/// 曲全体。テンポ・拍子・キーの変化は <see cref="Conductor"/> が持つ。
/// </summary>
public sealed class Song
{
    public const int DefaultPpq = 480;

    private Conductor? _conductor;

    /// <summary>4分音符あたりのtick数</summary>
    public int Ppq { get; init; } = DefaultPpq;

    /// <summary>テンポ・拍子・キーの変化</summary>
    public Conductor Conductor => _conductor ??= new Conductor(Ppq);

    public List<Track> Tracks { get; } = [];

    /// <summary>
    /// 再生時に鳴らすトラックか。ミュートを除き、ソロがあればソロのトラックだけ。
    /// </summary>
    public bool IsAudible(Track track) => !track.Mute && (track.Solo || !Tracks.Any(t => t.Solo));

    /// <summary>
    /// ノートトラックの最後の音の終わりを含む小節の、次の小節頭（曲の長さの目安）。
    /// </summary>
    public int EndBarTick(int minimumTick = 0)
    {
        var last = Tracks.OfType<NoteTrack>().SelectMany(t => t.Notes).Select(n => n.EndTick)
            .Append(minimumTick).Max();
        return Conductor.CeilToBar(last);
    }
}
