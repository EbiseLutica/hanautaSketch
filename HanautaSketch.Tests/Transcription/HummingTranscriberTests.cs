using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.IO;
using HanautaSketch.Engine.Transcription;
using static HanautaSketch.Tests.Transcription.SyntheticVoice;

namespace HanautaSketch.Tests.Transcription;

public class HummingTranscriberTests
{
    private static Conductor CreateConductor(double bpm = 120)
    {
        var conductor = new Conductor();
        conductor.Reset(bpm, 4, new Key(0, Mode.Major));
        return conductor;
    }

    [Fact]
    public void TranscribesSimpleMelody()
    {
        // 120 BPM で 4 分音符ずつ、C D E E G。全体に +30 セントずれ、各音の終わりに少し息継ぎする
        const int rate = 44100;
        double[] melody = [60, 62, 64, 64, 67];
        var segments = melody
            .SelectMany(m => new[] { new Segment(0.44, m + 0.3), new Segment(0.06, null) })
            .ToArray();
        var clip = new AudioClip(Render(rate, segments), rate);

        var notes = HummingTranscriber.Transcribe(clip, CreateConductor(), new TranscriptionOptions(), 0, 0, 480 * 8);

        Assert.Equal([60, 62, 64, 64, 67], notes.Select(n => n.Pitch));
        Assert.Equal([0, 480, 960, 1440, 1920], notes.Select(n => n.Tick));
        Assert.All(notes, n => Assert.InRange(n.Length, 360, 480));
    }

    [Fact]
    public void AppliesClipStartAndLatency()
    {
        // 録音の先頭は曲の 1 秒前（カウントイン）。音声上 1.6 秒の音は、遅れ 0.1 秒を引いて曲の 0.5 秒 = 480 tick
        var sung = new[] { new SungNote(1.6, 2.1, 60) };
        var options = new TranscriptionOptions { LatencySeconds = 0.1 };

        var notes = HummingTranscriber.Quantize(sung, CreateConductor(), options, -1.0, 0, 480 * 8);

        Assert.Equal(new Note(480, 480, 60, 100), Assert.Single(notes));
    }

    [Fact]
    public void QuantizesToGridAndFollowsTempo()
    {
        // 60 BPM なので 1 秒 = 480 tick。8 分グリッドで 0.2 秒 → 96 tick → 0、1.13 秒 → 542 tick → 480 に丸める
        var sung = new[] { new SungNote(0.2, 0.9, 62), new SungNote(1.13, 1.4, 64) };
        var options = new TranscriptionOptions { GridTicks = 240 };

        var notes = HummingTranscriber.Quantize(sung, CreateConductor(60), options, 0, 0, 480 * 8);

        Assert.Equal([new Note(0, 480, 62, 100), new Note(480, 240, 64, 100)], notes);
    }

    [Fact]
    public void ResolvesCollisionsAndOverlaps()
    {
        // 同じグリッドに吸着した 2 音は長い方を残し、次の音と重なる分は切り詰める
        var sung = new[]
        {
            new SungNote(0.00, 0.05, 60),
            new SungNote(0.02, 0.60, 62),
            new SungNote(0.50, 1.00, 64),
        };

        var notes = HummingTranscriber.Quantize(sung, CreateConductor(), new TranscriptionOptions(), 0, 0, 480 * 8);

        Assert.Equal([new Note(0, 480, 62, 100), new Note(480, 480, 64, 100)], notes);
    }

    [Fact]
    public void KeepsOnlyNotesInsideRange()
    {
        // 範囲の前に始まる音は捨て、範囲の終わりではみ出す分は切る
        var sung = new[] { new SungNote(0.0, 0.4, 60), new SungNote(0.5, 1.5, 62) };

        var notes = HummingTranscriber.Quantize(sung, CreateConductor(), new TranscriptionOptions(), 0, 240, 960);

        Assert.Equal(new Note(480, 480, 62, 100), Assert.Single(notes));
    }

    [Fact]
    public void UsesKeyAtNotePosition()
    {
        // 途中で E♭ メジャーに転調。E♭ と E の境界付近の音は、C メジャーでは E、E♭ メジャーでは E♭ に倒れる
        // （後ろの 2 音は長く正確に歌っており、チューニングの推定を 0 に保つためのもの）
        var conductor = CreateConductor();
        conductor.SetKey(960, new Key(3, Mode.Major));
        var sung = new[]
        {
            new SungNote(0.0, 0.4, 63.3),
            new SungNote(1.0, 1.4, 63.7),
            new SungNote(2.0, 3.0, 60),
            new SungNote(3.0, 3.9, 62),
        };

        var notes = HummingTranscriber.Quantize(sung, conductor, new TranscriptionOptions(), 0, 0, 480 * 8);

        Assert.Equal([64, 63, 60, 62], notes.Select(n => n.Pitch));
    }

    [Fact]
    public void ReplaceRangeKeepsOutsideParts()
    {
        List<Note> notes =
        [
            new(0, 480, 60, 100),     // 範囲の前：そのまま
            new(240, 960, 62, 100),   // 範囲にまたがる：前後に分かれる
            new(600, 120, 64, 100),   // 範囲内：消える
            new(1200, 480, 65, 100),  // 範囲の後ろ：そのまま
        ];

        HummingTranscriber.ReplaceRange(notes, 480, 960, [new Note(480, 240, 67, 90)]);

        Assert.Equal(
            [
                new Note(0, 480, 60, 100),
                new Note(240, 240, 62, 100),
                new Note(480, 240, 67, 90),
                new Note(960, 240, 62, 100),
                new Note(1200, 480, 65, 100),
            ],
            notes);
    }
}
