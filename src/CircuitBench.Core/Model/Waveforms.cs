using System.Collections.Immutable;

namespace CircuitBench.Core.Model;

/// <summary>暫態電源波形（同 SPICE 定義）。</summary>
public abstract record Waveform
{
    public abstract double Evaluate(double t);

    /// <summary>[tStart, tStop] 之間的轉折時刻（嚴格遞增）。分析器會讓這些時刻剛好成為時間點。</summary>
    public abstract IEnumerable<double> Breakpoints(double tStart, double tStop);
}

public sealed record DcWaveform(double Value) : Waveform
{
    public override double Evaluate(double t) => Value;

    public override IEnumerable<double> Breakpoints(double tStart, double tStop) => [];
}

/// <summary>SIN(VO VA FREQ TD THETA PHASE)，PHASE 單位為度。</summary>
public sealed record SineWaveform(double Vo, double Va, double Freq, double Td = 0, double Theta = 0, double PhaseDeg = 0) : Waveform
{
    public override double Evaluate(double t)
    {
        var phase = PhaseDeg * Math.PI / 180.0;
        if (t < Td)
        {
            return Vo + Va * Math.Sin(phase);
        }

        var dt = t - Td;
        return Vo + Va * Math.Exp(-dt * Theta) * Math.Sin(2 * Math.PI * Freq * dt + phase);
    }

    public override IEnumerable<double> Breakpoints(double tStart, double tStop)
    {
        if (Td > 0 && Td >= tStart && Td <= tStop)
        {
            yield return Td;
        }
    }
}

/// <summary>
/// PULSE(V1 V2 TD TR TF PW PER)。轉折時刻為 TD + k·PER + {0, TR, TR+PW, TR+PW+TF}。
/// PER ≤ 0 或無限大時只產生一個脈衝。轉折時刻一律由 <see cref="Corner"/> 計算，
/// 讓 Evaluate 與 Breakpoints 使用完全相同的浮點數值。
/// </summary>
public sealed record PulseWaveform(double V1, double V2, double Td, double Tr, double Tf, double Pw, double Per) : Waveform
{
    private bool IsPeriodic => Per > 0 && double.IsFinite(Per);

    public double Corner(long k, int index)
    {
        var start = IsPeriodic ? Td + k * Per : Td;
        return index switch
        {
            0 => start,
            1 => start + Tr,
            2 => start + Tr + Pw,
            3 => start + Tr + Pw + Tf,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    public override double Evaluate(double t)
    {
        if (t < Td)
        {
            return V1;
        }

        long k = 0;
        if (IsPeriodic)
        {
            k = (long)Math.Floor((t - Td) / Per);
            if (t < Corner(k, 0))
            {
                k--;
            }
            else if (t >= Corner(k + 1, 0))
            {
                k++;
            }
        }

        var c0 = Corner(k, 0);
        var c1 = Corner(k, 1);
        var c2 = Corner(k, 2);
        var c3 = Corner(k, 3);
        if (t < c1)
        {
            return V1 + (V2 - V1) * (t - c0) / (c1 - c0);
        }

        if (t < c2)
        {
            return V2;
        }

        if (t < c3)
        {
            return V2 + (V1 - V2) * (t - c2) / (c3 - c2);
        }

        return V1;
    }

    public override IEnumerable<double> Breakpoints(double tStart, double tStop)
    {
        long k = 0;
        if (IsPeriodic && tStart > Td)
        {
            k = Math.Max(0, (long)Math.Floor((tStart - Td) / Per) - 1);
        }

        var last = double.NegativeInfinity;
        while (true)
        {
            for (var j = 0; j < 4; j++)
            {
                var c = Corner(k, j);
                if (c > tStop)
                {
                    yield break;
                }

                if (c >= tStart && c > last)
                {
                    last = c;
                    yield return c;
                }
            }

            if (!IsPeriodic)
            {
                yield break;
            }

            k++;
        }
    }
}

/// <summary>PWL(t1 v1 t2 v2 …)：時間嚴格遞增，區間內線性內插，第一點之前與最後一點之後保持。</summary>
public sealed record PwlWaveform(ImmutableArray<(double T, double V)> Points) : Waveform
{
    public override double Evaluate(double t)
    {
        if (Points.IsDefaultOrEmpty)
        {
            return 0;
        }

        if (t <= Points[0].T)
        {
            return Points[0].V;
        }

        if (t >= Points[^1].T)
        {
            return Points[^1].V;
        }

        var lo = 0;
        var hi = Points.Length - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (Points[mid].T <= t)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        var (t0, v0) = Points[lo];
        var (t1, v1) = Points[hi];
        return v0 + (v1 - v0) * (t - t0) / (t1 - t0);
    }

    public override IEnumerable<double> Breakpoints(double tStart, double tStop) =>
        Points.IsDefaultOrEmpty ? [] : Points.Select(p => p.T).Where(t => t >= tStart && t <= tStop);

    public bool Equals(PwlWaveform? other) => other is not null && Points.SequenceEqual(other.Points);

    public override int GetHashCode() => Points.IsDefault ? 0 : Points.Length;
}
