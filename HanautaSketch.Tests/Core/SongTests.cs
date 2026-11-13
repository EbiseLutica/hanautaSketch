using HanautaSketch.Engine.Core;

namespace HanautaSketch.Tests.Core;

public class SongTests
{
    [Fact]
    public void IsAudible_HonorsMuteAndSolo()
    {
        var song = new Song();
        var a = new NoteTrack { Name = "A" };
        var b = new NoteTrack { Name = "B", Mute = true };
        var c = new NoteTrack { Name = "C" };
        song.Tracks.Add(a);
        song.Tracks.Add(b);
        song.Tracks.Add(c);

        Assert.Equal([true, false, true], song.Tracks.Select(song.IsAudible).ToArray());

        c.Solo = true;
        Assert.Equal([false, false, true], song.Tracks.Select(song.IsAudible).ToArray());

        // ミュートはソロより優先
        c.Mute = true;
        Assert.Equal([false, false, false], song.Tracks.Select(song.IsAudible).ToArray());
    }

    [Fact]
    public void GeneralMidi_HasNamesForAllPrograms()
    {
        Assert.Equal(128, GeneralMidi.ProgramNames.Count);
        Assert.Equal(16, GeneralMidi.Categories.Count);
        Assert.Equal("Electric Bass (finger)", GeneralMidi.ProgramNames[33]);
        Assert.Equal(4, GeneralMidi.CategoryOf(33));
    }
}
