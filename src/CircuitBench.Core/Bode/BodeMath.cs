using System.Globalization;
using System.Numerics;
using CircuitBench.Core.Units;

namespace CircuitBench.Core.Bode;

public static class BodeMath
{
    public static double[] MagnitudeDb(IReadOnlyList<Complex> h) =>
        h.Select(v => 20 * Math.Log10(v.Magnitude)).ToArray();

    /// <summary>相位（度），展開使相鄰點差值不超過 ±180°，避免 ±180° 的折返跳動。</summary>
    public static double[] UnwrappedPhaseDegrees(IReadOnlyList<Complex> h)
    {
        var result = new double[h.Count];
        double offset = 0;
        for (var i = 0; i < h.Count; i++)
        {
            var raw = h[i].Phase * 180 / Math.PI;
            if (i > 0)
            {
                var candidate = raw + offset;
                var diff = candidate - result[i - 1];
                offset -= 360 * Math.Round(diff / 360);
            }

            result[i] = raw + offset;
        }

        return result;
    }
}

public readonly record struct Tick(double Value, string Label, bool IsMajor);

public static class AxisTicks
{
    /// <summary>線性軸：1、2、5 × 10^k 的整齊刻度。</summary>
    public static IReadOnlyList<Tick> Linear(double min, double max, int maxTicks = 8, string? unit = null)
    {
        if (!(max > min) || maxTicks < 2)
        {
            return [new Tick(min, EngineeringNotation.Format(min, unit, 3), true)];
        }

        var rawStep = (max - min) / (maxTicks - 1);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var step = new[] { 1.0, 2, 5, 10 }.Select(m => m * magnitude).First(s => s >= rawStep);
        var first = Math.Ceiling(min / step - 1e-9) * step;
        var ticks = new List<Tick>();
        for (var v = first; v <= max + step * 1e-9; v += step)
        {
            var value = Math.Abs(v) < step * 1e-9 ? 0 : v;
            ticks.Add(new Tick(value, EngineeringNotation.Format(value, unit, 3), true));
        }

        return ticks;
    }

    /// <summary>對數軸：每個十倍頻為主刻度（附標籤），2~9 倍為次刻度。</summary>
    public static IReadOnlyList<Tick> Log10(double min, double max, string? unit = "Hz")
    {
        if (!(min > 0) || !(max > min))
        {
            return [];
        }

        var ticks = new List<Tick>();
        var startDecade = (int)Math.Floor(Math.Log10(min));
        var endDecade = (int)Math.Ceiling(Math.Log10(max));
        for (var d = startDecade; d <= endDecade; d++)
        {
            var decade = Math.Pow(10, d);
            for (var m = 1; m <= 9; m++)
            {
                var v = m * decade;
                if (v < min * (1 - 1e-12) || v > max * (1 + 1e-12))
                {
                    continue;
                }

                ticks.Add(m == 1
                    ? new Tick(v, EngineeringNotation.Format(v, unit, 3), true)
                    : new Tick(v, m.ToString(CultureInfo.InvariantCulture), false));
            }
        }

        return ticks;
    }
}
