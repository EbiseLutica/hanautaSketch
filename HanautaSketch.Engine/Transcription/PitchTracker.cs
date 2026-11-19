namespace HanautaSketch.Engine.Transcription;

/// <summary>
/// ピッチ検出の1フレーム分の結果。
/// </summary>
/// <param name="Time">フレーム中心の時刻（秒、音声の先頭から）</param>
/// <param name="Midi">基本周波数を MIDI ノート番号（小数）にしたもの。無声なら null</param>
/// <param name="Rms">音量（RMS）</param>
/// <param name="Aperiodicity">YIN の正規化差分の最小値。0 に近いほど周期的</param>
public readonly record struct PitchFrame(double Time, double? Midi, double Rms, double Aperiodicity)
{
    public bool IsVoiced => Midi.HasValue;
}

/// <summary>
/// YIN（de Cheveigné &amp; Kawahara, 2002）で単音の基本周波数を追う。
/// 処理を軽くするため、入力は <see cref="AnalysisRate"/> に変換してから解析する。
/// </summary>
public static class PitchTracker
{
    /// <summary>解析時のサンプルレート</summary>
    public const int AnalysisRate = 16000;

    /// <summary>フレームの間隔（10ms）</summary>
    public const int HopSize = 160;

    /// <summary>差分を取る窓の長さ（32ms）</summary>
    private const int WindowSize = 512;

    /// <summary>検出する基本周波数の下限（Hz）。男声の鼻歌の低い方まで</summary>
    public const double MinFrequency = 65;

    /// <summary>検出する基本周波数の上限（Hz）</summary>
    public const double MaxFrequency = 1100;

    /// <summary>YIN の絶対しきい値</summary>
    private const double Threshold = 0.15;

    /// <summary>これを超える非周期性のフレームは無声とみなす</summary>
    private const double MaxAperiodicity = 0.35;

    /// <summary>最大音量からこれだけ下回るフレームは無声とみなす（dB）</summary>
    private const double SilenceBelowPeakDb = 35;

    /// <summary>音量の絶対的な下限（これ未満は録音のノイズとみなす）</summary>
    private const double SilenceFloorRms = 0.003;

    public static IReadOnlyList<PitchFrame> Track(float[] samples, int sampleRate)
    {
        var x = AudioResampler.Resample(samples, sampleRate, AnalysisRate);
        var minTau = (int)Math.Floor(AnalysisRate / MaxFrequency);
        var maxTau = (int)Math.Ceiling(AnalysisRate / MinFrequency);
        var span = WindowSize + maxTau;

        var raw = new List<(double Time, double? Midi, double Rms, double Aperiodicity)>();
        var diff = new double[maxTau + 1];
        var cmnd = new double[maxTau + 1];
        for (var start = 0; start + span <= x.Length; start += HopSize)
        {
            var time = (start + WindowSize / 2.0) / AnalysisRate;
            var rms = Rms(x, start, WindowSize);
            Difference(x, start, maxTau, diff);
            CumulativeMeanNormalize(diff, cmnd);
            var (tau, aperiodicity) = PickPeriod(cmnd, minTau, maxTau);
            double? midi = tau > 0 ? FrequencyToMidi(AnalysisRate / Refine(cmnd, tau, maxTau)) : null;
            raw.Add((time, midi, rms, aperiodicity));
        }

        // 音量と周期性で有声・無声を決める
        var peak = raw.Count > 0 ? raw.Max(f => f.Rms) : 0;
        var floor = Math.Max(SilenceFloorRms, peak * Math.Pow(10, -SilenceBelowPeakDb / 20));
        return raw.Select(f =>
        {
            var voiced = f.Midi.HasValue && f.Rms >= floor && f.Aperiodicity <= MaxAperiodicity;
            return new PitchFrame(f.Time, voiced ? f.Midi : null, f.Rms, f.Aperiodicity);
        }).ToList();
    }

    public static double FrequencyToMidi(double frequency) => 69 + 12 * Math.Log2(frequency / 440);

    public static double MidiToFrequency(double midi) => 440 * Math.Pow(2, (midi - 69) / 12);

    private static double Rms(float[] x, int start, int length)
    {
        var sum = 0.0;
        for (var i = start; i < start + length; i++) sum += x[i] * x[i];
        return Math.Sqrt(sum / length);
    }

    /// <summary>差分関数 d(τ) = Σ (x[j] - x[j+τ])²</summary>
    private static void Difference(float[] x, int start, int maxTau, double[] diff)
    {
        diff[0] = 0;
        for (var tau = 1; tau <= maxTau; tau++)
        {
            var sum = 0.0;
            for (var j = start; j < start + WindowSize; j++)
            {
                var d = x[j] - x[j + tau];
                sum += d * d;
            }
            diff[tau] = sum;
        }
    }

    /// <summary>累積平均で正規化した差分関数 d'(τ)</summary>
    private static void CumulativeMeanNormalize(double[] diff, double[] cmnd)
    {
        cmnd[0] = 1;
        var running = 0.0;
        for (var tau = 1; tau < diff.Length; tau++)
        {
            running += diff[tau];
            cmnd[tau] = running > 0 ? diff[tau] * tau / running : 1;
        }
    }

    /// <summary>
    /// しきい値を下回った最初の谷を周期とする。下回らなければ最小値の位置を返す（有声かどうかは非周期性で決める）。
    /// </summary>
    private static (int Tau, double Aperiodicity) PickPeriod(double[] cmnd, int minTau, int maxTau)
    {
        for (var tau = minTau; tau <= maxTau; tau++)
        {
            if (cmnd[tau] >= Threshold) continue;
            while (tau + 1 <= maxTau && cmnd[tau + 1] < cmnd[tau]) tau++;
            return (tau, cmnd[tau]);
        }

        var best = minTau;
        for (var tau = minTau + 1; tau <= maxTau; tau++)
        {
            if (cmnd[tau] < cmnd[best]) best = tau;
        }
        return (best, cmnd[best]);
    }

    /// <summary>放物線補間で周期を小数精度にする</summary>
    private static double Refine(double[] cmnd, int tau, int maxTau)
    {
        if (tau <= 1 || tau >= maxTau) return tau;
        var (a, b, c) = (cmnd[tau - 1], cmnd[tau], cmnd[tau + 1]);
        var denominator = a - 2 * b + c;
        return denominator == 0 ? tau : tau + 0.5 * (a - c) / denominator;
    }
}
