using System.Text.Json;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Transcription;

namespace HanautaSketch.Tests.Transcription;

/// <summary>
/// 実際の鼻歌の録音を区切った結果（Fixtures/humming-*.json）から、歌おうとしたメロディにどこまで近づくかを確かめる。
/// 音声は入れず、NoteSegmenter の出力（区切りと音高）だけを持つ。
/// </summary>
public class HummingFixtureTests
{
    private sealed record SungData(double Start, double End, double Midi);

    private sealed record ExpectedData(int Tick, int Pitch);

    private sealed record Fixture(
        double Bpm, int Tonic, string Mode, double ClipStartSeconds, double LatencySeconds, int GridTicks,
        int RangeStartTick, int RangeEndTick, SungData[] Sung, ExpectedData[] Expected);

    private static Fixture Load(string file)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        return JsonSerializer.Deserialize<Fixture>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static IReadOnlyList<Note> Transcribe(Fixture fixture, PitchSnapMode mode)
    {
        var conductor = new Conductor();
        conductor.Reset(fixture.Bpm, 4, new Key(fixture.Tonic, fixture.Mode == "minor" ? Mode.Minor : Mode.Major));
        var options = new TranscriptionOptions
        {
            GridTicks = fixture.GridTicks,
            LatencySeconds = fixture.LatencySeconds,
            SnapMode = mode,
        };
        var sung = fixture.Sung.Select(s => new SungNote(s.Start, s.End, s.Midi)).ToList();
        return HummingTranscriber.Quantize(sung, conductor, options, fixture.ClipStartSeconds, fixture.RangeStartTick, fixture.RangeEndTick);
    }

    [Theory]
    // 音程が音ごとにバラバラにずれているので、全体のずれは補正しない。外れるのは、本来の音から 0.8 半音以上離れた音だけ
    [InlineData(PitchSnapMode.Lean, 15)]
    [InlineData(PitchSnapMode.Scale, 16)]
    public void TakeOne(PitchSnapMode mode, int minimumCorrect)
    {
        var fixture = Load("humming-take1.json");

        var notes = Transcribe(fixture, mode);

        // リズムはすべて合う
        Assert.Equal(fixture.Expected.Select(e => e.Tick), notes.Select(n => n.Tick));
        var correct = notes.Zip(fixture.Expected).Count(p => p.First.Pitch == p.Second.Pitch);
        Assert.True(correct >= minimumCorrect, $"{correct}/{notes.Count} correct: {string.Join(" ", notes.Select(n => n.Pitch))}");
    }

    [Fact]
    public void InconsistentDetuneIsNotCorrected()
    {
        // 端数の向きがバラけている録音では、全体のずれを 0 とみなす
        var fixture = Load("humming-take1.json");
        var sung = fixture.Sung.Select(s => new SungNote(s.Start, s.End, s.Midi)).ToList();

        Assert.Equal(0, PitchSnapper.EstimateTuningOffset(sung));
    }
}
