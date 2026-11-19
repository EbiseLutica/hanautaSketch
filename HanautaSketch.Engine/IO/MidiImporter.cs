using System.Buffers.Binary;
using System.Text;
using HanautaSketch.Engine.Core;

namespace HanautaSketch.Engine.IO;

/// <summary>
/// Standard MIDI File を曲として読み込む。おまけ機能なので、読むのはトラック名・ノーツ・音量（CC7）・
/// Program Change と、コンダクターのテンポ・拍子・調号だけ。
/// トラックはチャンネルごとに分け、ノーツのないものは作らない。10ch のノーツはドラムパートにする。
/// スマートトラックを書き出したものも、ただのノートトラックとして読む。
/// </summary>
public static class MidiImporter
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <exception cref="InvalidDataException">SMF として読めない、または SMPTE のタイムベース</exception>
    public static Song Import(Stream input)
    {
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        var data = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);

        var division = ReadHeader(data, out var offset);
        var song = new Song();
        var tickScale = (double)song.Ppq / division;
        var tracks = new List<ParsedTrack>();

        while (offset + 8 <= data.Length)
        {
            var id = data.Slice(offset, 4);
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
            offset += 8;
            if (length > (uint)(data.Length - offset))
                throw new InvalidDataException("The MIDI chunk is truncated.");

            var chunk = data.Slice(offset, (int)length);
            offset += (int)length;
            if (!id.SequenceEqual("MTrk"u8)) continue;

            tracks.Add(ParseTrack(chunk));
        }

        // 音色・音量だけを置いたセットアップ用のトラックもあるので、ノーツのないチャンネルの値も既定値として使う
        var defaults = new ChannelDefaults();
        foreach (var (channel, part) in tracks.SelectMany(t => t.Channels))
        {
            if (part.Program is { } program) defaults.Programs.TryAdd(channel, program);
            if (part.Volume is { } volume) defaults.Volumes.TryAdd(channel, volume);
        }
        foreach (var parsed in tracks)
            AddTracks(song, parsed, defaults, tickScale);

        var conductorEvents = tracks.SelectMany(t => t.ConductorEvents).ToList();
        if (conductorEvents.Count > MaxConductorEvents)
            throw new InvalidDataException("The MIDI file has too many tempo, time or key signature changes.");
        ApplyConductor(song.Conductor, conductorEvents, tickScale);
        return song;
    }

    /// <summary>テンポ・拍子・調号の変化の上限。変更点の追加は件数の2乗で重くなるので、細工されたファイルで固まらないようにする</summary>
    private const int MaxConductorEvents = 10_000;

    #region 解析

    /// <summary>ヘッダーを読み、4分音符あたりの tick 数を返す。offset はヘッダーの直後になる</summary>
    private static int ReadHeader(ReadOnlySpan<byte> data, out int offset)
    {
        if (data.Length < 14 || !data[..4].SequenceEqual("MThd"u8))
            throw new InvalidDataException("The file is not a Standard MIDI File.");

        var headerLength = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        if (headerLength < 6 || headerLength > (uint)(data.Length - 8))
            throw new InvalidDataException("The MIDI header is invalid.");

        // Format 2 はトラックごとに独立した曲なので、1曲としては読めない
        if (BinaryPrimitives.ReadUInt16BigEndian(data[8..]) > 1)
            throw new InvalidDataException("Only MIDI format 0 and 1 are supported.");

        var division = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
        if ((division & 0x8000) != 0)
            throw new InvalidDataException("SMPTE time division is not supported.");
        if (division == 0)
            throw new InvalidDataException("The MIDI time division is invalid.");

        offset = 8 + (int)headerLength;
        return division;
    }

    private static ParsedTrack ParseTrack(ReadOnlySpan<byte> chunk)
    {
        var track = new ParsedTrack();
        var reader = new SmfReader(chunk);
        var tick = 0L;
        byte runningStatus = 0;

        while (!reader.End)
        {
            tick += reader.ReadVariableLength();
            var status = reader.Peek();
            if (status >= 0x80)
                reader.Skip(1);
            else if (runningStatus != 0)
                status = runningStatus;
            else
                throw new InvalidDataException("The MIDI track has a data byte without a status.");

            if (status == SmfStatus.Meta)
            {
                var type = reader.ReadByte();
                var payload = reader.ReadBytes(reader.ReadVariableLength());
                if (type == SmfMeta.EndOfTrack) break;
                ReadMeta(track, tick, type, payload);
                continue;
            }

            if (status is SmfStatus.SysEx or SmfStatus.SysExEscape)
            {
                reader.Skip(reader.ReadVariableLength());
                continue;
            }

            if (status >= SmfStatus.SysEx)
                throw new InvalidDataException("The MIDI track has an unexpected system message.");

            runningStatus = status;
            var data1 = reader.ReadDataByte();
            var data2 = SmfStatus.DataLength(status) == 1 ? (byte)0 : reader.ReadDataByte();
            track.Channel(status & 0x0F).Add(tick, status & 0xF0, data1, data2);
        }

        track.EndTick = tick;
        return track;
    }

    private static void ReadMeta(ParsedTrack track, long tick, byte type, ReadOnlySpan<byte> payload)
    {
        switch (type)
        {
            case SmfMeta.TrackName when track.Name is null:
                track.Name = DecodeText(payload);
                break;
            case SmfMeta.Tempo when payload.Length >= 3:
                var microseconds = (payload[0] << 16) | (payload[1] << 8) | payload[2];
                if (microseconds > 0)
                    track.ConductorEvents.Add(new ConductorEvent(tick, ConductorChangeKind.Tempo, 60_000_000.0 / microseconds, 0));
                break;
            case SmfMeta.TimeSignature when payload.Length >= 2 && payload[0] > 0 && payload[1] < 8:
                var beats = Math.Max(1, (int)Math.Round(payload[0] * 4.0 / (1 << payload[1]), MidpointRounding.AwayFromZero));
                track.ConductorEvents.Add(new ConductorEvent(tick, ConductorChangeKind.Meter, beats, 0));
                break;
            case SmfMeta.KeySignature when payload.Length >= 2 && (sbyte)payload[0] is >= -7 and <= 7 && payload[1] <= 1:
                track.ConductorEvents.Add(new ConductorEvent(tick, ConductorChangeKind.Key, (sbyte)payload[0], payload[1]));
                break;
        }
    }

    /// <summary>
    /// テキスト系メタイベントを文字列にする。UTF-8 として正しければ UTF-8、そうでなければ Shift_JIS とみなす。
    /// </summary>
    private static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = MidiExporter.DefaultTextEncoding.GetString(bytes);
        }
        return text.TrimEnd('\0').Trim();
    }

    #endregion

    #region 曲への反映

    /// <summary>
    /// トラックをチャンネルごとに作る。音色・音量はそのチャンネルの最初の値、無ければファイル全体でそのチャンネルに最初に出た値
    /// </summary>
    private static void AddTracks(Song song, ParsedTrack parsed, ChannelDefaults defaults, double tickScale)
    {
        var parts = parsed.Channels
            .Select(p => (Channel: p.Key, Part: p.Value, Notes: p.Value.BuildNotes(parsed.EndTick, tickScale).ToList()))
            .Where(p => p.Notes.Count > 0)
            .ToList();
        foreach (var (channel, part, notes) in parts)
        {
            var name = parsed.Name ?? "";
            if (name.Length == 0)
                name = $"Ch {channel + 1}";
            else if (parts.Count > 1)
                name = $"{name} (Ch {channel + 1})";

            var track = new NoteTrack
            {
                Name = name,
                IsDrumPart = channel == MidiChannels.Drum,
            };
            if ((part.Program ?? defaults.ProgramOf(channel)) is { } program) track.Program = program;
            if ((part.Volume ?? defaults.VolumeOf(channel)) is { } volume) track.Volume = volume;
            track.Notes.AddRange(notes);
            song.Tracks.Add(track);
        }
    }

    /// <summary>
    /// 全トラックから集めたテンポ・拍子・調号を反映する。曲頭に無いものは新しい曲の既定値のまま。
    /// 拍子は n/4 しか持てないので、小節の長さを4分音符の数に丸める。
    /// </summary>
    private static void ApplyConductor(Conductor conductor, List<ConductorEvent> events, double tickScale)
    {
        // 拍子の小節番号はそれより前の拍子で決まるので、tick 順に置く（同じ tick は後に出たものを優先）
        foreach (var e in events.OrderBy(e => e.Tick))
        {
            var tick = ScaleTick(e.Tick, tickScale);
            switch (e.Kind)
            {
                case ConductorChangeKind.Tempo:
                    conductor.SetTempo(tick, e.Value);
                    break;
                case ConductorChangeKind.Meter:
                    var bar = conductor.BarAt(conductor.CeilToBar(tick));
                    conductor.SetMeter(bar, (int)e.Value);
                    break;
                case ConductorChangeKind.Key:
                    conductor.SetKey(tick, Key.FromKeySignature((int)e.Value, e.Minor == 1 ? Mode.Minor : Mode.Major));
                    break;
            }
        }
    }

    /// <summary>
    /// tick を曲の PPQ に換算する。小節の計算であふれないよう、int の半分を超える位置は不正なファイルとして弾く。
    /// </summary>
    private static int ScaleTick(long tick, double tickScale)
    {
        var scaled = Math.Round(tick * tickScale);
        return scaled <= MaxTick ? (int)scaled : throw new InvalidDataException("The MIDI file is too long.");
    }

    private const int MaxTick = int.MaxValue / 2;

    #endregion

    #region 中間データ

    /// <summary>ファイル全体で、チャンネルごとに最初に出た音色・音量</summary>
    private sealed class ChannelDefaults
    {
        public Dictionary<int, int> Programs { get; } = [];

        public Dictionary<int, int> Volumes { get; } = [];

        public int? ProgramOf(int channel) => Programs.TryGetValue(channel, out var program) ? program : null;

        public int? VolumeOf(int channel) => Volumes.TryGetValue(channel, out var volume) ? volume : null;
    }

    private readonly record struct ConductorEvent(long Tick, ConductorChangeKind Kind, double Value, int Minor);

    private sealed class ParsedTrack
    {
        public string? Name { get; set; }

        public long EndTick { get; set; }

        public List<ConductorEvent> ConductorEvents { get; } = [];

        public SortedDictionary<int, ChannelPart> Channels { get; } = [];

        public ChannelPart Channel(int channel)
        {
            if (!Channels.TryGetValue(channel, out var part))
                Channels[channel] = part = new ChannelPart();
            return part;
        }
    }

    /// <summary>
    /// 1トラック内の1チャンネル分。同じ音の重なりは、先に鳴った方から順に止める。
    /// </summary>
    private sealed class ChannelPart
    {
        private readonly List<(long Tick, long EndTick, int Pitch, int Velocity)> _notes = [];
        private readonly Dictionary<int, Queue<(long Tick, int Velocity)>> _sounding = [];

        public int? Program { get; private set; }

        public int? Volume { get; private set; }

        public void Add(long tick, int type, byte data1, byte data2)
        {
            switch (type)
            {
                case SmfStatus.NoteOn when data2 > 0:
                    if (!_sounding.TryGetValue(data1, out var queue))
                        _sounding[data1] = queue = new Queue<(long, int)>();
                    queue.Enqueue((tick, data2));
                    break;
                case SmfStatus.NoteOn:
                case SmfStatus.NoteOff:
                    if (_sounding.TryGetValue(data1, out var sounding) && sounding.TryDequeue(out var on))
                        _notes.Add((on.Tick, tick, data1, on.Velocity));
                    break;
                case SmfStatus.ProgramChange:
                    Program ??= data1;
                    break;
                case SmfStatus.ControlChange when data1 == 7:
                    Volume ??= data2;
                    break;
            }
        }

        /// <summary>止まっていないノーツはトラックの終わりで切り、tick を曲の PPQ に換算する。長さが 0 になったものは捨てる</summary>
        public IEnumerable<Note> BuildNotes(long endTick, double tickScale)
        {
            var unterminated = _sounding.SelectMany(p => p.Value.Select(on => (on.Tick, endTick, p.Key, on.Velocity)));
            return _notes.Concat(unterminated)
                .Select(n =>
                {
                    var start = ScaleTick(n.Item1, tickScale);
                    return new Note(start, ScaleTick(n.Item2, tickScale) - start, n.Item3, Math.Clamp(n.Item4, 1, 127));
                })
                .Where(n => n.Length > 0)
                .OrderBy(n => n.Tick).ThenBy(n => n.Pitch);
        }
    }

    /// <summary>
    /// トラックデータを先頭から読む。足りなければ <see cref="InvalidDataException"/>。
    /// </summary>
    private ref struct SmfReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public readonly bool End => _position >= _data.Length;

        public readonly byte Peek() => End ? throw Truncated() : _data[_position];

        public byte ReadByte()
        {
            var value = Peek();
            _position++;
            return value;
        }

        /// <summary>チャンネルメッセージのデータバイト（0〜127）を読む</summary>
        public byte ReadDataByte()
        {
            var value = ReadByte();
            return value < 0x80 ? value : throw new InvalidDataException("The MIDI track has an invalid data byte.");
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count > _data.Length - _position) throw Truncated();
            var bytes = _data.Slice(_position, count);
            _position += count;
            return bytes;
        }

        public void Skip(int count) => ReadBytes(count);

        public int ReadVariableLength()
        {
            var value = 0;
            for (var i = 0; i < 4; i++)
            {
                var b = ReadByte();
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0) return value;
            }
            throw new InvalidDataException("The MIDI variable-length value is too long.");
        }

        private static InvalidDataException Truncated() => new("The MIDI track is truncated.");
    }

    #endregion
}
