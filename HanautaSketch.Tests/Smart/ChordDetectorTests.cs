using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Tests.Smart;

public class ChordDetectorTests
{
    private const int Ppq = 480;
    private const int Bar = Ppq * 4;

    private static readonly ChordDetectionContext CMajor = new(new Key(0, Mode.Major), Ppq, Bar);
    private static readonly ChordDetectionContext AMinor = new(new Key(9, Mode.Minor), Ppq, Bar);

    /// <summary>1小節ずつ全音符のベースを並べる</summary>
    private static List<Note> WholeNoteBass(params int[] pitches) =>
        pitches.Select((p, i) => new Note(i * Bar, Bar, p, 100)).ToList();

    private static string[] Names(IReadOnlyList<ChordSegment> segments, ChordDetectionContext? context = null) =>
        segments.Select(s => s.GetName((context ?? CMajor).Key)).ToArray();

    private static IReadOnlyList<ChordSegment> Detect(List<Note> bass, ChordDetectionContext context,
        SmartChordSettings? settings = null, List<Note>? melody = null, List<ChordOverride>? overrides = null) =>
        ChordDetector.Detect(bass, melody ?? [], bass.Max(n => n.EndTick), context, settings ?? new(),
            overrides ?? []);

    [Fact]
    public void RootBass_IsDetectedAsDiatonicTriads()
    {
        var result = Detect(WholeNoteBass(48, 53, 55, 48), CMajor);
        Assert.Equal(["C", "F", "G", "C"], Names(result));
    }

    [Fact]
    public void ShortBassNotes_AreNotOverriddenByTransitions()
    {
        // 小節頭に8分音符を1つずつ: F C D F
        int[] pitches = [53, 60, 62, 53];
        var bass = pitches.Select((p, i) => new Note(i * Bar, Ppq / 2, p, 100)).ToList();
        var result = ChordDetector.Detect(bass, [], Bar * 4, CMajor, new(), []);

        Assert.Equal(["F", "C", "Dm", "F"], Names(result));
    }

    [Fact]
    public void TwoChordsPerBar_AreSplitAtHalfBar()
    {
        // C minor: Ab G | Cm Eb（1小節グリッドだと Gm Cm になっていたケース）
        const int s = Ppq / 4;
        (int Pos, int Len, int Pitch)[] raw =
        [
            (0, 2, 44), (2, 2, 51), (4, 2, 56), (6, 2, 44), (8, 2, 43), (10, 2, 50), (12, 1, 55), (13, 1, 50), (14, 2, 43),
            (16, 2, 36), (18, 2, 48), (20, 1, 36), (21, 1, 43), (22, 2, 48), (24, 2, 39), (26, 2, 46), (28, 1, 51),
            (29, 1, 55), (30, 2, 58),
        ];
        var bass = raw.Select(r => new Note(r.Pos * s, r.Len * s, r.Pitch, 100)).ToList();
        var cMinor = new ChordDetectionContext(new Key(0, Mode.Minor), Ppq, Bar);

        var result = ChordDetector.Detect(bass, [], Bar * 2, cMinor, new(), []);
        Assert.Equal(["Ab", "Gm", "Cm", "Eb"], Names(result, cMinor));
        Assert.Equal([0, Bar / 2, Bar, Bar * 3 / 2], result.Select(r => r.Start).ToArray());
    }

    [Fact]
    public void SyncopatedBass_SplitsAtAnticipatedOnsets()
    {
        // C minor。前小節後半は D のオクターブ、次の小節は 3+3+2（16分単位）で C → Eb → G、残りは G 周辺
        const int s = Ppq / 4;
        (int Pos, int Len, int Pitch)[] raw =
        [
            (8, 2, 38), (10, 2, 50), (12, 2, 38), (14, 2, 50),
            (16, 2, 36), (19, 1, 39), (22, 4, 43), (26, 2, 55), (28, 1, 43), (29, 1, 38), (30, 2, 31),
        ];
        var bass = raw.Select(r => new Note(r.Pos * s, r.Len * s, r.Pitch, 100)).ToList();
        var cMinor = new ChordDetectionContext(new Key(0, Mode.Minor), Ppq, Bar);

        var result = ChordDetector.Detect(bass, [], Bar * 2, cMinor, new(), []);
        var secondBar = result.Where(r => r.Start >= Bar).ToList();

        Assert.Equal(["Cm", "Eb", "Gm"], Names(secondBar, cMinor));
        Assert.Equal([Bar, Bar + 3 * s, Bar + 6 * s], secondBar.Select(r => r.Start).ToArray());
    }

    [Fact]
    public void RepeatedBassNote_IsTreatedAsRoot()
    {
        // 実機のケース（C minor、メロディあり）。2小節目前半は D を繰り返して Bb で G へ下りる → Ddim | Gm
        const int s = Ppq / 4;
        (int Pos, int Len, int Pitch)[] bassRaw =
        [
            (0, 2, 48), (3, 1, 51), (6, 7, 55), (13, 1, 50), (14, 2, 43),
            (16, 2, 38), (18, 2, 50), (20, 2, 38), (22, 2, 34), (24, 2, 31), (26, 2, 43), (28, 2, 31), (30, 2, 43),
            (32, 2, 36), (34, 2, 48), (36, 2, 36), (38, 2, 48), (40, 2, 39), (42, 2, 51), (44, 2, 39), (46, 2, 51),
        ];
        (int Pos, int Len, int Pitch)[] melodyRaw =
        [
            (0, 1, 60), (2, 1, 62), (3, 1, 63), (5, 1, 68), (6, 4, 67), (12, 2, 67), (14, 2, 70),
            (16, 1, 72), (18, 1, 72), (20, 1, 72), (22, 1, 70), (23, 1, 67), (26, 2, 67), (28, 2, 65), (30, 2, 63),
            (32, 2, 65), (34, 2, 67), (36, 2, 68), (38, 2, 60),
        ];
        var bass = bassRaw.Select(r => new Note(r.Pos * s, r.Len * s, r.Pitch, 100)).ToList();
        var melody = melodyRaw.Select(r => new Note(r.Pos * s, r.Len * s, r.Pitch, 100)).ToList();
        var cMinor = new ChordDetectionContext(new Key(0, Mode.Minor), Ppq, Bar);

        var result = ChordDetector.Detect(bass, melody, Bar * 3, cMinor, new(), []);
        var fromSecondBar = result.Where(r => r.Start >= Bar).ToList();

        Assert.Equal(["Ddim", "Gm", "Cm", "Eb"], Names(fromSecondBar, cMinor));
        Assert.Equal([Bar, Bar * 3 / 2, Bar * 2, Bar * 5 / 2], fromSecondBar.Select(r => r.Start).ToArray());
    }

    [Fact]
    public void WalkingBass_IsNotSplitOnWeakBeats()
    {
        // C E G A の4分音符。半小節での C | G は許容するが、2・4拍目では割らない
        int[] pitches = [48, 52, 55, 57];
        var bass = pitches.Select((p, i) => new Note(i * Ppq, Ppq, p, 100)).ToList();
        var result = Detect(bass, CMajor);

        Assert.Equal("C", result[0].Chord.ToString());
        Assert.All(result, r => Assert.Contains(r.Start, new[] { 0, Bar / 2 }));
    }

    [Fact]
    public void SustainedBass_IsNotSplitInside()
    {
        // 全音符の C の上で、後半だけメロディが F A → ベースの持続をまたいで割らない
        List<Note> bass = [new(0, Bar, 48, 100)];
        List<Note> melody = [new(Bar / 2, Ppq, 65, 100), new(Bar / 2 + Ppq, Ppq, 69, 100)];
        Assert.Equal(["C"], Names(Detect(bass, CMajor, melody: melody)));
    }

    [Fact]
    public void RootFifthBassPattern_DetectsRootChord()
    {
        // C G C G / F C F C
        var bass = new List<Note>();
        int[] pattern = [48, 55, 48, 55, 41, 48, 41, 48];
        for (var i = 0; i < pattern.Length; i++)
            bass.Add(new Note(i * Ppq, Ppq, pattern[i], 100));

        Assert.Equal(["C", "F"], Names(Detect(bass, CMajor)));
    }

    [Fact]
    public void MinorKey_DefaultsToMinorDominant()
    {
        var result = Detect(WholeNoteBass(45, 50, 52, 45), AMinor);
        Assert.Equal(["Am", "Dm", "Em", "Am"], Names(result, AMinor));
    }

    [Fact]
    public void MinorKey_MajorSetting_UsesMajorDominant()
    {
        var result = Detect(WholeNoteBass(45, 50, 52, 45), AMinor,
            new SmartChordSettings { MinorDominant = MinorDominantMode.Major });
        Assert.Equal(["Am", "Dm", "E", "Am"], Names(result, AMinor));
    }

    [Fact]
    public void MinorKey_Auto_UsesMajorDominantOnlyWithLeadingTone()
    {
        var auto = new SmartChordSettings { MinorDominant = MinorDominantMode.Auto };
        var bass = WholeNoteBass(45, 52, 45, 52);

        // 2小節目のメロディに G#、4小節目に G
        List<Note> melody = [new(Bar, Ppq, 68, 100), new(Bar * 3, Ppq, 67, 100)];

        Assert.Equal(["Am", "E", "Am", "Em"], Names(Detect(bass, AMinor, auto, melody), AMinor));
    }

    [Fact]
    public void Override_IsKeptAndMarked()
    {
        var bass = WholeNoteBass(48, 53, 55, 48);
        var dm = new Chord(2, ChordQuality.Minor);
        var result = Detect(bass, CMajor, overrides: [new ChordOverride(Bar, Bar, dm)]);

        Assert.Equal(["C", "Dm", "G", "C"], Names(result));
        Assert.True(result[1].IsOverride);
        Assert.False(result[0].IsOverride);
    }

    [Fact]
    public void NoChordOverride_IsSilentAndRestIsDetected()
    {
        var bass = WholeNoteBass(48, 53, 55, 48);
        var result = Detect(bass, CMajor, overrides: [new ChordOverride(0, Bar, null)]);

        Assert.Equal(["N.C.", "F", "G", "C"], Names(result));
        Assert.Null(result[0].Chord);
        Assert.True(result[0].IsOverride);

        var notes = ChordAccompaniment.Generate(result, bass, new AccompanimentSettings(), Ppq);
        Assert.All(notes, n => Assert.True(n.Tick >= Bar));
    }

    [Fact]
    public void Override_WithArbitraryRange_SplitsGrid()
    {
        var bass = WholeNoteBass(48, 48);
        var f = new Chord(5, ChordQuality.Major);
        var result = Detect(bass, CMajor, overrides: [new ChordOverride(Ppq * 2, Ppq * 4, f)]);

        Assert.Equal(
            [(0, Ppq * 2), (Ppq * 2, Ppq * 4), (Ppq * 6, Ppq * 2)],
            result.Select(s => (s.Start, s.Length)).ToArray());
        Assert.Equal(["C", "F", "C"], Names(result));
    }

    [Fact]
    public void EmptySegment_InheritsPreviousChord()
    {
        List<Note> bass = [new(0, Bar, 48, 100), new(Bar * 2, Bar, 55, 100)];
        var result = Detect(bass, CMajor);
        Assert.Equal(["C", "G"], Names(result));
        Assert.Equal(Bar * 2, result[0].Length);
    }

    [Fact]
    public void ThirdInBassAtSegmentHead_IsInversion()
    {
        // 小節頭 E、残りは C が長く鳴る → C/E
        List<Note> bass = [new(0, Ppq, 52, 100), new(Ppq, Ppq * 3, 48, 100)];
        Assert.Equal(["C/E"], Names(Detect(bass, CMajor)));
    }

    [Theory]
    [InlineData(0, Mode.Minor, 8, "Ab")]
    [InlineData(0, Mode.Major, 8, "G#")]
    [InlineData(5, Mode.Major, 10, "Bb")]
    [InlineData(9, Mode.Minor, 1, "C#")]
    public void PitchName_FollowsKeySignature(int tonic, Mode mode, int pc, string expected)
    {
        Assert.Equal(expected, new Key(tonic, mode).GetPitchName(pc));
    }

    [Fact]
    public void SuggestNext_PrefersResolutionFromDominant()
    {
        var key = new Key(0, Mode.Major);
        var suggestions = ChordTransitions.SuggestNext(key, new Chord(7, ChordQuality.Major), MinorDominantMode.Minor);
        Assert.Equal("C", suggestions[0].ToString());
    }
}
