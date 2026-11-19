using HanautaSketch.Resources;
using Silk.NET.OpenAL;
using Silk.NET.OpenAL.Extensions.EXT;
using Silk.NET.OpenAL.Extensions.EXT.Enumeration;

namespace HanautaSketch.Audio;

/// <summary>
/// OpenAL のキャプチャ（ALC_EXT_CAPTURE）で、モノラル 16bit の録音を取り込む。
/// 専用スレッドでデバイスのバッファから引き取り、float にして溜める。
/// </summary>
public sealed unsafe class OpenAlAudioInput : IAudioInput
{
    private const int Rate = 44100;

    /// <summary>デバイス側のバッファ（サンプル数）。引き取りが遅れても溢れないよう 1 秒分取る</summary>
    private const int DeviceBufferSamples = Rate;

    private readonly ALContext _alc;
    private readonly Capture _capture;
    private readonly Device* _device;
    private readonly object _lock = new();
    private readonly List<float> _samples = [];
    private readonly short[] _chunk = new short[DeviceBufferSamples];
    private readonly Thread _thread;
    private volatile bool _running = true;
    private bool _closed;

    private OpenAlAudioInput(ALContext alc, Capture capture, Device* device)
    {
        _alc = alc;
        _capture = capture;
        _device = device;
        _thread = new Thread(PumpLoop) { IsBackground = true, Name = "OpenAL capture" };
    }

    public int SampleRate => Rate;

    public static IReadOnlyList<string> GetDeviceNames()
    {
        using var alc = ALContext.GetApi(soft: true);
        var enumeration = new CaptureEnumerationEnumeration(alc.Context);
        return enumeration.GetStringList(GetCaptureContextStringList.CaptureDeviceSpecifiers).ToList();
    }

    public static OpenAlAudioInput Start(string? deviceName)
    {
        var alc = ALContext.GetApi(soft: true);
        var capture = new Capture(alc.Context);
        var name = deviceName is not null && GetDeviceNames().Contains(deviceName) ? deviceName : null;
        var device = capture.CaptureOpenDevice(name, Rate, BufferFormat.Mono16, DeviceBufferSamples);
        if (device == null)
        {
            alc.Dispose();
            throw new InvalidOperationException(Strings.RecordingDeviceOpenFailed);
        }

        var input = new OpenAlAudioInput(alc, capture, device);
        capture.CaptureStart(device);
        input._thread.Start();
        return input;
    }

    public long CapturedSampleCount
    {
        get
        {
            lock (_lock)
            {
                return _samples.Count + (_closed ? 0 : _capture.GetAvailableSamples(_device));
            }
        }
    }

    public float[] CopySamples(int from)
    {
        lock (_lock)
        {
            from = Math.Clamp(from, 0, _samples.Count);
            return _samples.GetRange(from, _samples.Count - from).ToArray();
        }
    }

    public float[] Stop()
    {
        StopThread();
        lock (_lock)
        {
            if (!_closed)
            {
                Pull();
                _capture.CaptureStop(_device);
            }
            return _samples.ToArray();
        }
    }

    private void PumpLoop()
    {
        while (_running)
        {
            lock (_lock)
            {
                if (!_closed) Pull();
            }
            Thread.Sleep(5);
        }
    }

    /// <summary>デバイスに溜まった分を引き取る（ロック内で呼ぶ）</summary>
    private void Pull()
    {
        var available = Math.Min(_capture.GetAvailableSamples(_device), _chunk.Length);
        if (available <= 0) return;
        fixed (short* p = _chunk) _capture.CaptureSamples(_device, p, available);
        for (var i = 0; i < available; i++) _samples.Add(_chunk[i] / 32768f);
    }

    private void StopThread()
    {
        _running = false;
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join();
    }

    public void Dispose()
    {
        StopThread();
        lock (_lock)
        {
            if (_closed) return;
            _closed = true;
            _capture.CaptureStop(_device);
            _capture.CaptureCloseDevice(_device);
        }
        _alc.Dispose();
    }
}
