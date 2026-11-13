using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.Smart.Chords;

/// <summary>
/// 伴奏パターンの種類。
/// </summary>
public enum AccompanimentPattern
{
    /// <summary>コード区間いっぱいに和音を伸ばす</summary>
    Sustain,

    /// <summary>一定間隔で和音を刻む</summary>
    Beat,

    /// <summary>ベースの発音タイミングで和音を鳴らす</summary>
    FollowBass,

    /// <summary>構成音を下から順に鳴らす</summary>
    Arpeggio,
}

/// <summary>
/// 伴奏の鳴らし方の設定。
/// </summary>
public sealed record AccompanimentSettings
{
    public AccompanimentPattern Pattern { get; init; } = AccompanimentPattern.Sustain;

    /// <summary>ビート刻み・アルペジオの間隔（tick）。0 以下なら8分音符</summary>
    public int DivisionTicks { get; init; }

    public int Velocity { get; init; } = 80;
}

/// <summary>
/// ベース（任意でメロディ）からコード進行を判別し、伴奏を生成するスマートトラック。
/// 生成結果は派生データとして持ち、保存しない。ソースや設定が変わったら <see cref="Regenerate"/> を呼ぶ。
/// </summary>
public sealed class SmartChordTrack : Track
{
    private IReadOnlyList<Note> _generatedNotes = [];

    /// <summary>コード判別の主なソース</summary>
    public NoteTrack? BassSource { get; set; }

    /// <summary>判別のヒントに使うメロディ。null なら使わない</summary>
    public NoteTrack? MelodySource { get; set; }

    public SmartChordSettings ChordSettings { get; set; } = new();

    public AccompanimentSettings Accompaniment { get; set; } = new();

    /// <summary>ユーザーが手動で指定したコード区間</summary>
    public List<ChordOverride> Overrides { get; } = [];

    /// <summary>直近の判別結果</summary>
    public IReadOnlyList<ChordSegment> Chords { get; private set; } = [];

    public override IReadOnlyList<Note> PlaybackNotes => _generatedNotes;

    /// <summary>
    /// 曲の現在の内容からコード進行を判別し直し、伴奏を生成する。
    /// </summary>
    public void Regenerate(Song song)
    {
        var bass = BassSource?.Notes ?? [];
        var melody = MelodySource?.Notes ?? [];
        var lastTick = song.Tracks.OfType<NoteTrack>().SelectMany(t => t.Notes).Select(n => n.EndTick)
            .Concat(Overrides.Select(o => o.End))
            .DefaultIfEmpty(0).Max();
        var endTick = (lastTick + song.TicksPerBar - 1) / song.TicksPerBar * song.TicksPerBar;

        var context = new ChordDetectionContext(song.Key, song.Ppq, song.TicksPerBar);
        Chords = ChordDetector.Detect(bass, melody, endTick, context, ChordSettings, Overrides);
        _generatedNotes = ChordAccompaniment.Generate(Chords, bass, Accompaniment, song.Ppq);
    }

    /// <summary>
    /// 指定区間をコードで上書きする。null なら N.C.（鳴らさない）にする。重なる既存の override は取り除く。
    /// </summary>
    public void SetOverride(int start, int length, Chord? chord)
    {
        ClearOverrides(start, length);
        Overrides.Add(new ChordOverride(start, length, chord));
        Overrides.Sort((a, b) => a.Start.CompareTo(b.Start));
    }

    /// <summary>
    /// 指定区間に重なる override を取り除き、自動判別に戻す。
    /// </summary>
    public void ClearOverrides(int start, int length) =>
        Overrides.RemoveAll(o => o.Start < start + length && start < o.End);
}
