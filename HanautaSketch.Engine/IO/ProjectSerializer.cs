using System.Text.Json;
using System.Text.Json.Serialization;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Engine.IO;

/// <summary>
/// 保存・読み込みの単位。曲本体と、曲に紐づくスマートコードの設定。
/// </summary>
public sealed record ProjectDocument(Song Song, MinorDominantMode MinorDominant, bool UseMelodyForChords);

/// <summary>
/// プロジェクトファイル（.hsk、JSON）の読み書き。
/// ファイル上の形式は DTO で固定し、ドメインモデルの変更がそのままファイル形式に漏れないようにする。
/// </summary>
public static class ProjectSerializer
{
    public const string FileExtension = ".hsk";

    /// <summary>現在のファイル形式のバージョン。互換性のない変更をしたら上げる</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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

    #region DTO 変換

    private static ProjectDto ToDto(ProjectDocument document)
    {
        var song = document.Song;
        return new ProjectDto
        {
            Version = CurrentVersion,
            Song = new SongDto
            {
                Ppq = song.Ppq,
                Bpm = song.Bpm,
                BeatsPerBar = song.BeatsPerBar,
                Key = new KeyDto { Tonic = song.Key.Tonic, Mode = song.Key.Mode },
                Tracks = song.Tracks.OfType<NoteTrack>().Select(t => new TrackDto
                {
                    Type = TrackType.Note,
                    Name = t.Name,
                    Program = t.Program,
                    Volume = t.Volume,
                    Notes = t.Notes.OrderBy(n => n.Tick).ThenBy(n => n.Pitch)
                        .Select(n => new NoteDto { Tick = n.Tick, Length = n.Length, Pitch = n.Pitch, Velocity = n.Velocity })
                        .ToList(),
                }).ToList(),
            },
            SmartChord = new SmartChordDto
            {
                MinorDominant = document.MinorDominant,
                UseMelody = document.UseMelodyForChords,
            },
        };
    }

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

        foreach (var trackDto in songDto.Tracks ?? [])
        {
            if (trackDto.Type != TrackType.Note) continue;

            var track = new NoteTrack
            {
                Name = trackDto.Name ?? "",
                Program = Math.Clamp(trackDto.Program, 0, 127),
                Volume = Math.Clamp(trackDto.Volume, 0, 127),
            };
            foreach (var n in trackDto.Notes ?? [])
            {
                if (n.Length <= 0 || n.Tick < 0) continue;
                track.Notes.Add(new Note(n.Tick, n.Length, Math.Clamp(n.Pitch, 0, 127), Math.Clamp(n.Velocity, 1, 127)));
            }
            song.Tracks.Add(track);
        }

        var smart = dto.SmartChord ?? new SmartChordDto();
        return new ProjectDocument(song, smart.MinorDominant, smart.UseMelody);
    }

    #endregion

    #region DTO

    private enum TrackType
    {
        Note,
    }

    private sealed class ProjectDto
    {
        public int Version { get; set; }
        public SongDto? Song { get; set; }
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
        public List<NoteDto>? Notes { get; set; }
    }

    private sealed class NoteDto
    {
        public int Tick { get; set; }
        public int Length { get; set; }
        public int Pitch { get; set; }
        public int Velocity { get; set; } = 100;
    }

    private sealed class SmartChordDto
    {
        public MinorDominantMode MinorDominant { get; set; }
        public bool UseMelody { get; set; } = true;
    }

    #endregion
}
