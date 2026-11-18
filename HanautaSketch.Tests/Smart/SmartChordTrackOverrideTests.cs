using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Tests.Smart;

/// <summary>
/// スマートコードトラックの固定区間（<see cref="ChordOverride"/>）の追加・伸縮・移動を確かめるテスト。
/// 時間は tick で書く。PPQ 480 で 4/4 なので、1拍 = 480 tick、1小節 = 1920 tick。
/// 区間の比較は (開始 tick, 長さ, コード) の並びで行い、並びは開始位置の昇順になる。
/// </summary>
public class SmartChordTrackOverrideTests
{
    /// <summary>テスト用のコード。区間ごとに違うコードにして、どの区間か見分けられるようにする</summary>
    private static readonly Chord A = new(9, ChordQuality.Minor);
    private static readonly Chord B = new(11, ChordQuality.Major);
    private static readonly Chord C = new(0, ChordQuality.Major);

    /// <summary>
    /// 既存の固定区間の内側に新しい区間を置くと、既存の区間は前後の2つに分かれることを確かめる。
    /// </summary>
    [Fact]
    public void SetOverride_InsideExisting_SplitsIt()
    {
        var track = new SmartChordTrack();
        track.SetOverride(0, 1920, A);
        track.SetOverride(480, 480, B);

        Assert.Equal([(0, 480, A), (480, 480, B), (960, 960, A)], Summarize(track));
    }

    /// <summary>
    /// 既存の固定区間を完全に覆う区間を置くと、覆われた固定区間は消えることを確かめる。
    /// </summary>
    [Fact]
    public void SetOverride_CoveringExisting_RemovesIt()
    {
        var track = new SmartChordTrack();
        track.SetOverride(480, 480, A);
        track.SetOverride(0, 1920, B);

        Assert.Equal([(0, 1920, B)], Summarize(track));
    }

    /// <summary>
    /// 固定区間の右端を隣の区間の中へ伸ばすと、伸ばした側は新しい長さになり、
    /// 隣の区間は伸ばされた分だけ短くなることを確かめる。戻り値も伸縮後の区間であることを確かめる。
    /// </summary>
    [Fact]
    public void ResizeOverride_ExtendsIntoNeighbor_TruncatesNeighbor()
    {
        var track = new SmartChordTrack();
        track.SetOverride(0, 960, A);
        track.SetOverride(960, 960, B);

        var resized = track.ResizeOverride(track.Overrides[0], 0, 1440);

        Assert.Equal(new ChordOverride(0, 1440, A), resized);
        Assert.Equal([(0, 1440, A), (1440, 480, B)], Summarize(track));
    }

    /// <summary>
    /// 伸縮後の範囲が空（開始と終了が同じ）になる場合は、何もせず null を返すことを確かめる。
    /// 固定区間の一覧も変わらないことを確かめる。
    /// </summary>
    [Fact]
    public void ResizeOverride_ToEmptyRange_ReturnsNullAndKeepsState()
    {
        var track = new SmartChordTrack();
        track.SetOverride(0, 960, A);

        Assert.Null(track.ResizeOverride(track.Overrides[0], 480, 480));
        Assert.Equal([(0, 960, A)], Summarize(track));
    }

    /// <summary>
    /// 固定区間を移動しても長さは変わらず、戻り値が移動後の区間であることを確かめる。
    /// 移動先に他の区間がない場合、他の区間には影響しないことも確かめる。
    /// </summary>
    [Fact]
    public void MoveOverride_KeepsLength()
    {
        var track = new SmartChordTrack();
        track.SetOverride(0, 480, A);
        track.SetOverride(960, 480, B);

        var moved = track.MoveOverride(track.Overrides[0], 1920);

        Assert.Equal(new ChordOverride(1920, 480, A), moved);
        Assert.Equal([(960, 480, B), (1920, 480, A)], Summarize(track));
    }

    /// <summary>
    /// 固定区間を別の区間の内側へ移動すると、移動先の区間は前後の2つに分かれ、
    /// 移動した区間はその間に入ることを確かめる。
    /// </summary>
    [Fact]
    public void MoveOverride_IntoNeighbor_SplitsNeighbor()
    {
        var track = new SmartChordTrack();
        track.SetOverride(0, 480, A);
        track.SetOverride(960, 960, B);

        track.MoveOverride(track.Overrides[0], 1200);

        Assert.Equal([(960, 240, B), (1200, 480, A), (1680, 240, B)], Summarize(track));
    }

    /// <summary>
    /// 固定区間を伸ばした後に判別し直すと、判別結果の区間が伸縮後の範囲どおりになることを確かめる。
    /// 固定区間の外は判別されない（ベースもメロディもないので、固定区間の範囲だけが結果になる）。
    /// </summary>
    [Fact]
    public void Regenerate_UsesResizedRange()
    {
        var song = new Song();
        var track = new SmartChordTrack();
        song.Tracks.Add(track);
        track.SetOverride(0, 960, A);
        track.SetOverride(960, 960, B);
        track.ResizeOverride(track.Overrides[0], 0, 1440);

        track.Regenerate(song);

        var segments = track.Chords.Select(c => (c.Start, c.Length, c.Chord)).ToList();
        Assert.Equal([(0, 1440, (Chord?)A), (1440, 480, (Chord?)B)], segments);
    }

    /// <summary>
    /// 固定区間の一覧を、テストで比べやすい (開始, 長さ, コード) の並びにする。
    /// コードは固定区間に入っているものだけを使う（N.C. は含まない）。
    /// </summary>
    private static List<(int Start, int Length, Chord Chord)> Summarize(SmartChordTrack track) =>
        track.Overrides.Select(o => (o.Start, o.Length, o.Chord!.Value)).ToList();
}
