using System.Text;
using MeltySynth;

namespace HanautaSketch.Tests.IO;

/// <summary>
/// テスト用の最小の SoundFont を組み立てる。正弦波のサンプル1つを、プリセット1つ・インストゥルメント1つで全音域に割り当てる。
/// SF2 はリポジトリに置けないので、実際に鳴らすテストはこれを使う。
/// </summary>
internal static class TestSoundFont
{
    private const int SampleCount = 1000;

    public static SoundFont Create() => new(new MemoryStream(Build()));

    private static byte[] Build()
    {
        // サンプルの後ろに 46 個の 0 を置く（SF2 の決まり）
        var smpl = Bytes(w =>
        {
            for (var i = 0; i < SampleCount + 46; i++) w.Write((short)(i < SampleCount ? 8000 * Math.Sin(i * 0.1) : 0));
        });
        var info = List("INFO",
            Chunk("ifil", Bytes(w => { w.Write((ushort)2); w.Write((ushort)1); })),
            Chunk("INAM", "T\0\0\0"u8.ToArray()));
        var sdta = List("sdta", Chunk("smpl", smpl));

        var phdr = Bytes(w =>
        {
            PresetHeader(w, "P", bagIndex: 0);
            PresetHeader(w, "EOP", bagIndex: 1);
        });
        var pbag = Bytes(w => { Bag(w, 0); Bag(w, 1); });
        var pgen = Bytes(w =>
        {
            Generator(w, 41, 0); // instrument = 0
            Generator(w, 0, 0); // 終端
        });
        var inst = Bytes(w =>
        {
            Name(w, "I");
            w.Write((ushort)0);
            Name(w, "EOI");
            w.Write((ushort)1);
        });
        // 終端の genNdx はジェネレーターの総数にする（でないとゾーンがグローバル扱いになり鳴らない）
        var ibag = Bytes(w => { Bag(w, 0); Bag(w, 2); });
        var igen = Bytes(w =>
        {
            Generator(w, 54, 1); // sampleModes = ループ
            Generator(w, 53, 0); // sampleID = 0
            Generator(w, 0, 0); // 終端
        });
        var shdr = Bytes(w =>
        {
            Name(w, "S");
            w.Write(0);
            w.Write(SampleCount);
            w.Write(100);
            w.Write(900);
            w.Write(44100);
            w.Write((byte)60);
            w.Write((sbyte)0);
            w.Write((ushort)0);
            w.Write((ushort)1);
            Name(w, "EOS");
            w.Write(new byte[26]);
        });
        var pdta = List("pdta",
            Chunk("phdr", phdr), Chunk("pbag", pbag), Chunk("pmod", new byte[10]), Chunk("pgen", pgen),
            Chunk("inst", inst), Chunk("ibag", ibag), Chunk("imod", new byte[10]), Chunk("igen", igen),
            Chunk("shdr", shdr));
        return Chunk("RIFF", [.. "sfbk"u8, .. info, .. sdta, .. pdta]);
    }

    private static void PresetHeader(BinaryWriter w, string name, ushort bagIndex)
    {
        Name(w, name);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write(bagIndex);
        w.Write(0);
        w.Write(0);
        w.Write(0);
    }

    private static void Bag(BinaryWriter w, ushort genIndex)
    {
        w.Write(genIndex);
        w.Write((ushort)0);
    }

    private static void Generator(BinaryWriter w, ushort type, short amount)
    {
        w.Write(type);
        w.Write(amount);
    }

    private static void Name(BinaryWriter w, string name)
    {
        var bytes = new byte[20];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        w.Write(bytes);
    }

    private static byte[] Chunk(string id, byte[] body)
    {
        return Bytes(w =>
        {
            w.Write(Encoding.ASCII.GetBytes(id));
            w.Write(body.Length);
            w.Write(body);
            if ((body.Length & 1) != 0) w.Write((byte)0);
        });
    }

    private static byte[] List(string type, params byte[][] chunks) =>
        Chunk("LIST", [.. Encoding.ASCII.GetBytes(type), .. chunks.SelectMany(c => c)]);

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) write(writer);
        return stream.ToArray();
    }
}
