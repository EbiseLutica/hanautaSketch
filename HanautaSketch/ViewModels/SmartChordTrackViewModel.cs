using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Resources;

namespace HanautaSketch.ViewModels;

/// <summary>ソーストラックの選択肢。Track が null なら「なし」</summary>
public sealed record SourceOption(string Label, NoteTrack? Track)
{
    public override string ToString() => Label;
}

/// <summary>伴奏パターンの選択肢</summary>
public sealed record PatternOption(string Label, AccompanimentPattern Pattern)
{
    public override string ToString() => Label;
}

/// <summary>刻み幅の選択肢</summary>
public sealed record DivisionOption(string Label, int Ticks)
{
    public override string ToString() => Label;
}

/// <summary>
/// スマートコードトラックの設定パネル。値を変えるとモデルに反映し、オーナーに再生成を依頼する。
/// </summary>
public sealed class SmartChordTrackViewModel
{
    private readonly SmartChordTrack _track;
    private readonly MainWindowViewModel _owner;

    public SmartChordTrackViewModel(SmartChordTrack track, MainWindowViewModel owner)
    {
        _track = track;
        _owner = owner;

        // ソースのトラックが削除されると null になるので、ベースにも「なし」を用意する
        var options = owner.Tracks.OfType<NoteTrack>().Select(t => new SourceOption(t.Name, t))
            .Prepend(new SourceOption(Strings.SmartChordNoSource, null)).ToList();
        BassSourceOptions = options;
        MelodySourceOptions = options;
    }

    public IReadOnlyList<SourceOption> BassSourceOptions { get; }

    public IReadOnlyList<SourceOption> MelodySourceOptions { get; }

    public IReadOnlyList<MinorDominantMode> MinorDominantModes { get; } = Enum.GetValues<MinorDominantMode>();

    public IReadOnlyList<PatternOption> PatternOptions { get; } =
    [
        new(Strings.SmartChordPatternSustain, AccompanimentPattern.Sustain),
        new(Strings.SmartChordPatternBeat, AccompanimentPattern.Beat),
        new(Strings.SmartChordPatternFollowBass, AccompanimentPattern.FollowBass),
        new(Strings.SmartChordPatternArpeggio, AccompanimentPattern.Arpeggio),
    ];

    public IReadOnlyList<DivisionOption> DivisionOptions { get; } =
    [
        new("1/4", Song.DefaultPpq),
        new("1/8", Song.DefaultPpq / 2),
        new("1/16", Song.DefaultPpq / 4),
    ];

    public SourceOption? BassSource
    {
        get => BassSourceOptions.FirstOrDefault(o => o.Track == _track.BassSource);
        set => Update(() => _track.BassSource = value?.Track);
    }

    public SourceOption? MelodySource
    {
        get => MelodySourceOptions.FirstOrDefault(o => o.Track == _track.MelodySource);
        set => Update(() => _track.MelodySource = value?.Track);
    }

    public MinorDominantMode MinorDominant
    {
        get => _track.ChordSettings.MinorDominant;
        set => Update(() => _track.ChordSettings = _track.ChordSettings with { MinorDominant = value });
    }

    public PatternOption? Pattern
    {
        get => PatternOptions.FirstOrDefault(o => o.Pattern == _track.Accompaniment.Pattern);
        set
        {
            if (value is null) return;
            Update(() => _track.Accompaniment = _track.Accompaniment with { Pattern = value.Pattern });
        }
    }

    public DivisionOption? Division
    {
        get
        {
            var ticks = _track.Accompaniment.DivisionTicks > 0 ? _track.Accompaniment.DivisionTicks : Song.DefaultPpq / 2;
            return DivisionOptions.FirstOrDefault(o => o.Ticks == ticks);
        }
        set
        {
            if (value is null) return;
            Update(() => _track.Accompaniment = _track.Accompaniment with { DivisionTicks = value.Ticks });
        }
    }

    public int Velocity
    {
        get => _track.Accompaniment.Velocity;
        set => Update(() => _track.Accompaniment = _track.Accompaniment with { Velocity = Math.Clamp(value, 1, 127) });
    }

    private void Update(Action apply)
    {
        apply();
        _owner.OnSmartTrackSettingsChanged();
    }
}
