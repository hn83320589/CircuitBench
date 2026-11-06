namespace CircuitBench.Core.Measurement;

public static class Interpolation
{
    /// <summary>x 遞增的分段線性內插；範圍外取端點值。</summary>
    public static double At(ReadOnlySpan<double> x, ReadOnlySpan<double> y, double xq)
    {
        if (x.Length == 0)
        {
            return double.NaN;
        }

        if (xq <= x[0])
        {
            return y[0];
        }

        if (xq >= x[^1])
        {
            return y[^1];
        }

        var i = x.BinarySearch(xq);
        if (i >= 0)
        {
            return y[i];
        }

        i = ~i;
        var f = (xq - x[i - 1]) / (x[i] - x[i - 1]);
        return y[i - 1] + f * (y[i] - y[i - 1]);
    }
}

public sealed record MeasurementResult(double Average, double Rms, double PeakToPeak, double Min, double Max, double? Frequency);

/// <summary>
/// 量測區間 [t0, t1] 內的統計量。資料視為分段線性（與波形繪製一致），
/// 平均值與 RMS 以精確積分計算，因此在非均勻時間點上也正確。
/// </summary>
public static class Measurements
{
    public static MeasurementResult Measure(ReadOnlySpan<double> t, ReadOnlySpan<double> v, double t0, double t1)
    {
        if (t1 < t0)
        {
            (t0, t1) = (t1, t0);
        }

        var (ts, vs) = Clip(t, v, t0, t1);
        if (ts.Count == 0)
        {
            return new MeasurementResult(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, null);
        }

        var duration = ts[^1] - ts[0];
        double integral = 0, integralSq = 0;
        for (var i = 1; i < ts.Count; i++)
        {
            var h = ts[i] - ts[i - 1];
            var a = vs[i - 1];
            var b = vs[i];
            integral += h * (a + b) / 2;
            integralSq += h * (a * a + a * b + b * b) / 3;
        }

        var avg = duration > 0 ? integral / duration : vs[0];
        var rms = duration > 0 ? Math.Sqrt(Math.Max(0, integralSq / duration)) : Math.Abs(vs[0]);
        var min = vs.Min();
        var max = vs.Max();
        return new MeasurementResult(avg, rms, max - min, min, max, Frequency(ts, vs, avg));
    }

    /// <summary>
    /// 以「減去平均值後的上升零交越」估計頻率：每個交越時間以線性內插求得，
    /// 頻率 = (交越數 − 1) / (最後交越 − 第一個交越)。少於兩個交越時回傳 null。
    /// </summary>
    public static double? Frequency(IReadOnlyList<double> t, IReadOnlyList<double> v, double level)
    {
        var crossings = new List<double>();
        for (var i = 1; i < t.Count; i++)
        {
            var a = v[i - 1] - level;
            var b = v[i] - level;
            if (a < 0 && b >= 0)
            {
                crossings.Add(t[i - 1] + (t[i] - t[i - 1]) * (-a) / (b - a));
            }
        }

        if (crossings.Count < 2)
        {
            return null;
        }

        return (crossings.Count - 1) / (crossings[^1] - crossings[0]);
    }

    /// <summary>取出 [t0,t1] 內的點，並在兩端加入內插點。</summary>
    private static (List<double> T, List<double> V) Clip(ReadOnlySpan<double> t, ReadOnlySpan<double> v, double t0, double t1)
    {
        var ts = new List<double>();
        var vs = new List<double>();
        if (t.Length == 0 || t1 < t[0] || t0 > t[^1])
        {
            return (ts, vs);
        }

        t0 = Math.Max(t0, t[0]);
        t1 = Math.Min(t1, t[^1]);
        ts.Add(t0);
        vs.Add(Interpolation.At(t, v, t0));
        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] > t0 && t[i] < t1)
            {
                ts.Add(t[i]);
                vs.Add(v[i]);
            }
        }

        if (t1 > t0)
        {
            ts.Add(t1);
            vs.Add(Interpolation.At(t, v, t1));
        }

        return (ts, vs);
    }
}
