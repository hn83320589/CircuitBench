using CircuitBench.Core.Sampling;
using CircuitBench.TestKit.Property;

namespace CircuitBench.Core.Tests.Sampling;

public class MinMaxPyramidTests
{
    private static (double[] X, double[] Y) RandomSignal(Random rng, int n)
    {
        var x = new double[n];
        var y = new double[n];
        var t = rng.NextDouble();
        for (var i = 0; i < n; i++)
        {
            t += Math.Pow(10, -3 + rng.NextDouble() * 3); // 非均勻間隔
            x[i] = t;
            y[i] = Math.Sin(t) + (rng.NextDouble() < 0.001 ? (rng.NextDouble() - 0.5) * 100 : 0); // 偶發尖峰
        }

        return (x, y);
    }

    [Fact]
    public void Property_DecimationEqualsBruteForceForAnyRangeAndWidth()
    {
        PropertyTest.ForAll(300, (rng, _) =>
        {
            var n = rng.Next(1, 5000);
            var (x, y) = RandomSignal(rng, n);
            var pyramid = new MinMaxPyramid(x, y);
            var a = x[0] + (x[^1] - x[0]) * (rng.NextDouble() * 1.2 - 0.1);
            var b = x[0] + (x[^1] - x[0]) * (rng.NextDouble() * 1.2 - 0.1);
            var (lo, hi) = a < b ? (a, b) : (b, a);
            if (hi == lo)
            {
                hi = lo + 1;
            }

            // 偶爾把範圍邊界放在取樣點上
            if (rng.Next(4) == 0)
            {
                lo = x[rng.Next(n)];
                hi = Math.Max(lo + 1e-9, x[rng.Next(n)]);
            }

            var width = rng.Next(1, 2000);
            var fast = pyramid.Decimate(lo, hi, width);
            var slow = MinMaxPyramid.DecimateBruteForce(x, y, lo, hi, width);
            Assert.Equal(slow.Length, fast.Length);
            for (var i = 0; i < width; i++)
            {
                Assert.Equal(slow[i].HasData, fast[i].HasData);
                if (slow[i].HasData)
                {
                    Assert.Equal(slow[i].Min, fast[i].Min);
                    Assert.Equal(slow[i].Max, fast[i].Max);
                }
            }
        });
    }

    [Fact]
    public void SingleSampleSpikeIsNeverLostAtAnyZoom()
    {
        const int n = 1_000_000;
        var x = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        var y = new double[n];
        y[654_321] = 42;
        y[123] = -7;
        var pyramid = new MinMaxPyramid(x, y);
        foreach (var width in new[] { 1, 3, 100, 800, 1920 })
        {
            var cols = pyramid.Decimate(0, n - 1, width);
            Assert.Equal(42, cols.Max(c => c.Max));
            Assert.Equal(-7, cols.Min(c => c.Min));
        }

        var zoomed = pyramid.Decimate(654_000, 655_000, 500);
        Assert.Equal(42, zoomed.Max(c => c.Max));
    }

    [Fact]
    public void Property_RangeMinMaxMatchesLinq()
    {
        PropertyTest.ForAll(200, (rng, _) =>
        {
            var n = rng.Next(1, 3000);
            var (x, y) = RandomSignal(rng, n);
            var pyramid = new MinMaxPyramid(x, y);
            var a = rng.Next(n);
            var b = rng.Next(a, n) + 1;
            var (mn, mx) = pyramid.RangeMinMax(a, b);
            Assert.Equal(y[a..b].Min(), mn);
            Assert.Equal(y[a..b].Max(), mx);
        });
    }

    [Fact]
    public void ColumnsWithoutSamplesAreMarkedEmpty()
    {
        var pyramid = new MinMaxPyramid([0.0, 10.0], [1.0, 2.0]);
        var cols = pyramid.Decimate(0, 10, 10);
        Assert.True(cols[0].HasData);
        Assert.True(cols[9].HasData);
        Assert.All(cols[1..9], c => Assert.False(c.HasData));
    }

    [Fact]
    public void InvalidRangeReturnsEmptyColumns()
    {
        var pyramid = new MinMaxPyramid([0.0, 1.0], [1.0, 2.0]);
        Assert.All(pyramid.Decimate(1, 1, 5), c => Assert.False(c.HasData));
    }
}
