using HanautaSketch.Engine.Audio;
using HanautaSketch.Resources;
using Silk.NET.OpenAL;

namespace HanautaSketch.Audio;

/// <summary>
/// <see cref="PlaybackEngine"/> の出力を OpenAL のバッファキューで鳴らす（macOS など Windows 以外用）。
/// 専用スレッドでバッファを順に埋めて流し続ける。
/// </summary>
public sealed unsafe class OpenAlAudioOutput : IAudioOutput
{
    private const int SampleRate = 44100;
    private const int BufferCount = 4;
    private const int FramesPerBuffer = 1024;

    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly Device* _device;
    private readonly Context* _context;
    private readonly uint _source;
    private readonly uint[] _buffers;
    private readonly Thread _thread;
    private volatile bool _running = true;

    private OpenAlAudioOutput(PlaybackEngine engine, AL al, ALContext alc, Device* device, Context* context, uint source, uint[] buffers)
    {
        Engine = engine;
        _al = al;
        _alc = alc;
        _device = device;
        _context = context;
        _source = source;
        _buffers = buffers;
        _thread = new Thread(PumpLoop) { IsBackground = true, Name = "OpenAL output" };
    }

    public PlaybackEngine Engine { get; }

    /// <summary>
    /// 既定の出力デバイスを開き、エンジンを作って出力を開始する。SF2 の読み込みは重いので別スレッドで呼ぶ。
    /// </summary>
    public static OpenAlAudioOutput Create(string soundFontPath)
    {
        var alc = ALContext.GetApi(soft: true);
        var al = AL.GetApi(soft: true);

        var device = alc.OpenDevice(null);
        if (device == null) throw new InvalidOperationException(Strings.AudioDeviceOpenFailed);
        var context = alc.CreateContext(device, null);
        alc.MakeContextCurrent(context);

        var engine = new PlaybackEngine(soundFontPath, SampleRate);
        var source = al.GenSource();
        var buffers = al.GenBuffers(BufferCount);
        var output = new OpenAlAudioOutput(engine, al, alc, device, context, source, buffers);

        // 無音で満たしてから再生を始め、あとはスレッドが埋め続ける
        foreach (var buffer in buffers) output.Fill(buffer);
        fixed (uint* p = buffers) al.SourceQueueBuffers(source, BufferCount, p);
        al.SourcePlay(source);
        output._thread.Start();
        return output;
    }

    private void PumpLoop()
    {
        while (_running)
        {
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out var processed);
            while (processed-- > 0 && _running)
            {
                var buffer = 0u;
                _al.SourceUnqueueBuffers(_source, 1, &buffer);
                Fill(buffer);
                _al.SourceQueueBuffers(_source, 1, &buffer);
            }

            // アンダーランで止まっていたら再開する
            _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out var state);
            if (state != (int)SourceState.Playing && _running) _al.SourcePlay(_source);

            Thread.Sleep(5);
        }
    }

    private float[] _left = new float[FramesPerBuffer];
    private float[] _right = new float[FramesPerBuffer];
    private readonly short[] _pcm = new short[FramesPerBuffer * 2];

    /// <summary>エンジンから1バッファ分を引き出し、16bit ステレオにして OpenAL に渡す。</summary>
    private void Fill(uint buffer)
    {
        Engine.Render(_left, _right);
        for (var i = 0; i < FramesPerBuffer; i++)
        {
            _pcm[i * 2] = ToPcm(_left[i]);
            _pcm[i * 2 + 1] = ToPcm(_right[i]);
        }

        fixed (short* p = _pcm)
            _al.BufferData(buffer, BufferFormat.Stereo16, p, _pcm.Length * sizeof(short), SampleRate);
    }

    private static short ToPcm(float sample) => (short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue);

    public void Dispose()
    {
        _running = false;
        _thread.Join();
        _al.SourceStop(_source);
        _al.DeleteSource(_source);
        _al.DeleteBuffers(_buffers);
        _alc.MakeContextCurrent(null);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
        _al.Dispose();
        _alc.Dispose();
    }
}
