using System.Numerics;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.Model;
using CircuitBench.ReferenceSolver;
using CircuitBench.TestKit;
using CircuitBench.TestKit.Property;

namespace CircuitBench.Core.Tests.Analysis;

public class AcTests
{
    private readonly Simulator _sim = new();

    private static SourceSpec AcUnit => new(0, new DcWaveform(0), new AcSpec(1, 0));

    [Fact]
    public void RcLowPassMinus3dBFrequencyAndPhase()
    {
        const double r = 1e3, cap = 100e-9;
        var fc = 1 / (2 * Math.PI * r * cap);
        var c = Circuit.Create([
            new VoltageSource("V1", "in", "0", AcUnit),
            new Resistor("R1", "in", "out", r),
            new Capacitor("C1", "out", "0", cap),
        ]);
        var res = _sim.Ac(c, new AcRequest(AcSweepType.Decade, 10, 1e6, 200), SimulationOptions.Default);
        var f = res.Data.Frequency;
        var db = res.Data[SignalId.V("out")].Select(h => 20 * Math.Log10(h.Magnitude)).ToArray();

        // 在對數頻率上內插 −3.0103 dB 交越
        var target = 20 * Math.Log10(1 / Math.Sqrt(2));
        var i = Array.FindIndex(db, d => d < target);
        var frac = (target - db[i - 1]) / (db[i] - db[i - 1]);
        var f3 = Math.Pow(10, Math.Log10(f[i - 1]) + frac * (Math.Log10(f[i]) - Math.Log10(f[i - 1])));
        Assert.True(Math.Abs(f3 / fc - 1) <= 1e-3, $"−3 dB 頻率 {f3:G6} vs {fc:G6}");

        var atFc = _sim.Ac(c, new AcRequest(AcSweepType.Linear, fc, fc, 1), SimulationOptions.Default).Data[SignalId.V("out")][0];
        Assert.Equal(-45, atFc.Phase * 180 / Math.PI, 0.05);
        Assert.Equal(1 / Math.Sqrt(2), atFc.Magnitude, 1e-12);
    }

    [Fact]
    public void SeriesRlcResonanceFrequencyAndQ()
    {
        const double r = 10, l = 1e-3, cap = 1e-6;
        var f0 = 1 / (2 * Math.PI * Math.Sqrt(l * cap));
        var q = Math.Sqrt(l / cap) / r;
        var c = Circuit.Create([
            new VoltageSource("V1", "in", "0", AcUnit),
            new Inductor("L1", "in", "a", l),
            new Capacitor("C1", "a", "b", cap),
            new Resistor("R1", "b", "0", r),
        ]);
        var res = _sim.Ac(c, new AcRequest(AcSweepType.Linear, 4000, 6000, 20001), SimulationOptions.Default);
        var f = res.Data.Frequency;
        var vr = res.Data[SignalId.V("b")].Select(h => h.Magnitude).ToArray();
        var peak = Array.IndexOf(vr, vr.Max());
        Assert.True(Math.Abs(f[peak] / f0 - 1) <= 1e-4, $"共振頻率 {f[peak]} vs {f0}");
        Assert.Equal(1, vr[peak], 1e-6);

        double Crossing(int from, int step)
        {
            var level = vr[peak] / Math.Sqrt(2);
            var i = from;
            while (vr[i] > level) i += step;
            var j = i - step;
            return f[j] + (level - vr[j]) / (vr[i] - vr[j]) * (f[i] - f[j]);
        }

        var bandwidth = Crossing(peak, 1) - Crossing(peak, -1);
        var measuredQ = f0 / bandwidth;
        Assert.True(Math.Abs(measuredQ / q - 1) <= 1e-3, $"Q {measuredQ:G6} vs {q:G6}");
    }

    [Fact]
    public void Property_RandomLinearCircuitsMatchReferenceComplexSolution()
    {
        PropertyTest.ForAll(200, (rng, _) =>
        {
            var circuit = RandomCircuits.Linear(rng, includeReactive: true, includeAc: true);
            var freq = Math.Pow(10, rng.NextDouble() * 7);
            var reference = new RefSolver(CoreToReference.Convert(circuit)).Ac(freq);
            var res = _sim.Ac(circuit, new AcRequest(AcSweepType.Linear, freq, freq, 1), SimulationOptions.Default);

            var scale = reference.Where(p => p.Key.StartsWith("V(")).Select(p => p.Value.Magnitude).DefaultIfEmpty(0).Max();
            foreach (var node in circuit.Nodes.Where(n => !n.IsGround))
            {
                var expected = reference[$"V({node.Value.ToUpperInvariant()})"];
                var actual = res.Data[SignalId.V(node.Value)][0];
                var err = (actual - expected).Magnitude;
                Assert.True(err <= 1e-9 * scale + 1e-13, $"f={freq:G4} 節點 {node}：{actual} vs {expected}，誤差 {err:E3}");
            }
        });
    }

    [Theory]
    [InlineData(1e-6)]
    [InlineData(1e-3)]
    [InlineData(10e-3)]
    [InlineData(-1e-15)]
    public void DiodeSmallSignalImpedanceFollowsBias(double biasCurrent)
    {
        // 直流偏壓電流源 + 單位交流電流源並聯驅動二極體：Z = 1 / (gd + gmin)
        var c = Circuit.Create([
            new CurrentSource("Ib", "0", "a", new SourceSpec(biasCurrent, new DcWaveform(biasCurrent), null)),
            new CurrentSource("Iac", "0", "a", new SourceSpec(0, new DcWaveform(0), new AcSpec(1, 0))),
            new Diode("D1", "a", "0", DiodeModel.Default),
        ]);
        var op = _sim.OperatingPoint(c, SimulationOptions.Default);
        var vd = op[SignalId.V("a")];
        const double nvt = 0.025852;
        var gd = 1e-14 / nvt * Math.Exp(vd / nvt);
        var expected = 1 / (gd + 1e-12);

        var z = _sim.Ac(c, new AcRequest(AcSweepType.Linear, 1e3, 1e3, 1), SimulationOptions.Default).Data[SignalId.V("a")][0];
        Assert.Equal(expected, z.Real, expected * 1e-9);
        Assert.Equal(0, z.Imaginary, expected * 1e-12);

        if (biasCurrent > 1e-9)
        {
            // 順偏時近似 r_d = n·Vt / I
            Assert.Equal(nvt / biasCurrent, z.Real, nvt / biasCurrent * 1e-3);
        }
    }

    [Fact]
    public void AcSourcePhaseIsApplied()
    {
        var c = Circuit.Create([
            new VoltageSource("V1", "a", "0", new SourceSpec(0, new DcWaveform(0), new AcSpec(2, 30))),
            new Resistor("R1", "a", "0", 1),
        ]);
        var v = _sim.Ac(c, new AcRequest(AcSweepType.Linear, 1, 1, 1), SimulationOptions.Default).Data[SignalId.V("a")][0];
        Assert.Equal(2, v.Magnitude, 12);
        Assert.Equal(30, v.Phase * 180 / Math.PI, 10);
    }

    [Fact]
    public void AcAmplitudeDoesNotAffectDcOrTransient()
    {
        var c = Circuit.Create([
            new VoltageSource("V1", "a", "0", new SourceSpec(3, new DcWaveform(3), new AcSpec(100, 45))),
            new Resistor("R1", "a", "0", 1),
        ]);
        Assert.Equal(3, _sim.OperatingPoint(c, SimulationOptions.Default)[SignalId.V("a")], 12);
        var tran = _sim.Transient(c, new TransientRequest(1e-3), SimulationOptions.Default);
        Assert.All(tran.Data[SignalId.V("a")], v => Assert.Equal(3, v, 12));
    }

    [Fact]
    public void DecadeSweepFrequencies()
    {
        var f = Simulator.AcFrequencies(new AcRequest(AcSweepType.Decade, 10, 1000, 10));
        Assert.Equal(21, f.Length);
        Assert.Equal(10, f[0]);
        Assert.Equal(1000, f[^1], 9);
        Assert.Equal(Math.Pow(10, 1.1), f[1], 9);
    }

    [Fact]
    public void LinearSweepFrequencies()
    {
        Assert.Equal([100.0, 200, 300], Simulator.AcFrequencies(new AcRequest(AcSweepType.Linear, 100, 300, 3)));
    }

    [Fact]
    public void ComplexFactorizationIsReusedAcrossFrequencies()
    {
        var c = Circuit.Create([
            new VoltageSource("V1", "in", "0", AcUnit),
            new Resistor("R1", "in", "out", 1e3),
            new Capacitor("C1", "out", "0", 1e-6),
        ]);
        var stats = _sim.Ac(c, new AcRequest(AcSweepType.Decade, 1, 1e6, 10), SimulationOptions.Default).Statistics;
        Assert.True(stats.Refactorizations >= 59, $"refactor={stats.Refactorizations}");
    }

    [Fact]
    public void ResultAtSingleFrequencyMatchesHandCalculation()
    {
        var c = Circuit.Create([
            new VoltageSource("V1", "in", "0", AcUnit),
            new Resistor("R1", "in", "out", 1e3),
            new Inductor("L1", "out", "0", 1e-3),
        ]);
        var w = 2 * Math.PI * 1e5;
        var expected = new Complex(0, w * 1e-3) / new Complex(1e3, w * 1e-3);
        var v = _sim.Ac(c, new AcRequest(AcSweepType.Linear, 1e5, 1e5, 1), SimulationOptions.Default).Data[SignalId.V("out")][0];
        Assert.True((v - expected).Magnitude < 1e-12);
    }
}
