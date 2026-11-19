using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;
using HanautaSketch.Engine.Smart.Drums;

namespace HanautaSketch.Engine.IO;

/// <summary>
/// 保存・読み込みの単位。スマートトラックの設定はトラック自身が持つ。
/// </summary>
public sealed record ProjectDocument(Song Song);

/// <summary>
/// プロジェクトファイル（.hsk、JSON）の読み書き。
/// ファイル上の形式は DTO で固定し、ドメインモデルの変更がそのままファイル形式に漏れないようにする。
/// </summary>
public static partial class ProjectSerializer
{
    public const string FileExtension = ".hsk";

    /// <summary>
    /// 現在のファイル形式のバージョン（メジャー.マイナー）。正式版で 1.0 にする。
    /// 0.x の間はマイナーアップでも互換が崩れ得るので、ファイルの方が新しければマイナーまで含めて弾く。
    /// 0.1: トラックはノートのみ（曲全体のスマートコード設定 smartChord は読み込み時に無視する）
    /// 0.2: スマートコードトラック（ソース参照・設定・override）を追加
    /// 0.3: スマートドラムトラック（ソース参照・区間・fix した実体ノーツ）を追加
    /// 0.4: コードの N.C. override（noChord）。ドラム区間のベース追従を真偽値 followBass から bassFollow（none / fill / full）へ
    /// 0.5: テンポ・拍子・キーの途中変更（tempos / meters / keys）。曲頭の値だけの bpm / beatsPerBar / key は書き出さない
    /// 0.6: override のコードの種類に、三和音以外（7th・sus・テンションなど）を追加
    /// 0.7: ノートトラックのドラムパート指定（drum）
    /// 初期のファイルは整数（1 / 2）で書かれているので、それぞれ 0.1 / 0.2 として読む。
    /// </summary>
    public static Version CurrentVersion { get; } = new(0, 7);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = ProjectJsonContext.Default,
        Converters =
        {
            new JsonStringEnumConverter<TrackType>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<Mode>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<MinorDominantMode>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<AccompanimentPattern>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<ChordQuality>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<DrumPattern>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<DrumBassFollow>(JsonNamingPolicy.CamelCase),
        },
    };

    private static JsonTypeInfo<ProjectDto> ProjectDtoInfo => (JsonTypeInfo<ProjectDto>)Options.GetTypeInfo(typeof(ProjectDto));

    public static async Task SaveAsync(ProjectDocument document, Stream stream, CancellationToken ct = default)
    {
        await JsonSerializer.SerializeAsync(stream, ToDto(document), ProjectDtoInfo, ct);
    }

    /// <exception cref="InvalidDataException">形式が不正、または未対応のバージョン</exception>
    public static async Task<ProjectDocument> LoadAsync(Stream stream, CancellationToken ct = default)
    {
        ProjectDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync(stream, ProjectDtoInfo, ct);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The project file format is invalid.", ex);
        }

        if (dto is null) throw new InvalidDataException("The project file is empty.");
        if (dto.Version > CurrentVersion)
            throw new InvalidDataException($"The project file version {dto.Version.ToString(2)} is newer than supported.");

        return FromDto(dto);
    }

    #region 保存

    private static ProjectDto ToDto(ProjectDocument document)
    {
        var song = document.Song;
        var tracks = song.Tracks;
        return new ProjectDto
        {
            Version = CurrentVersion,
            Song = new SongDto
            {
                Ppq = song.Ppq,
                Tempos = song.Conductor.Tempos.Select(t => new TempoDto { Tick = t.Tick, Bpm = t.Bpm }).ToList(),
                Meters = song.Conductor.Meters.Select(m => new MeterDto { Bar = m.Bar, BeatsPerBar = m.BeatsPerBar }).ToList(),
                Keys = song.Conductor.Keys
                    .Select(k => new KeyDto { Tick = k.Tick, Tonic = k.Key.Tonic, Mode = k.Key.Mode }).ToList(),
                Tracks = tracks.Select(t => ToDto(t, tracks)).OfType<TrackDto>().ToList(),
            },
        };
    }

    private static TrackDto? ToDto(Track track, IList<Track> tracks)
    {
        var dto = new TrackDto
        {
            Name = track.Name,
            Program = track.Program,
            Volume = track.Volume,
            Mute = track.Mute ? true : null,
            Solo = track.Solo ? true : null,
        };
        switch (track)
        {
            case NoteTrack note:
                dto.Type = TrackType.Note;
                dto.Drum = note.IsDrumPart ? true : null;
                dto.Notes = note.Notes.OrderBy(n => n.Tick).ThenBy(n => n.Pitch)
                    .Select(n => new NoteDto { Tick = n.Tick, Length = n.Length, Pitch = n.Pitch, Velocity = n.Velocity })
                    .ToList();
                return dto;
            case SmartChordTrack smart:
                dto.Type = TrackType.SmartChord;
                dto.BassSource = IndexOf(tracks, smart.BassSource);
                dto.MelodySource = IndexOf(tracks, smart.MelodySource);
                dto.MinorDominant = smart.ChordSettings.MinorDominant;
                dto.Pattern = smart.Accompaniment.Pattern;
                dto.DivisionTicks = smart.Accompaniment.DivisionTicks;
                dto.Velocity = smart.Accompaniment.Velocity;
                dto.Overrides = smart.Overrides.Select(o => new OverrideDto
                {
                    Start = o.Start,
                    Length = o.Length,
                    NoChord = o.Chord is null ? true : null,
                    Root = o.Chord?.Root ?? 0,
                    Quality = o.Chord?.Quality ?? default,
                    Bass = o.Chord?.Bass,
                }).ToList();
                return dto;
            case SmartDrumTrack drum:
                dto.Type = TrackType.SmartDrum;
                dto.BassSource = IndexOf(tracks, drum.BassSource);
                dto.Notes = drum.FixedNotes.OrderBy(n => n.Tick).ThenBy(n => n.Pitch)
                    .Select(n => new NoteDto { Tick = n.Tick, Length = n.Length, Pitch = n.Pitch, Velocity = n.Velocity })
                    .ToList();
                dto.Sections = drum.Sections.Select(s => new DrumSectionDto
                {
                    Start = s.Start,
                    Length = s.Length,
                    Pattern = s.Pattern,
                    Swing = s.Swing,
                    SwingAmount = s.SwingAmount,
                    BassFollow = s.BassFollow,
                    Fixed = s.IsFixed,
                }).ToList();
                return dto;
            default:
                return null;
        }
    }

    private static int? IndexOf(IList<Track> tracks, Track? track)
    {
        if (track is null) return null;
        var index = tracks.IndexOf(track);
        return index >= 0 ? index : null;
    }

    #endregion

    #region 読み込み

    private static ProjectDocument FromDto(ProjectDto dto)
    {
        var songDto = dto.Song ?? new SongDto();
        var song = new Song { Ppq = songDto.Ppq > 0 ? songDto.Ppq : Song.DefaultPpq };
        ReadConductor(song.Conductor, songDto);

        // ソース参照はトラックの並び順で持つので、全トラックを作ってから解決する
        var pending = new List<(SmartChordTrack Track, TrackDto Dto)>();
        var pendingDrums = new List<(SmartDrumTrack Track, TrackDto Dto)>();
        foreach (var trackDto in songDto.Tracks ?? [])
        {
            Track? track = trackDto.Type switch
            {
                TrackType.Note => CreateNoteTrack(trackDto),
                TrackType.SmartChord => CreateSmartChordTrack(trackDto, pending),
                TrackType.SmartDrum => CreateSmartDrumTrack(trackDto, pendingDrums),
                _ => null,
            };
            if (track is null) continue;

            track.Name = trackDto.Name ?? "";
            track.Program = Math.Clamp(trackDto.Program, 0, 127);
            track.Volume = Math.Clamp(trackDto.Volume, 0, 127);
            track.Mute = trackDto.Mute ?? false;
            track.Solo = trackDto.Solo ?? false;
            song.Tracks.Add(track);
        }

        foreach (var (track, trackDto) in pending)
        {
            track.BassSource = ResolveNoteTrack(song, trackDto.BassSource);
            track.MelodySource = ResolveNoteTrack(song, trackDto.MelodySource);
        }

        foreach (var (track, trackDto) in pendingDrums)
            track.BassSource = ResolveNoteTrack(song, trackDto.BassSource);

        return new ProjectDocument(song);
    }

    /// <summary>
    /// テンポ・拍子・キーを読む。0.4 までは曲頭の値（bpm / beatsPerBar / key）だけを持つ。
    /// </summary>
    private static void ReadConductor(Conductor conductor, SongDto dto)
    {
        static Key ToKey(KeyDto? k) => new(Math.Clamp(k?.Tonic ?? 0, 0, 11), k?.Mode ?? Mode.Major);

        conductor.Reset(dto.Bpm is > 0 ? dto.Bpm.Value : 120, dto.BeatsPerBar is > 0 ? dto.BeatsPerBar.Value : 4,
            ToKey(dto.Key));

        foreach (var t in dto.Tempos ?? [])
        {
            if (t.Tick >= 0 && t.Bpm > 0) conductor.SetTempo(t.Tick, t.Bpm);
        }
        foreach (var m in dto.Meters ?? [])
        {
            if (m.Bar >= 0 && m.BeatsPerBar > 0) conductor.SetMeter(m.Bar, m.BeatsPerBar);
        }
        foreach (var k in dto.Keys ?? [])
        {
            if (k.Tick >= 0) conductor.SetKey(k.Tick, ToKey(k));
        }
    }

    private static NoteTrack CreateNoteTrack(TrackDto dto)
    {
        var track = new NoteTrack { IsDrumPart = dto.Drum ?? false };
        track.Notes.AddRange(ReadNotes(dto));
        return track;
    }

    private static IEnumerable<Note> ReadNotes(TrackDto dto) =>
        (dto.Notes ?? [])
            .Where(n => n.Length > 0 && n.Tick >= 0)
            .Select(n => new Note(n.Tick, n.Length, Math.Clamp(n.Pitch, 0, 127), Math.Clamp(n.Velocity, 1, 127)));

    private static SmartDrumTrack CreateSmartDrumTrack(TrackDto dto, List<(SmartDrumTrack, TrackDto)> pending)
    {
        var track = new SmartDrumTrack();
        foreach (var s in (dto.Sections ?? []).Where(s => s.Length > 0 && s.Start >= 0).OrderBy(s => s.Start))
        {
            track.Sections.Add(new DrumSection
            {
                Start = s.Start,
                Length = s.Length,
                Pattern = s.Pattern,
                Swing = s.Swing,
                SwingAmount = Math.Clamp(s.SwingAmount, 0, 1),
                // 0.3 までは「キックをベースに合わせる」の真偽値
                BassFollow = s.BassFollow ?? (s.FollowBass == true ? DrumBassFollow.Full : DrumBassFollow.None),
                IsFixed = s.Fixed,
            });
        }
        track.FixedNotes.AddRange(ReadNotes(dto));
        pending.Add((track, dto));
        return track;
    }

    private static SmartChordTrack CreateSmartChordTrack(TrackDto dto, List<(SmartChordTrack, TrackDto)> pending)
    {
        var track = new SmartChordTrack
        {
            ChordSettings = new SmartChordSettings { MinorDominant = dto.MinorDominant ?? default },
            Accompaniment = new AccompanimentSettings
            {
                Pattern = dto.Pattern ?? default,
                DivisionTicks = Math.Max(0, dto.DivisionTicks ?? 0),
                Velocity = Math.Clamp(dto.Velocity ?? 80, 1, 127),
            },
        };

        foreach (var o in dto.Overrides ?? [])
        {
            if (o.Length <= 0 || o.Start < 0) continue;
            var bass = o.Bass is { } b ? Math.Clamp(b, 0, 11) : (int?)null;
            Chord? chord = o.NoChord == true ? null : new Chord(Math.Clamp(o.Root, 0, 11), o.Quality, bass);
            track.Overrides.Add(new ChordOverride(o.Start, o.Length, chord));
        }

        pending.Add((track, dto));
        return track;
    }

    private static NoteTrack? ResolveNoteTrack(Song song, int? index) =>
        index is { } i && i >= 0 && i < song.Tracks.Count ? song.Tracks[i] as NoteTrack : null;

    #endregion

    #region DTO

    /// <summary>
    /// DTO の JSON メタデータ（NativeAOT で動くようソース生成する）。書式と enum の変換は <see cref="Options"/> で与える。
    /// </summary>
    [JsonSerializable(typeof(ProjectDto))]
    private sealed partial class ProjectJsonContext : JsonSerializerContext;

    private enum TrackType
    {
        Note,
        SmartChord,
        SmartDrum,
    }

    private sealed class ProjectDto
    {
        [JsonConverter(typeof(FormatVersionConverter))]
        public Version Version { get; set; } = new(0, 1);

        public SongDto? Song { get; set; }
    }

    /// <summary>
    /// バージョンを "0.2" のような文字列で書き出す。読み込みでは初期の整数表記（1 / 2）を 0.x として受け付ける。
    /// </summary>
    private sealed class FormatVersionConverter : JsonConverter<Version>
    {
        public override Version Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var legacy))
                return new Version(0, legacy);

            if (reader.TokenType == JsonTokenType.String && Version.TryParse(reader.GetString(), out var version))
                return new Version(version.Major, Math.Max(0, version.Minor));

            throw new JsonException("The version format is invalid.");
        }

        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(2));
    }

    private sealed class SongDto
    {
        public int Ppq { get; set; }

        // 0.4 まで（曲頭の値）。読み込み専用
        public double? Bpm { get; set; }
        public int? BeatsPerBar { get; set; }
        public KeyDto? Key { get; set; }

        // 0.5 から（曲頭を含む変更点）
        public List<TempoDto>? Tempos { get; set; }
        public List<MeterDto>? Meters { get; set; }
        public List<KeyDto>? Keys { get; set; }

        public List<TrackDto>? Tracks { get; set; }
    }

    private sealed class TempoDto
    {
        public int Tick { get; set; }
        public double Bpm { get; set; }
    }

    private sealed class MeterDto
    {
        public int Bar { get; set; }
        public int BeatsPerBar { get; set; }
    }

    private sealed class KeyDto
    {
        /// <summary>変更点の位置（0.5 から。0.4 までの曲頭の key では使わない）</summary>
        public int Tick { get; set; }
        public int Tonic { get; set; }
        public Mode Mode { get; set; }
    }

    private sealed class TrackDto
    {
        public TrackType Type { get; set; }
        public string? Name { get; set; }
        public int Program { get; set; }
        public int Volume { get; set; } = 100;

        // 0.2 の途中で追加した任意項目。古いアプリでは無視されるだけなのでバージョンは上げていない
        public bool? Mute { get; set; }
        public bool? Solo { get; set; }

        // ノートトラック
        public List<NoteDto>? Notes { get; set; }

        /// <summary>ドラムパート（0.7〜）。false のときは書き出さない</summary>
        public bool? Drum { get; set; }

        // スマートコードトラック
        public int? BassSource { get; set; }
        public int? MelodySource { get; set; }
        public MinorDominantMode? MinorDominant { get; set; }
        public AccompanimentPattern? Pattern { get; set; }
        public int? DivisionTicks { get; set; }
        public int? Velocity { get; set; }
        public List<OverrideDto>? Overrides { get; set; }

        // スマートドラムトラック（Notes は fix した区間の実体ノーツ）
        public List<DrumSectionDto>? Sections { get; set; }
    }

    private sealed class DrumSectionDto
    {
        public int Start { get; set; }
        public int Length { get; set; }
        public DrumPattern Pattern { get; set; }
        public bool Swing { get; set; }
        public double SwingAmount { get; set; } = 0.67;
        public DrumBassFollow? BassFollow { get; set; }

        /// <summary>0.3 まで。読み込み専用（null のまま書き出さない）</summary>
        public bool? FollowBass { get; set; }

        public bool Fixed { get; set; }
    }

    private sealed class NoteDto
    {
        public int Tick { get; set; }
        public int Length { get; set; }
        public int Pitch { get; set; }
        public int Velocity { get; set; } = 100;
    }

    private sealed class OverrideDto
    {
        public int Start { get; set; }
        public int Length { get; set; }
        /// <summary>true なら N.C.（Root などは無視）</summary>
        public bool? NoChord { get; set; }
        public int Root { get; set; }
        public ChordQuality Quality { get; set; }
        public int? Bass { get; set; }
    }

    #endregion
}
