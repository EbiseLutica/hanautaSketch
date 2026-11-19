using HanautaSketch.Engine.Transcription;

namespace HanautaSketch.Tests.Transcription;

public class NoteSegmenterTests
{
    private const double Hop = 0.01;

    /// <summary>(フレーム数, 音高, 音量) の並びからフレーム列を作る</summary>
    private static List<PitchFrame> Frames(params (int Count, double? Midi, double Rms)[] parts)
    {
        var frames = new List<PitchFrame>();
        foreach (var (count, midi, rms) in parts)
        {
            for (var i = 0; i < count; i++) frames.Add(new PitchFrame(frames.Count * Hop, midi, rms, 0.05));
        }
        return frames;
    }

    [Fact]
    public void SplitsOnSilence()
    {
        var notes = NoteSegmenter.Segment(Frames((20, 60, 0.2), (10, null, 0.001), (20, 62, 0.2)));

        Assert.Equal(2, notes.Count);
        Assert.Equal(60, notes[0].Midi, 1);
        Assert.Equal(62, notes[1].Midi, 1);
        Assert.Equal(0.30, notes[1].Start, 2);
    }

    [Fact]
    public void SplitsOnPitchChangeWithoutGap()
    {
        // レガートで音高だけ変わる
        var notes = NoteSegmenter.Segment(Frames((20, 60, 0.2), (20, 64, 0.2)));

        Assert.Equal(2, notes.Count);
        Assert.Equal(64, notes[1].Midi, 1);
        Assert.Equal(0.20, notes[1].Start, 2);
    }

    [Fact]
    public void IgnoresShortPitchWobble()
    {
        // 2 フレームだけの揺れでは区切らない
        var notes = NoteSegmenter.Segment(Frames((20, 60, 0.2), (2, 61, 0.2), (20, 60, 0.2)));

        Assert.Single(notes);
    }

    [Fact]
    public void FixesOctaveErrors()
    {
        // 1 フレームだけオクターブ下に誤検出しても、1 つの音のまま
        var notes = NoteSegmenter.Segment(Frames((20, 60, 0.2), (1, 48, 0.2), (20, 60, 0.2)));

        Assert.Single(notes);
        Assert.Equal(60, notes[0].Midi, 1);
    }

    [Fact]
    public void SplitsRepeatedPitchOnVolumeDip()
    {
        // 同じ音を歌い直すと、間に音量の谷ができる
        var notes = NoteSegmenter.Segment(Frames((20, 60, 0.2), (2, 60, 0.05), (20, 60, 0.2)));

        Assert.Equal(2, notes.Count);
    }

    [Fact]
    public void BridgesShortUnvoicedGap()
    {
        // 1 フレームの無声（子音など）はつなげる。音量も下がらないので 1 つの音
        var notes = NoteSegmenter.Segment(Frames((20, 60, 0.2), (1, null, 0.2), (20, 60, 0.2)));

        Assert.Single(notes);
    }

    [Fact]
    public void DropsTooShortNotes()
    {
        var notes = NoteSegmenter.Segment(Frames((3, 60, 0.2), (10, null, 0.001), (20, 62, 0.2)));

        Assert.Single(notes);
        Assert.Equal(62, notes[0].Midi, 1);
    }
}
