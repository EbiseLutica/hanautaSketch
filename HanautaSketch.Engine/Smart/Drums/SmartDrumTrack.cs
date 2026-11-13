using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Rhythm;

namespace HanautaSketch.Engine.Smart.Drums;

/// <summary>
/// ドラムの区間。小節単位で並び、区間ごとにパターンとグルーヴを持つ。
/// fix した区間は生成をやめ、トラックの実体ノーツ（<see cref="SmartDrumTrack.FixedNotes"/>）をそのまま鳴らす。
/// </summary>
public sealed record DrumSection
{
    public int Start { get; init; }

    public int Length { get; init; }

    public int End => Start + Length;

    public DrumPattern Pattern { get; init; } = DrumPattern.EightBeat;

    /// <summary>スウィングさせるか（false ならストレート）</summary>
    public bool Swing { get; init; }

    /// <summary>スウィングの強さ（0 = ストレート、1 = 3連符の 2:1）</summary>
    public double SwingAmount { get; init; } = 0.67;

    /// <summary>ベースのリズムへの合わせ方</summary>
    public DrumBassFollow BassFollow { get; init; }

    /// <summary>生成をやめ、実体ノーツを編集できる状態か</summary>
    public bool IsFixed { get; init; }

    public bool Contains(int tick) => Start <= tick && tick < End;
}

/// <summary>
/// 区間ごとの設定からドラムを生成するスマートトラック。GM ドラム（10ch）で鳴らす。
/// 生成結果は派生データとして持ち、保存しない。fix した区間のノーツだけは実体として保存する。
/// </summary>
public sealed class SmartDrumTrack : Track, INoteEditable
{
    private IReadOnlyList<Note> _playbackNotes = [];

    public override bool IsDrum => true;

    /// <summary>「ベースに合わせる」で使うベース</summary>
    public NoteTrack? BassSource { get; set; }

    /// <summary>区間（開始位置の昇順、重なりなし）</summary>
    public List<DrumSection> Sections { get; } = [];

    /// <summary>fix した区間の実体ノーツ</summary>
    public List<Note> FixedNotes { get; } = [];

    public override IReadOnlyList<Note> PlaybackNotes => _playbackNotes;

    List<Note> INoteEditable.EditableNotes => FixedNotes;

    /// <summary>新しいノートは fix した区間の中にだけ置ける</summary>
    public bool CanPlaceAt(int tick) => Sections.Any(s => s.IsFixed && s.Contains(tick));

    /// <summary>
    /// fix していない区間を生成し直し、fix した区間の実体ノーツと合わせて再生用ノーツを作る。
    /// </summary>
    public void Regenerate(Song song)
    {
        var bass = BassSource?.Notes ?? [];
        var generated = Sections.Where(s => !s.IsFixed)
            .SelectMany(s => DrumGenerator.Generate(s, bass, song.Ppq, song.TicksPerBar));
        _playbackNotes = generated.Concat(FixedNotes).OrderBy(n => n.Tick).ToList();
    }

    /// <summary>
    /// [start, end) を新しい区間にする。重なる既存の区間は切り詰め、完全に覆われたものは取り除く。
    /// 新しい区間の設定は、重なっていた最初の区間（なければ既定）を引き継ぐ。
    /// </summary>
    public DrumSection AddSection(int start, int end)
    {
        var template = Sections.FirstOrDefault(s => s.Start < end && start < s.End) ?? new DrumSection();
        var section = template with { Start = start, Length = end - start, IsFixed = false };
        Place(section);
        return section;
    }

    /// <summary>
    /// 区間の範囲を [start, end) に変える。設定と fix 状態は保ち、重なる他の区間は切り詰める。
    /// fix した区間の外に出た実体ノーツは捨てる。
    /// </summary>
    public DrumSection ResizeSection(DrumSection section, int start, int end)
    {
        if (end <= start || !Sections.Remove(section)) return section;

        var resized = section with { Start = start, Length = end - start };
        Place(resized);
        return resized;
    }

    /// <summary>
    /// 区間を置き、重なる既存の区間を切り詰める（完全に覆われたものは取り除く）。
    /// </summary>
    private void Place(DrumSection section)
    {
        var (start, end) = (section.Start, section.End);
        var rest = new List<DrumSection>();
        foreach (var s in Sections)
        {
            if (s.End <= start || end <= s.Start)
            {
                rest.Add(s);
                continue;
            }
            if (s.Start < start) rest.Add(s with { Length = start - s.Start });
            if (end < s.End) rest.Add(s with { Start = end, Length = s.End - end });
        }

        // 切り詰めで fix 区間の外に出た実体ノーツは捨てる
        rest.Add(section);
        Sections.Clear();
        Sections.AddRange(rest.OrderBy(s => s.Start));
        FixedNotes.RemoveAll(n => !Sections.Any(s => s.IsFixed && s.Contains(n.Tick)));
    }

    public void ReplaceSection(DrumSection oldSection, DrumSection newSection)
    {
        var index = Sections.IndexOf(oldSection);
        if (index >= 0) Sections[index] = newSection;
    }

    public void RemoveSection(DrumSection section)
    {
        Sections.Remove(section);
        FixedNotes.RemoveAll(n => section.Contains(n.Tick));
    }

    /// <summary>
    /// 区間を fix する。今の生成結果を実体ノーツとしてコピーし、以降は編集できるようにする。
    /// </summary>
    public DrumSection Fix(DrumSection section, Song song)
    {
        if (section.IsFixed) return section;

        var bass = BassSource?.Notes ?? [];
        FixedNotes.AddRange(DrumGenerator.Generate(section, bass, song.Ppq, song.TicksPerBar));
        var fixedSection = section with { IsFixed = true };
        ReplaceSection(section, fixedSection);
        return fixedSection;
    }

    /// <summary>
    /// fix を解除して生成に戻す。区間内の実体ノーツ（手で加えた編集も含む）は破棄する。
    /// </summary>
    public DrumSection Unfix(DrumSection section)
    {
        if (!section.IsFixed) return section;

        FixedNotes.RemoveAll(n => section.Contains(n.Tick));
        var generatedSection = section with { IsFixed = false };
        ReplaceSection(section, generatedSection);
        return generatedSection;
    }
}

/// <summary>
/// 区間の設定からドラムのノーツを作る。
/// </summary>
public static class DrumGenerator
{
    /// <summary>ドラムのノートの長さ（16分音符）。ドラムは長さに意味がないので短く置く</summary>
    private const int NoteLengthDivisor = 4;

    /// <summary>フィルを入れる間隔（小節）。区間頭から数えてこの小節数ごとの最後の小節がフィル小節</summary>
    private const int FillPeriodBars = 4;

    /// <summary>フィルで使う音。後ろの発音ほど低いタムにする</summary>
    private static readonly int[] FillVoices =
        [GmDrum.Snare, GmDrum.HighTom, GmDrum.HighMidTom, GmDrum.LowMidTom, GmDrum.LowTom, GmDrum.HighFloorTom];

    public static IReadOnlyList<Note> Generate(DrumSection section, IReadOnlyList<Note> bass, int ppq, int ticksPerBar)
    {
        var template = DrumTemplate.For(section.Pattern);
        var stepTicks = ticksPerBar / DrumTemplate.StepsPerBar;
        var noteLength = ppq / NoteLengthDivisor;
        var result = new List<Note>();

        var onsets = BassRhythmAnalyzer.GetOnsets(bass);
        var followFull = section.BassFollow == DrumBassFollow.Full;

        for (var (bar, index) = (section.Start, 0); bar < section.End; bar += ticksPerBar, index++)
        {
            var barEnd = Math.Min(bar + ticksPerBar, section.End);

            // フィル小節では、後半にベースの発音があれば後半をパターンからフィルに替える
            var fillStart = int.MaxValue;
            if (section.BassFollow == DrumBassFollow.Fill && IsFillBar(section, bar, index, ticksPerBar))
            {
                var half = bar + ticksPerBar / 2;
                var fillOnsets = onsets.Where(t => half <= t && t < barEnd).ToList();
                if (fillOnsets.Count > 0)
                {
                    fillStart = half;
                    AddFill(result, fillOnsets, half, bar + ticksPerBar, ppq, noteLength);
                    if (barEnd < section.End) result.Add(new Note(barEnd, noteLength, GmDrum.Crash, 100));
                }
            }

            foreach (var (pitch, step, velocity) in template.Hits)
            {
                if (followFull && pitch == GmDrum.Kick) continue;

                var tick = bar + step * stepTicks + SwingOffset(section, step, ppq);
                if (tick < barEnd && tick < fillStart) result.Add(new Note(tick, noteLength, pitch, velocity));
            }
        }

        if (followFull) AddKicksFromBass(result, section, onsets, noteLength);

        // 区間の頭にクラッシュ
        result.Add(new Note(section.Start, noteLength, GmDrum.Crash, 100));
        return result.OrderBy(n => n.Tick).ThenBy(n => n.Pitch).ToList();
    }

    /// <summary>
    /// スウィングで後ろへずらす量。16ビートは16分の裏、それ以外は8分の裏をずらす。
    /// 強さ 1 で 3連符の 2:1 の位置になる。
    /// </summary>
    private static int SwingOffset(DrumSection section, int step, int ppq)
    {
        if (!section.Swing) return 0;

        var sixteenth = section.Pattern == DrumPattern.SixteenBeat;
        var isOffbeat = sixteenth ? step % 2 == 1 : step % 4 == 2;
        if (!isOffbeat) return 0;

        // 裏の位置: 8分なら 1/2 拍 → 2/3 拍、16分なら 1/4 拍 → 1/3 拍
        var maxShift = sixteenth ? ppq / 12.0 : ppq / 6.0;
        return (int)Math.Round(maxShift * Math.Clamp(section.SwingAmount, 0, 1));
    }

    /// <summary>
    /// フィル小節か。区間の最後の小節と、区間頭から4小節ごとの区切りの直前の小節。
    /// </summary>
    private static bool IsFillBar(DrumSection section, int bar, int index, int ticksPerBar) =>
        bar + ticksPerBar >= section.End || (index + 1) % FillPeriodBars == 0;

    /// <summary>
    /// [start, end) のベースの発音位置にフィルを打つ。スネアから始めて後ろほど低いタムにし、だんだん強くする。
    /// 拍頭の発音にはキックも重ねる。
    /// </summary>
    private static void AddFill(List<Note> result, List<int> onsets, int start, int end, int ppq, int noteLength)
    {
        var span = end - start;
        for (var i = 0; i < onsets.Count; i++)
        {
            var tick = onsets[i];
            var voice = FillVoices[Math.Min(FillVoices.Length - 1, (tick - start) * FillVoices.Length / span)];
            var velocity = 85 + 30 * i / Math.Max(1, onsets.Count - 1);
            result.Add(new Note(tick, noteLength, voice, velocity));
            if (tick % ppq == 0) result.Add(new Note(tick, noteLength, GmDrum.Kick, 100));
        }
    }

    /// <summary>
    /// ベースの発音位置にキックを置く。スネアと同じ位置は避ける。
    /// </summary>
    private static void AddKicksFromBass(List<Note> result, DrumSection section, IReadOnlyList<int> onsets, int noteLength)
    {
        var snareTicks = result.Where(n => n.Pitch == GmDrum.Snare).Select(n => n.Tick).ToHashSet();
        foreach (var onset in onsets)
        {
            if (!section.Contains(onset) || snareTicks.Contains(onset)) continue;
            result.Add(new Note(onset, noteLength, GmDrum.Kick, 100));
        }
    }
}
