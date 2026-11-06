namespace CircuitBench.TestKit;

/// <summary>測試用的波形工具（與正式量測程式碼獨立）。</summary>
public static class WaveformMath
{
    public static double Interpolate(double[] x, double[] y, double xq)
    {
        if (xq <= x[0]) return y[0];
        if (xq >= x[^1]) return y[^1];
        var i = Array.BinarySearch(x, xq);
        if (i >= 0) return y[i];
        i = ~i;
        var f = (xq - x[i - 1]) / (x[i] - x[i - 1]);
        return y[i - 1] + f * (y[i] - y[i - 1]);
    }

    /// <summary>最小平方法直線斜率。</summary>
    public static double Slope(double[] x, double[] y)
    {
        var mx = x.Average();
        var my = y.Average();
        double num = 0, den = 0;
        for (var i = 0; i < x.Length; i++)
        {
            num += (x[i] - mx) * (y[i] - my);
            den += (x[i] - mx) * (x[i] - mx);
        }

        return num / den;
    }

    /// <summary>局部極值，以通過三點的拋物線內插位置與值。</summary>
    public static IEnumerable<(double Time, double Value)> Extrema(double[] t, double[] v)
    {
        for (var i = 1; i < t.Length - 1; i++)
        {
            var d1 = v[i] - v[i - 1];
            var d2 = v[i + 1] - v[i];
            if (d1 > 0 && d2 <= 0 || d1 < 0 && d2 >= 0)
            {
                yield return ParabolicVertex(t[i - 1], v[i - 1], t[i], v[i], t[i + 1], v[i + 1]);
            }
        }
    }

    private static (double, double) ParabolicVertex(double x0, double y0, double x1, double y1, double x2, double y2)
    {
        var denom = (x0 - x1) * (x0 - x2) * (x1 - x2);
        var a = (x2 * (y1 - y0) + x1 * (y0 - y2) + x0 * (y2 - y1)) / denom;
        var b = (x2 * x2 * (y0 - y1) + x1 * x1 * (y2 - y0) + x0 * x0 * (y1 - y2)) / denom;
        var c = (x1 * x2 * (x1 - x2) * y0 + x2 * x0 * (x2 - x0) * y1 + x0 * x1 * (x0 - x1) * y2) / denom;
        if (a == 0) return (x1, y1);
        var xv = -b / (2 * a);
        return (xv, c - b * b / (4 * a));
    }

    /// <summary>
    /// 計算「逐步正負交替」的次數：連續三個差分方向為 +−+ 或 −+−（每一步都反向），
    /// 且幅度都大於門檻。單一極值（只反向一次）不算。
    /// </summary>
    public static int StepwiseAlternations(double[] v, double threshold)
    {
        var count = 0;
        for (var i = 1; i < v.Length - 2; i++)
        {
            var d1 = v[i] - v[i - 1];
            var d2 = v[i + 1] - v[i];
            var d3 = v[i + 2] - v[i + 1];
            if (d1 * d2 < 0 && d2 * d3 < 0 && Math.Abs(d1) > threshold && Math.Abs(d2) > threshold && Math.Abs(d3) > threshold)
            {
                count++;
            }
        }

        return count;
    }
}
