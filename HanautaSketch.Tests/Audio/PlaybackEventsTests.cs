using HanautaSketch.Engine.Audio;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.Tests.Audio;

/// <summary>
/// 再生する MIDI メッセージ列（PlaybackEngine.BuildEvents）を、SF2 なしで確かめる。
/// </summary>
public class PlaybackEventsTests
{
    private const int Rate = 48000;
    private const int NoteOn = 0x90;

    /// <summary>120 BPM・4 拍子で、メロディ（0ch）とベース（1ch）が 1 音ずつある曲</summary>
    private static (Song Song, NoteTrack Melody, NoteTrack Bass) CreateSong()
    {
        var song = new Song();
        song.Conductor.Reset(120, 4, new Key(0, Mode.Major));
        var melody = new NoteTrack { Program = 0 };
        melody.Notes.Add(new Note(1920, 480, 72, 100));
        var bass = new NoteTrack { Program = 33 };
        bass.Notes.Add(new Note(1920, 960, 36, 100));
        song.Tracks.Add(melody);
        song.Tracks.Add(bass);
        return (song, melody, bass);
    }

    [Fact]
    public void PlainPlaybackHasNoPreRollOrClicks()
    {
        var (song, _, _) = CreateSong();

        var events = PlaybackEngine.BuildEvents(song, 1920, new PlaybackOptions(), Rate);

        Assert.Equal(0, events.Min(e => e.Sample));
        Assert.DoesNotContain(events, e => e.Force);
        Assert.Equal([72, 36], events.Where(e => e.Command == NoteOn).Select(e => e.Data1));
    }

    [Fact]
    public void CountInStartsBeforeStartWithSetupFirst()
    {
        // 4 拍のカウントインは 2 秒前から。音色・音量の設定はカウントインより前に届く
        var (song, _, _) = CreateSong();

        var events = PlaybackEngine.BuildEvents(song, 1920, new PlaybackOptions { CountInBeats = 4 }, Rate);

        Assert.Equal(-2 * Rate, events[0].Sample);
        var firstClick = events.FindIndex(e => e.Force && e.Command == NoteOn);
        Assert.All(events.Where(e => e.Command is 0xC0 or 0xB0), e => Assert.Equal(-2 * Rate, e.Sample));
        Assert.True(events.FindLastIndex(e => e.Command is 0xC0 or 0xB0) < firstClick);
        Assert.Equal(
            [MetronomeClicks.AccentPitch, MetronomeClicks.BeatPitch, MetronomeClicks.BeatPitch, MetronomeClicks.BeatPitch],
            events.Where(e => e.Force && e.Command == NoteOn).Select(e => e.Data1));
        Assert.All(events.Where(e => e.Force), e => Assert.Equal(MidiChannels.Drum, e.Channel));
    }

    [Fact]
    public void ExcludedTrackAndAccompanimentOff()
    {
        var (song, melody, _) = CreateSong();

        // 録音先（メロディ）は鳴らさず、ベースは鳴らす
        var excluded = PlaybackEngine.BuildEvents(song, 1920, new PlaybackOptions { ExcludedTrack = melody }, Rate);
        Assert.Equal([36], excluded.Where(e => e.Command == NoteOn).Select(e => e.Data1));

        // 伴奏を鳴らさないときは、クリックだけ
        var clicksOnly = PlaybackEngine.BuildEvents(
            song, 1920, new PlaybackOptions { PlaySong = false, CountInBeats = 4, Metronome = true }, Rate);
        Assert.All(clicksOnly.Where(e => e.Command == NoteOn), e => Assert.True(e.Force));
    }

    [Fact]
    public void ContinuousMetronomeRunsPastSongEnd()
    {
        // 止めるまで鳴らすときは、曲の終わり（3 秒後）を越えてもクリックが続く
        var (song, _, _) = CreateSong();

        var events = PlaybackEngine.BuildEvents(
            song, 1920, new PlaybackOptions { Metronome = true, Continuous = true }, Rate);

        var lastClick = events.Where(e => e.Force && e.Command == NoteOn).Max(e => e.Sample);
        Assert.True(lastClick > 60 * Rate);
    }
}
