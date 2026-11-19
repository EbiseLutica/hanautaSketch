using System.Buffers.Binary;

namespace HanautaSketch.Engine.IO;

/// <summary>
/// WAV のサンプル形式。
/// </summary>
public enum WavSampleFormat
{
    Pcm16,
    Pcm24,
    Float32,
}

/// <summary>
/// fmt チャンクの形式コード（<see cref="WavReader"/> と共有する）。
/// </summary>
internal static class WavFormatCodes
{
    public const ushort Pcm = 1;
    public const ushort Float = 3;
    public const ushort Extensible = 0xFFFE;
}

/// <summary>
/// ステレオの WAV を、鳴らしながら少しずつ書く（WAV 書き出し用）。長さは先に決めてヘッダに書くので、シークできないストリームにも書ける。
/// PCM は -1〜1 に収め、float はそのまま（NaN・無限大は 0）書く。
/// </summary>
public sealed class WavStreamWriter
{
    private const int Channels = 2;

    private readonly Stream _stream;
    private readonly WavSampleFormat _format;
    private readonly int _bytesPerSample;
    private readonly long _frames;
    private long _written;
    private byte[] _buffer = [];

    /// <summary>ヘッダを書き込む。</summary>
    /// <param name="frames">これから書くフレーム数</param>
    /// <exception cref="IOException">WAV（RIFF）の上限 4GB を超える</exception>
    public WavStreamWriter(Stream stream, int sampleRate, WavSampleFormat format, long frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        if (!Fits(frames, format)) throw new IOException("The audio is too long for a WAV file.");

        _stream = stream;
        _format = format;
        _frames = frames;
        _bytesPerSample = BytesPerSample(format);

        var isFloat = format == WavSampleFormat.Float32;
        var blockAlign = Channels * _bytesPerSample;
        var dataSize = frames * blockAlign;
        var fmtSize = FmtSize(format);
        var factSize = FactSize(format);

        var header = new byte[HeaderSize(format)];
        var span = header.AsSpan();
        var p = 0;
        WriteTag(span, ref p, "RIFF");
        WriteUInt32(span, ref p, (uint)RiffSize(frames, format));
        WriteTag(span, ref p, "WAVE");
        WriteTag(span, ref p, "fmt ");
        WriteUInt32(span, ref p, (uint)fmtSize);
        WriteUInt16(span, ref p, isFloat ? WavFormatCodes.Float : WavFormatCodes.Pcm);
        WriteUInt16(span, ref p, Channels);
        WriteUInt32(span, ref p, (uint)sampleRate);
        WriteUInt32(span, ref p, (uint)(sampleRate * blockAlign));
        WriteUInt16(span, ref p, (ushort)blockAlign);
        WriteUInt16(span, ref p, (ushort)(_bytesPerSample * 8));
        if (isFloat)
        {
            WriteUInt16(span, ref p, 0);
            WriteTag(span, ref p, "fact");
            WriteUInt32(span, ref p, (uint)(factSize - 8));
            WriteUInt32(span, ref p, (uint)frames);
        }
        WriteTag(span, ref p, "data");
        WriteUInt32(span, ref p, (uint)dataSize);
        _stream.Write(header);
    }

    /// <summary>
    /// 指定の長さが WAV（RIFF）の上限 4GB に収まるか。
    /// </summary>
    public static bool Fits(long frames, WavSampleFormat format) =>
        frames >= 0 && RiffSize(frames, format) <= uint.MaxValue;

    private static int BytesPerSample(WavSampleFormat format) => format switch
    {
        WavSampleFormat.Pcm16 => 2,
        WavSampleFormat.Pcm24 => 3,
        WavSampleFormat.Float32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>float は fmt を 18 バイト（cbSize 付き）にし、fact チャンクを置く（非 PCM の WAV の決まり）</summary>
    private static int FmtSize(WavSampleFormat format) => format == WavSampleFormat.Float32 ? 18 : 16;

    private static int FactSize(WavSampleFormat format) => format == WavSampleFormat.Float32 ? 8 + 4 : 0;

    private static int HeaderSize(WavSampleFormat format) => 12 + 8 + FmtSize(format) + FactSize(format) + 8;

    /// <summary>RIFF チャンクの大きさ（先頭 8 バイトを除くファイル全体）。ステレオなので data は常に偶数長で、詰め物は要らない</summary>
    private static long RiffSize(long frames, WavSampleFormat format) =>
        HeaderSize(format) - 8 + frames * Channels * BytesPerSample(format);

    /// <summary>
    /// 左右のサンプルを書く。left と right は同じ長さにする。
    /// </summary>
    public void Write(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length) throw new ArgumentException("Left and right must have the same length.");
        if (_written + left.Length > _frames) throw new InvalidOperationException("More frames than declared in the header.");

        var size = left.Length * Channels * _bytesPerSample;
        if (_buffer.Length < size) _buffer = new byte[size];
        var span = _buffer.AsSpan(0, size);
        var p = 0;
        for (var i = 0; i < left.Length; i++)
        {
            WriteSample(span, ref p, left[i]);
            WriteSample(span, ref p, right[i]);
        }
        _stream.Write(span);
        _written += left.Length;
    }

    /// <summary>
    /// 書き終える。ヘッダに書いた長さと実際の長さが合っているか確かめる。
    /// </summary>
    public void Complete()
    {
        if (_written != _frames) throw new InvalidOperationException("Fewer frames than declared in the header.");
    }

    private void WriteSample(Span<byte> span, ref int p, float value)
    {
        if (!float.IsFinite(value)) value = 0;
        switch (_format)
        {
            case WavSampleFormat.Pcm16:
                BinaryPrimitives.WriteInt16LittleEndian(span[p..], (short)MathF.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue));
                p += 2;
                break;
            case WavSampleFormat.Pcm24:
                var v = (int)MathF.Round(Math.Clamp(value, -1f, 1f) * 8388607f);
                span[p] = (byte)v;
                span[p + 1] = (byte)(v >> 8);
                span[p + 2] = (byte)(v >> 16);
                p += 3;
                break;
            default:
                BinaryPrimitives.WriteSingleLittleEndian(span[p..], value);
                p += 4;
                break;
        }
    }

    private static void WriteTag(Span<byte> span, ref int p, string tag)
    {
        for (var i = 0; i < 4; i++) span[p + i] = (byte)tag[i];
        p += 4;
    }

    private static void WriteUInt16(Span<byte> span, ref int p, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(span[p..], value);
        p += 2;
    }

    private static void WriteUInt32(Span<byte> span, ref int p, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(span[p..], value);
        p += 4;
    }
}
