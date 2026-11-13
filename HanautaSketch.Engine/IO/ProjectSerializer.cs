using System.Text.Json;
using System.Text.Json.Serialization;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Engine.IO;

/// <summary>
/// 保存・読み込みの単位。スマートトラックの設定はトラック自身が持つ。
/// </summary>
public sealed record ProjectDocument(Song Song);

/// <summary>
/// プロジェクトファイル（.hsk、JSON）の読み書き。
/// ファイル上の形式は DTO で固定し、ドメインモデルの変更がそのままファイル形式に漏れないようにする。
/// </summary>
public static class ProjectSerializer
{
    public const string FileExtension = ".hsk";

    /// <summary>
    /// 現在のファイル形式のバージョン。互換性のない変更をしたら上げる。
    /// v1: トラックはノートのみ、スマートコードの設定は曲全体に1つ
    /// v2: スマートコードトラック（ソース参照・設定・override）を追加
    /// </summary>
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task SaveAsync(ProjectDocument document, Stream stream, CancellationToken ct = default)
    {
        await JsonSerializer.SerializeAsync(stream, ToDto(document), Options, ct);
    }

    /// <exception cref="InvalidDataException">形式が不正、または未対応のバージョン</exception>
    public static async Task<ProjectDocument> LoadAsync(Stream stream, CancellationToken ct = default)
    {
        ProjectDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync<ProjectDto>(stream, Options, ct);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("プロジェクトファイルの形式が不正です。", ex);
        }

        if (dto is null) throw new InvalidDataException("プロジェクトファイルが空です。");
        if (dto.Version > CurrentVersion)
            throw new InvalidDataException($"新しいバージョン（{dto.Version}）のファイルには対応していません。");

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
                Bpm = song.Bpm,
                BeatsPerBar = song.BeatsPerBar,
                Key = new KeyDto { Tonic = song.Key.Tonic, Mode = song.Key.Mode },
                Tracks = tracks.Select(t => ToDto(t, tracks)).OfType<TrackDto>().ToList(),
            },
        };
    }

    private static TrackDto? ToDto(Track track, IList<Track> tracks)
    {
        var dto = new TrackDto { Name = track.Name, Program = track.Program, Volume = track.Volume };
        switch (track)
        {
            case NoteTrack note:
                dto.Type = TrackType.Note;
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
                    Root = o.Chord.Root,
                    Quality = o.Chord.Quality,
                    Bass = o.Chord.Bass,
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
        var song = new Song
        {
            Ppq = songDto.Ppq > 0 ? songDto.Ppq : Song.DefaultPpq,
            Bpm = songDto.Bpm > 0 ? songDto.Bpm : 120,
            BeatsPerBar = songDto.BeatsPerBar > 0 ? songDto.BeatsPerBar : 4,
            Key = new Key(Math.Clamp(songDto.Key?.Tonic ?? 0, 0, 11), songDto.Key?.Mode ?? Mode.Major),
        };

        // ソース参照はトラックの並び順で持つので、全トラックを作ってから解決する
        var pending = new List<(SmartChordTrack Track, TrackDto Dto)>();
        foreach (var trackDto in songDto.Tracks ?? [])
        {
            Track? track = trackDto.Type switch
            {
                TrackType.Note => CreateNoteTrack(trackDto),
                TrackType.SmartChord => CreateSmartChordTrack(trackDto, pending),
                _ => null,
            };
            if (track is null) continue;

            track.Name = trackDto.Name ?? "";
            track.Program = Math.Clamp(trackDto.Program, 0, 127);
            track.Volume = Math.Clamp(trackDto.Volume, 0, 127);
            song.Tracks.Add(track);
        }

        foreach (var (track, trackDto) in pending)
        {
            track.BassSource = ResolveNoteTrack(song, trackDto.BassSource);
            track.MelodySource = ResolveNoteTrack(song, trackDto.MelodySource);
        }

        if (dto.Version < 2) MigrateFromV1(dto, song);

        return new ProjectDocument(song);
    }

    private static NoteTrack CreateNoteTrack(TrackDto dto)
    {
        var track = new NoteTrack();
        foreach (var n in dto.Notes ?? [])
        {
            if (n.Length <= 0 || n.Tick < 0) continue;
            track.Notes.Add(new Note(n.Tick, n.Length, Math.Clamp(n.Pitch, 0, 127), Math.Clamp(n.Velocity, 1, 127)));
        }
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
            track.Overrides.Add(new ChordOverride(o.Start, o.Length, new Chord(Math.Clamp(o.Root, 0, 11), o.Quality, bass)));
        }

        pending.Add((track, dto));
        return track;
    }

    private static NoteTrack? ResolveNoteTrack(Song song, int? index) =>
        index is { } i && i >= 0 && i < song.Tracks.Count ? song.Tracks[i] as NoteTrack : null;

    /// <summary>
    /// v1 は曲全体のスマートコード設定だけを持っていたので、先頭2トラック（Melody / Bass）を
    /// ソースにしたスマートコードトラックを1本作る。
    /// </summary>
    private static void MigrateFromV1(ProjectDto dto, Song song)
    {
        var noteTracks = song.Tracks.OfType<NoteTrack>().ToList();
        if (noteTracks.Count < 2) return;

        var smart = dto.SmartChord ?? new SmartChordDto();
        song.Tracks.Add(new SmartChordTrack
        {
            Name = "Chord",
            BassSource = noteTracks[1],
            MelodySource = smart.UseMelody ? noteTracks[0] : null,
            ChordSettings = new SmartChordSettings { MinorDominant = smart.MinorDominant },
        });
    }

    #endregion

    #region DTO

    private enum TrackType
    {
        Note,
        SmartChord,
    }

    private sealed class ProjectDto
    {
        public int Version { get; set; }
        public SongDto? Song { get; set; }

        /// <summary>v1 のみ。v2 以降はスマートコードトラックが持つ</summary>
        public SmartChordDto? SmartChord { get; set; }
    }

    private sealed class SongDto
    {
        public int Ppq { get; set; }
        public double Bpm { get; set; }
        public int BeatsPerBar { get; set; }
        public KeyDto? Key { get; set; }
        public List<TrackDto>? Tracks { get; set; }
    }

    private sealed class KeyDto
    {
        public int Tonic { get; set; }
        public Mode Mode { get; set; }
    }

    private sealed class TrackDto
    {
        public TrackType Type { get; set; }
        public string? Name { get; set; }
        public int Program { get; set; }
        public int Volume { get; set; } = 100;

        // ノートトラック
        public List<NoteDto>? Notes { get; set; }

        // スマートコードトラック
        public int? BassSource { get; set; }
        public int? MelodySource { get; set; }
        public MinorDominantMode? MinorDominant { get; set; }
        public AccompanimentPattern? Pattern { get; set; }
        public int? DivisionTicks { get; set; }
        public int? Velocity { get; set; }
        public List<OverrideDto>? Overrides { get; set; }
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
        public int Root { get; set; }
        public ChordQuality Quality { get; set; }
        public int? Bass { get; set; }
    }

    private sealed class SmartChordDto
    {
        public MinorDominantMode MinorDominant { get; set; }
        public bool UseMelody { get; set; } = true;
    }

    #endregion
}
