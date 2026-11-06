using System.Numerics;
using System.Text;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.Bode;
using CircuitBench.Core.IO;
using CircuitBench.Core.Measurement;
using CircuitBench.TestKit.Property;

namespace CircuitBench.Core.Tests.Measurement;

public class MeasurementTests
{
    private static double[] NonUniformTimes(Random rng, double t0, double t1, int n)
    {
        var cuts = Enumerable.Range(0, n - 2).Select(_ => t0 + rng.NextDouble() * (t1 - t0)).Append(t0).Append(t1).Distinct().Order().ToArray();
        return cuts;
    }

    [Fact]
    public void Property_AverageAndRmsOfPiecewiseLinearAreExactOnNonUniformGrid()
    {
        PropertyTest.ForAll(200, (rng, _) =>
        {
            var t = NonUniformTimes(rng, 0, 1 + rng.NextDouble(), rng.Next(3, 400));
            var v = t.Select(_ => rng.NextDouble() * 10 - 5).ToArray();
            var r = Measurements.Measure(t, v, t[0], t[^1]);

            // 以極細的子分割數值積分做對照
            double sum = 0, sumSq = 0;
            for (var i = 1; i < t.Length; i++)
            {
                const int sub = 64;
                var h = (t[i] - t[i - 1]) / sub;
                for (var k = 0; k < sub; k++)
                {
                    var f = (k + 0.5) / sub;
                    var val = v[i - 1] + f * (v[i] - v[i - 1]);
                    sum += val * h;
                    sumSq += val * val * h;
                }
            }

            var duration = t[^1] - t[0];
            Assert.Equal(sum / duration, r.Average, 1e-9);
            Assert.Equal(Math.Sqrt(sumSq / duration), r.Rms, 1e-3);
            Assert.Equal(v.Max() - v.Min(), r.PeakToPeak, 12);
        });
    }

    [Fact]
    public void SineMeasurementsMatchAnalyticValues()
    {
        var rng = new Random(1);
        const double f = 50, amp = 3, offset = 1;
        var t = NonUniformTimes(rng, 0, 0.2, 20000);
        var v = t.Select(x => offset + amp * Math.Sin(2 * Math.PI * f * x + 0.3)).ToArray();
        var r = Measurements.Measure(t, v, 0, 0.2); // 剛好 10 個週期
        Assert.Equal(offset, r.Average, 1e-4);
        Assert.Equal(Math.Sqrt(offset * offset + amp * amp / 2), r.Rms, 1e-4);
        Assert.Equal(2 * amp, r.PeakToPeak, 1e-3);
        Assert.NotNull(r.Frequency);
        Assert.Equal(f, r.Frequency!.Value, 1e-3);
    }

    [Fact]
    public void Property_FrequencyFromInterpolatedZeroCrossings()
    {
        PropertyTest.ForAll(50, (rng, _) =>
        {
            var f = Math.Pow(10, rng.NextDouble() * 4);
            var periods = 3 + rng.Next(20);
            var t = NonUniformTimes(rng, 0, periods / f, 60 * periods);
            var v = t.Select(x => Math.Sin(2 * Math.PI * f * x + rng.NextDouble() * 0)).ToArray();
            var measured = Measurements.Frequency(t, v, 0);
            Assert.NotNull(measured);
            Assert.True(Math.Abs(measured!.Value / f - 1) < 2e-3, $"f={f} measured={measured}");
        });
    }

    [Fact]
    public void MeasurementWindowUsesInterpolatedEndpoints()
    {
        double[] t = [0, 1, 2];
        double[] v = [0, 10, 0];
        var r = Measurements.Measure(t, v, 0.5, 1.5);
        Assert.Equal(7.5, r.Average, 12); // ∫ 從 0.5 到 1.5 的三角形中段
        Assert.Equal(5, r.PeakToPeak, 12);
    }

    [Fact]
    public void CursorReadoutIsInterpolated()
    {
        double[] t = [0, 1, 3];
        double[] v = [0, 10, 30];
        Assert.Equal(5, Interpolation.At(t, v, 0.5), 12);
        Assert.Equal(20, Interpolation.At(t, v, 2), 12);
        Assert.Equal(0, Interpolation.At(t, v, -1));
        Assert.Equal(30, Interpolation.At(t, v, 99));
    }

    [Fact]
    public void FrequencyIsNullWithoutTwoCrossings()
    {
        Assert.Null(Measurements.Measure([0.0, 1.0], [1.0, 2.0], 0, 1).Frequency);
    }
}

public class BodeTests
{
    [Fact]
    public void MagnitudeInDecibels()
    {
        var db = BodeMath.MagnitudeDb([new Complex(10, 0), new Complex(0, 0.1), new Complex(1 / Math.Sqrt(2), 0)]);
        Assert.Equal(20, db[0], 12);
        Assert.Equal(-20, db[1], 12);
        Assert.Equal(-3.0103, db[2], 4);
    }

    [Fact]
    public void PhaseIsUnwrappedWithoutJumps()
    {
        // 三階低通：相位由 0 一路降到 −270°，原始 atan2 會在 −180° 折返到 +180°
        var h = Enumerable.Range(0, 400).Select(i =>
        {
            var w = Math.Pow(10, -2 + i * 0.01);
            var s = new Complex(0, w);
            return 1 / ((s + 1) * (s + 1) * (s + 1));
        }).ToArray();
        var phase = BodeMath.UnwrappedPhaseDegrees(h);
        for (var i = 1; i < phase.Length; i++)
        {
            Assert.True(Math.Abs(phase[i] - phase[i - 1]) < 10, $"相位在 {i} 跳動 {phase[i - 1]} → {phase[i]}");
        }

        var wLast = Math.Pow(10, -2 + 399 * 0.01);
        Assert.Equal(-3 * Math.Atan(wLast) * 180 / Math.PI, phase[^1], 9);
        Assert.True(phase.Min() < -180);
    }

    [Fact]
    public void LogAxisTicksAtDecadesWithMinorTicks()
    {
        var ticks = AxisTicks.Log10(10, 1e4);
        var majors = ticks.Where(t => t.IsMajor).Select(t => t.Value).ToArray();
        Assert.Equal([10.0, 100, 1000, 10000], majors);
        Assert.Equal(1 + 3 * 9, ticks.Count);
        Assert.Contains(ticks, t => t.Value == 2000 && !t.IsMajor);
        Assert.Equal("1kHz", ticks.First(t => t.Value == 1000).Label);
    }

    [Fact]
    public void LogAxisClipsPartialDecades()
    {
        var ticks = AxisTicks.Log10(35, 420);
        Assert.Equal(40, ticks[0].Value);
        Assert.Equal(400, ticks[^1].Value);
    }

    [Fact]
    public void LinearAxisUsesNiceSteps()
    {
        var ticks = AxisTicks.Linear(0, 0.97, 6);
        Assert.Equal([0.0, 0.2, 0.4, 0.6, 0.8], ticks.Select(t => Math.Round(t.Value, 12)).ToArray());
    }
}

public class CsvExporterTests
{
    [Fact]
    public void CsvHasBomHeaderAndRoundTripNumbers()
    {
        var data = new RealSignalSet([0, 1e-3, 0.1 + 0.2], new Dictionary<SignalId, double[]>
        {
            [SignalId.V("out")] = [1.0 / 3, -2, 5e-300],
            [SignalId.I("R1")] = [0, 1, 2],
        }, "時間 (s)");
        using var ms = new MemoryStream();
        CsvExporter.Write(ms, data, [SignalId.V("out"), SignalId.I("R1")]);
        var bytes = ms.ToArray();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("時間 (s),V(out),I(R1)", lines[0]);
        Assert.Equal(4, lines.Length);
        var cells = lines[1].Split(',');
        Assert.Equal(1.0 / 3, double.Parse(cells[1], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0.1 + 0.2, double.Parse(lines[3].Split(',')[0], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AcCsvContainsDbAndPhase()
    {
        var data = new ComplexSignalSet([1, 10], new Dictionary<SignalId, Complex[]> { [SignalId.V("o")] = [new(1, 0), new(0, -10)] });
        using var ms = new MemoryStream();
        CsvExporter.WriteAc(ms, data, [SignalId.V("o")]);
        var text = Encoding.UTF8.GetString(ms.ToArray()[3..]).Split("\r\n");
        Assert.Contains("V(o) 振幅 (dB)", text[0]);
        Assert.Equal("10,20,-90", text[2]);
    }
}
