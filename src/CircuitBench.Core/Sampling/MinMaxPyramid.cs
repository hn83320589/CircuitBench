namespace CircuitBench.Core.Sampling;

/// <summary>一個像素欄內的最小值與最大值；HasData=false 代表該欄沒有任何取樣點。</summary>
public readonly record struct MinMaxColumn(double Min, double Max, bool HasData)
{
    public static MinMaxColumn Empty => new(double.NaN, double.NaN, false);
}

/// <summary>
/// 每條訊號建一次的最小/最大值金字塔（第 k 層每個元素涵蓋 2^k 個取樣點）。
/// 任意 x 範圍與像素寬度的抽樣查詢為 O(W·log N)，且每欄結果等於該欄所有取樣點的
/// 真實最小值與最大值，因此任何縮放程度都不會遺漏尖峰。
/// x 必須為遞增（暫態時間點嚴格遞增）。
/// </summary>
public sealed class MinMaxPyramid
{
    private readonly double[] _x;
    private readonly List<double[]> _min = [];
    private readonly List<double[]> _max = [];

    public MinMaxPyramid(double[] x, double[] y)
    {
        if (x.Length != y.Length)
        {
            throw new ArgumentException("x 與 y 長度不同。");
        }

        _x = x;
        _min.Add(y);
        _max.Add(y);
        var level = 0;
        while (_min[level].Length > 1)
        {
            var prevMin = _min[level];
            var prevMax = _max[level];
            var n = (prevMin.Length + 1) / 2;
            var mn = new double[n];
            var mx = new double[n];
            for (var j = 0; j < n; j++)
            {
                var a = 2 * j;
                var b = Math.Min(a + 1, prevMin.Length - 1);
                mn[j] = Math.Min(prevMin[a], prevMin[b]);
                mx[j] = Math.Max(prevMax[a], prevMax[b]);
            }

            _min.Add(mn);
            _max.Add(mx);
            level++;
        }
    }

    public int Count => _x.Length;

    /// <summary>索引區間 [lo, hi) 的最小與最大值。</summary>
    public (double Min, double Max) RangeMinMax(int lo, int hi)
    {
        var mn = double.PositiveInfinity;
        var mx = double.NegativeInfinity;
        var level = 0;
        while (lo < hi)
        {
            if ((lo & 1) == 1)
            {
                mn = Math.Min(mn, _min[level][lo]);
                mx = Math.Max(mx, _max[level][lo]);
                lo++;
            }

            if ((hi & 1) == 1)
            {
                hi--;
                mn = Math.Min(mn, _min[level][hi]);
                mx = Math.Max(mx, _max[level][hi]);
            }

            lo >>= 1;
            hi >>= 1;
            level++;
        }

        return (mn, mx);
    }

    /// <summary>把 [xMin, xMax] 分成 pixelWidth 欄，第 i 欄涵蓋 [xMin+i·dx, xMin+(i+1)·dx)，最後一欄包含 xMax。</summary>
    public MinMaxColumn[] Decimate(double xMin, double xMax, int pixelWidth)
    {
        var result = new MinMaxColumn[pixelWidth];
        if (pixelWidth <= 0 || !(xMax > xMin) || _x.Length == 0)
        {
            Array.Fill(result, MinMaxColumn.Empty);
            return result;
        }

        var dx = (xMax - xMin) / pixelWidth;
        var lo = LowerBound(xMin);
        for (var i = 0; i < pixelWidth; i++)
        {
            int hi;
            if (i == pixelWidth - 1)
            {
                hi = UpperBound(xMax);
            }
            else
            {
                var edge = xMin + (i + 1) * dx;
                hi = LowerBound(edge, lo);
            }

            if (hi > lo)
            {
                var (mn, mx) = RangeMinMax(lo, hi);
                result[i] = new MinMaxColumn(mn, mx, true);
            }
            else
            {
                result[i] = MinMaxColumn.Empty;
            }

            lo = Math.Max(lo, hi);
        }

        return result;
    }

    /// <summary>暴力計算的對照版本（測試用）：與 Decimate 使用完全相同的欄邊界定義。</summary>
    public static MinMaxColumn[] DecimateBruteForce(double[] x, double[] y, double xMin, double xMax, int pixelWidth)
    {
        var result = new MinMaxColumn[pixelWidth];
        Array.Fill(result, MinMaxColumn.Empty);
        if (pixelWidth <= 0 || !(xMax > xMin))
        {
            return result;
        }

        var dx = (xMax - xMin) / pixelWidth;
        for (var k = 0; k < x.Length; k++)
        {
            if (x[k] < xMin || x[k] > xMax)
            {
                continue;
            }

            var col = -1;
            for (var i = 0; i < pixelWidth; i++)
            {
                var right = xMin + (i + 1) * dx;
                if (i == pixelWidth - 1 || x[k] < right)
                {
                    col = i;
                    break;
                }
            }

            var c = result[col];
            result[col] = c.HasData
                ? new MinMaxColumn(Math.Min(c.Min, y[k]), Math.Max(c.Max, y[k]), true)
                : new MinMaxColumn(y[k], y[k], true);
        }

        return result;
    }

    /// <summary>第一個 x ≥ value 的索引。</summary>
    public int LowerBound(double value, int start = 0)
    {
        int lo = start, hi = _x.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (_x[mid] < value) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    /// <summary>第一個 x &gt; value 的索引。</summary>
    public int UpperBound(double value)
    {
        int lo = 0, hi = _x.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (_x[mid] <= value) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }
}
