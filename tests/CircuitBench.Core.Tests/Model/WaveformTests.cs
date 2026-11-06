using System.Collections.Immutable;
using CircuitBench.Core.Model;

namespace CircuitBench.Core.Tests.Model;

public class WaveformTests
{
    [Fact]
    public void SineBeforeDelayUsesPhaseOnly()
    {
        var w = new SineWaveform(1, 2, 1000, Td: 1e-3, Theta: 0, PhaseDeg: 30);
        Assert.Equal(1 + 2 * Math.Sin(Math.PI / 6), w.Evaluate(0.5e-3), 12);
    }

    [Fact]
    public void SineAfterDelayIsDampedSinusoid()
    {
        var w = new SineWaveform(0.5, 1, 50, Td: 0.01, Theta: 10, PhaseDeg: 90);
        var t = 0.0123;
        var expected = 0.5 + Math.Exp(-(t - 0.01) * 10) * Math.Sin(2 * Math.PI * 50 * (t - 0.01) + Math.PI / 2);
        Assert.Equal(expected, w.Evaluate(t), 12);
    }

    [Fact]
    public void SineBreakpointIsDelay()
    {
        Assert.Equal([1e-3], new SineWaveform(0, 1, 1e3, Td: 1e-3).Breakpoints(0, 1));
        Assert.Empty(new SineWaveform(0, 1, 1e3).Breakpoints(0, 1));
    }

    [Fact]
    public void PulseShape()
    {
        var p = new PulseWaveform(0, 5, Td: 1, Tr: 1, Tf: 2, Pw: 3, Per: 10);
        Assert.Equal(0, p.Evaluate(0.5));
        Assert.Equal(2.5, p.Evaluate(1.5), 12);
        Assert.Equal(5, p.Evaluate(2));
        Assert.Equal(5, p.Evaluate(4.9));
        Assert.Equal(2.5, p.Evaluate(6), 12);
        Assert.Equal(0, p.Evaluate(8));
        Assert.Equal(2.5, p.Evaluate(11.5), 12);
    }

    [Fact]
    public void PulseBreakpointsFollowFormula()
    {
        var p = new PulseWaveform(0, 1, Td: 1e-6, Tr: 1e-7, Tf: 2e-7, Pw: 3e-7, Per: 1e-6);
        var bps = p.Breakpoints(0, 3.05e-6).ToArray();
        var expected = new List<double>();
        for (var k = 0; k < 3; k++)
        {
            var start = 1e-6 + k * 1e-6;
            expected.AddRange([start, start + 1e-7, start + 1e-7 + 3e-7, start + 1e-7 + 3e-7 + 2e-7]);
        }

        expected.RemoveAll(t => t > 3.05e-6);
        Assert.Equal(expected, bps);
        Assert.True(bps.Zip(bps.Skip(1)).All(pair => pair.Second > pair.First));
    }

    [Fact]
    public void PulseBreakpointsInWindowStartLate()
    {
        var p = new PulseWaveform(0, 1, 0, 1, 1, 1, 10);
        var bps = p.Breakpoints(95, 105).ToArray();
        Assert.Equal([100.0, 101, 102, 103], bps);
    }

    [Fact]
    public void PulseEvaluatesExactlyAtBreakpoints()
    {
        var p = new PulseWaveform(1, 3, Td: 0.1, Tr: 0.2, Tf: 0.3, Pw: 0.4, Per: 1.7);
        foreach (var bp in p.Breakpoints(0, 20))
        {
            var v = p.Evaluate(bp);
            Assert.True(Math.Abs(v - 1) < 1e-9 || Math.Abs(v - 3) < 1e-9, $"t={bp} v={v}");
        }
    }

    [Fact]
    public void NonPeriodicPulseHasSinglePulse()
    {
        var p = new PulseWaveform(0, 1, 1, 1, 1, 1, 0);
        Assert.Equal(4, p.Breakpoints(0, 100).Count());
        Assert.Equal(0, p.Evaluate(50));
    }

    [Fact]
    public void PwlInterpolatesAndHolds()
    {
        var w = new PwlWaveform([(1.0, 0.0), (2.0, 10.0), (4.0, -10.0)]);
        Assert.Equal(0, w.Evaluate(0));
        Assert.Equal(5, w.Evaluate(1.5), 12);
        Assert.Equal(0, w.Evaluate(3), 12);
        Assert.Equal(-10, w.Evaluate(100));
        Assert.Equal([1.0, 2.0, 4.0], w.Breakpoints(0, 10));
        Assert.Equal([2.0], w.Breakpoints(1.5, 3));
    }

    [Fact]
    public void PwlEqualityComparesPoints()
    {
        var a = new PwlWaveform([(0.0, 1.0), (1.0, 2.0)]);
        var b = new PwlWaveform(ImmutableArray.Create((0.0, 1.0), (1.0, 2.0)));
        Assert.Equal(a, b);
    }

    [Fact]
    public void SwitchStateToggles()
    {
        var s = new TimedSwitch("S1", "a", "b", [1.0, 2.0], InitiallyOn: false);
        Assert.False(s.IsOnAt(0.5));
        Assert.True(s.IsOnAt(1.0));
        Assert.True(s.IsOnAt(1.5));
        Assert.False(s.IsOnAt(2.0));
    }

    [Fact]
    public void SourceDcValueFallsBackToWaveformAtZero()
    {
        var spec = new SourceSpec(null, new SineWaveform(2, 1, 10), null);
        Assert.Equal(2, spec.EffectiveDcValue, 12);
        Assert.Equal(7, (spec with { DcValue = 7 }).EffectiveDcValue);
    }
}

public class CircuitTests
{
    [Fact]
    public void DuplicateNamesAreRejectedCaseInsensitively()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Circuit.Create([new Resistor("R1", "a", "0", 1), new Resistor("r1", "b", "0", 1)]));
        Assert.Contains("重複", ex.Message);
    }

    [Fact]
    public void NodesAreCaseInsensitiveAndCollected()
    {
        var c = Circuit.Create([new Resistor("R1", "Out", "0", 1), new Resistor("R2", "OUT", "in", 1)]);
        Assert.Equal(3, c.Nodes.Count);
    }

    [Fact]
    public void StructuralEqualityIgnoresOrder()
    {
        var a = Circuit.Create([new Resistor("R1", "a", "0", 1), VoltageSource.Dc("V1", "a", "0", 5)]);
        var b = Circuit.Create([VoltageSource.Dc("v1", "a", "0", 5), new Resistor("R1", "A", "0", 1)]);
        Assert.True(a.StructurallyEquals(b));
        Assert.False(a.StructurallyEquals(b.With(new Resistor("R1", "a", "0", 2))));
    }
}
