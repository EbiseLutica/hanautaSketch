using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using HanautaSketch.Engine.Core;
using HanautaSketch.Engine.Smart.Chords;

namespace HanautaSketch.Engine.IO;

/// <summary>
/// MIDI 書き出しで省略できる項目。テンポ・拍子・トラック名・ノーツは常に書き出す。
/// DAW 側の設定と干渉しやすいものを外せるようにしている。
/// </summary>
public sealed record MidiExportOptions
{
    public bool IncludeProgramChange { get; init; } = true;

    /// <summary>コントロールチェンジ（現在は音量 CC7 のみ）</summary>
    public bool IncludeControlChange { get; init; } = true;

    public bool IncludeChordMarkers { get; init; } = true;

    public bool IncludeKeySignature { get; init; } = true;
}

/// <summary>
/// 曲を Standard MIDI File（Format 1）として書き出す。
/// トラック0はコンダクター（テンポ・拍子・調号・コード名のマーカー）、以降は曲のトラックを順に並べる。
/// スマートトラックは生成済みのノーツを書き出すので、呼び出し前に再生成しておくこと。
/// </summary>
public static class MidiExporter
{
    public const string FileExtension = ".mid";

    static MidiExporter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// テキスト系メタイベントの既定の文字コード。Domino など国内の MIDI ソフトに合わせて Shift_JIS。
    /// </summary>
    public static Encoding DefaultTextEncoding => Encoding.GetEncoding("shift_jis");

    public static void Export(Song song, Stream output, MidiExportOptions? options = null, Encoding? textEncoding = null)
    {
        options ??= new MidiExportOptions();
        var encoding = textEncoding ?? DefaultTextEncoding;
        var tracks = new List<SmfTrackBuilder>(song.Tracks.Count + 1) { BuildConductor(song, options, encoding) };

        foreach (var (track, channel) in MidiChannels.Assign(song.Tracks))
            tracks.Add(BuildTrack(track, channel, options, encoding));

        WriteFile(output, song.Ppq, tracks);
    }

    #region トラックの組み立て

    private static SmfTrackBuilder BuildConductor(Song song, MidiExportOptions options, Encoding encoding)
    {
        var builder = new SmfTrackBuilder();
        builder.AddText(0, SmfMeta.TrackName, "Conductor", encoding);
        var conductor = song.Conductor;
        foreach (var tempo in conductor.Tempos)
            builder.AddTempo(tempo.Tick, tempo.Bpm);
        foreach (var meter in conductor.Meters)
            builder.AddTimeSignature(conductor.BarToTick(meter.Bar), meter.BeatsPerBar, 4);
        if (options.IncludeKeySignature)
        {
            foreach (var key in conductor.Keys)
                builder.AddKeySignature(key.Tick, key.Key);
        }

        // コード名は DAW でマーカーとして見えるよう、コンダクターに置く
        if (options.IncludeChordMarkers && song.Tracks.OfType<SmartChordTrack>().FirstOrDefault() is { } chordTrack)
        {
            foreach (var segment in chordTrack.Chords)
                builder.AddText(segment.Start, SmfMeta.Marker, segment.GetName(conductor.KeyAt(segment.Start)), encoding);
        }

        return builder;
    }

    private static SmfTrackBuilder BuildTrack(Track track, int channel, MidiExportOptions options, Encoding encoding)
    {
        var notes = track.PlaybackNotes;
        var builder = new SmfTrackBuilder(notes.Count * 2 + 3);
        builder.AddText(0, SmfMeta.TrackName, track.Name, encoding);
        // ドラムは Standard（0）以外のときだけキットを指定する（GM1 前提の受け手と干渉しないように）
        if (options.IncludeProgramChange && (!track.IsDrum || track.Program != 0))
            builder.AddChannelMessage(0, SmfStatus.ProgramChange, channel, track.Program);
        if (options.IncludeControlChange)
            builder.AddChannelMessage(0, SmfStatus.ControlChange, channel, 7, track.Volume);

        foreach (var note in notes)
        {
            builder.AddChannelMessage(note.Tick, SmfStatus.NoteOn, channel, note.Pitch, Math.Max(1, note.Velocity));
            builder.AddChannelMessage(note.EndTick, SmfStatus.NoteOff, channel, note.Pitch, 0);
        }

        return builder;
    }

    #endregion

    #region ファイル出力

    private static void WriteFile(Stream output, int ppq, List<SmfTrackBuilder> tracks)
    {
        Span<byte> header = stackalloc byte[14];
        "MThd"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], 6);
        BinaryPrimitives.WriteInt16BigEndian(header[8..], 1);
        BinaryPrimitives.WriteInt16BigEndian(header[10..], (short)tracks.Count);
        BinaryPrimitives.WriteInt16BigEndian(header[12..], (short)ppq);
        output.Write(header);

        var buffer = new ArrayBufferWriter<byte>();
        Span<byte> chunkHeader = stackalloc byte[8];
        foreach (var track in tracks)
        {
            buffer.ResetWrittenCount();
            track.WriteTo(buffer);

            "MTrk"u8.CopyTo(chunkHeader);
            BinaryPrimitives.WriteInt32BigEndian(chunkHeader[4..], buffer.WrittenCount);
            output.Write(chunkHeader);
            output.Write(buffer.WrittenSpan);
        }

        output.Flush();
    }

    #endregion
}

/// <summary>
/// チャンネルメッセージのステータス（上位4ビット）。
/// </summary>
internal static class SmfStatus
{
    public const byte NoteOff = 0x80;
    public const byte NoteOn = 0x90;
    public const byte ControlChange = 0xB0;
    public const byte ProgramChange = 0xC0;
    public const byte Meta = 0xFF;
}

/// <summary>
/// メタイベントの種類番号。
/// </summary>
internal static class SmfMeta
{
    public const byte TrackName = 0x03;
    public const byte Marker = 0x06;
    public const byte EndOfTrack = 0x2F;
    public const byte Tempo = 0x51;
    public const byte TimeSignature = 0x58;
    public const byte KeySignature = 0x59;
}

/// <summary>
/// 1トラック分のイベントを絶対 tick で集め、デルタタイム付きのトラックデータとして書き出す。
/// 同じ tick では「メタ・設定 → ノートオフ → ノートオン」の順に並べ、同じ音の連打が途切れないようにする。
/// </summary>
internal sealed class SmfTrackBuilder(int capacity = 16)
{
    private const int OrderSetup = 0;
    private const int OrderNoteOff = 1;
    private const int OrderNoteOn = 2;

    /// <summary>可変長数値の最大バイト数（28ビット）</summary>
    private const int MaxVariableLengthBytes = 4;

    private readonly List<SmfEvent> _events = new(capacity);

    /// <summary>
    /// 1イベント。チャンネルメッセージはデータを直接持ち、メタイベントだけ可変長のペイロードを持つ。
    /// </summary>
    private readonly record struct SmfEvent(
        int Tick, int Order, int Sequence, byte Status, byte Data1, byte Data2, ReadOnlyMemory<byte> Payload)
    {
        public bool IsMeta => Status == SmfStatus.Meta;

        /// <summary>プログラムチェンジ・チャンネルプレッシャーはデータが1バイト</summary>
        public int ChannelDataLength => (Status & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;

        public int MaxEncodedLength => IsMeta ? 2 + MaxVariableLengthBytes + Payload.Length : 1 + ChannelDataLength;
    }

    public void AddText(int tick, byte type, string text, Encoding encoding) =>
        AddMeta(tick, type, encoding.GetBytes(text));

    public void AddTempo(int tick, double bpm)
    {
        var microseconds = (int)Math.Clamp(Math.Round(60_000_000 / bpm), 1, 0xFFFFFF);
        AddMeta(tick, SmfMeta.Tempo, [(byte)(microseconds >> 16), (byte)(microseconds >> 8), (byte)microseconds]);
    }

    /// <param name="denominator">分母（2 の累乗）</param>
    public void AddTimeSignature(int tick, int numerator, int denominator) =>
        AddMeta(tick, SmfMeta.TimeSignature, [(byte)numerator, (byte)int.Log2(denominator), 24, 8]);

    /// <summary>
    /// 調号。シャープ・フラットの数は平行調のメジャーキーから五度圏で求める。
    /// </summary>
    public void AddKeySignature(int tick, Key key)
    {
        var relativeMajor = key.Mode == Mode.Major ? key.Tonic : (key.Tonic + 3) % 12;
        var sharps = relativeMajor * 7 % 12;
        var accidentals = key.PrefersFlats ? sharps - 12 : sharps;
        AddMeta(tick, SmfMeta.KeySignature, [(byte)(sbyte)accidentals, (byte)(key.Mode == Mode.Minor ? 1 : 0)]);
    }

    public void AddChannelMessage(int tick, byte status, int channel, int data1, int data2 = 0)
    {
        var order = status switch
        {
            SmfStatus.NoteOn => OrderNoteOn,
            SmfStatus.NoteOff => OrderNoteOff,
            _ => OrderSetup,
        };
        Add(new SmfEvent(tick, order, 0, (byte)(status | (channel & 0x0F)), Clamp7(data1), Clamp7(data2), default));
    }

    /// <summary>
    /// デルタタイム付きのトラックデータを書き込む。末尾に End of Track を付ける。
    /// </summary>
    public void WriteTo(IBufferWriter<byte> writer)
    {
        _events.Sort(static (a, b) =>
        {
            var c = a.Tick.CompareTo(b.Tick);
            if (c != 0) return c;
            c = a.Order.CompareTo(b.Order);
            return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
        });

        var previous = 0;
        foreach (var e in _events)
        {
            var span = writer.GetSpan(MaxVariableLengthBytes + e.MaxEncodedLength);
            var written = WriteVariableLength(span, e.Tick - previous);
            written += Encode(e, span[written..]);
            writer.Advance(written);
            previous = e.Tick;
        }

        var end = writer.GetSpan(4);
        end[0] = 0x00;
        end[1] = SmfStatus.Meta;
        end[2] = SmfMeta.EndOfTrack;
        end[3] = 0x00;
        writer.Advance(4);
    }

    /// <summary>
    /// SMF の可変長数値（7ビットずつ上位から、継続ビット付き）を書き、書いたバイト数を返す。
    /// </summary>
    internal static int WriteVariableLength(Span<byte> destination, int value)
    {
        if ((uint)value > 0x0FFFFFFF) throw new ArgumentOutOfRangeException(nameof(value));

        Span<byte> reversed = stackalloc byte[MaxVariableLengthBytes];
        var count = 0;
        do
        {
            reversed[count++] = (byte)(value & 0x7F);
            value >>= 7;
        } while (value > 0);

        for (var i = 0; i < count; i++)
            destination[i] = (byte)(reversed[count - 1 - i] | (i < count - 1 ? 0x80 : 0));
        return count;
    }

    private static int Encode(in SmfEvent e, Span<byte> destination)
    {
        destination[0] = e.Status;
        if (!e.IsMeta)
        {
            destination[1] = e.Data1;
            if (e.ChannelDataLength == 1) return 2;
            destination[2] = e.Data2;
            return 3;
        }

        destination[1] = e.Data1;
        var written = 2 + WriteVariableLength(destination[2..], e.Payload.Length);
        e.Payload.Span.CopyTo(destination[written..]);
        return written + e.Payload.Length;
    }

    private void AddMeta(int tick, byte type, byte[] payload) =>
        Add(new SmfEvent(tick, OrderSetup, 0, SmfStatus.Meta, type, 0, payload));

    private void Add(SmfEvent e) => _events.Add(e with { Tick = Math.Max(0, e.Tick), Sequence = _events.Count });

    private static byte Clamp7(int value) => (byte)Math.Clamp(value, 0, 127);
}
