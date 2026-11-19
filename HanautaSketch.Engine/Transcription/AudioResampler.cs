namespace HanautaSketch.Engine.Transcription;

/// <summary>
/// 窓付き sinc 補間でサンプルレートを変換する。下げるときは折り返しを防ぐため、変換先のナイキスト周波数の手前で帯域を切る。
/// </summary>
public static class AudioResampler
{
    /// <summary>sinc の片側のゼロ交差の数</summary>
    private const int HalfZeroCrossings = 16;

    /// <summary>カーネルの表の細かさ（入力1サンプルあたりの点数）</summary>
    private const int TableResolution = 256;

    public static float[] Resample(float[] input, int fromRate, int toRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toRate);
        if (fromRate == toRate) return (float[])input.Clone();

        var ratio = (double)toRate / fromRate;
        var cutoff = Math.Min(1.0, ratio) * 0.95;
        var halfWidth = HalfZeroCrossings / cutoff;
        var table = BuildKernel(cutoff, halfWidth);
        var output = new float[(int)Math.Floor(input.Length * ratio)];

        for (var n = 0; n < output.Length; n++)
        {
            var center = n / ratio;
            var first = Math.Max(0, (int)Math.Ceiling(center - halfWidth));
            var last = Math.Min(input.Length - 1, (int)Math.Floor(center + halfWidth));
            var sum = 0.0;
            for (var k = first; k <= last; k++)
            {
                // 表を線形補間で引く
                var position = Math.Abs(center - k) * TableResolution;
                var index = (int)position;
                if (index + 1 >= table.Length) continue;
                var fraction = position - index;
                sum += input[k] * (table[index] + (table[index + 1] - table[index]) * fraction);
            }
            output[n] = (float)sum;
        }
        return output;
    }

    /// <summary>窓付き sinc の片側（0〜halfWidth）を表にする</summary>
    private static double[] BuildKernel(double cutoff, double halfWidth)
    {
        var table = new double[(int)Math.Ceiling(halfWidth * TableResolution) + 2];
        for (var i = 0; i < table.Length; i++)
        {
            var x = (double)i / TableResolution;
            table[i] = cutoff * Sinc(cutoff * x) * Window(x / halfWidth);
        }
        return table;
    }

    private static double Sinc(double x) => x == 0 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);

    /// <summary>Blackman 窓（-1〜1 の範囲で定義）</summary>
    private static double Window(double x)
    {
        if (Math.Abs(x) >= 1) return 0;
        var t = (x + 1) / 2;
        return 0.42 - 0.5 * Math.Cos(2 * Math.PI * t) + 0.08 * Math.Cos(4 * Math.PI * t);
    }
}
