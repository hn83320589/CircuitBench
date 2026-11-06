using System.Collections.Immutable;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.Model;
using CircuitBench.ReferenceSolver;
using CircuitBench.TestKit;
using CircuitBench.TestKit.Property;

namespace CircuitBench.Core.Tests.Analysis;

public class TransientTests
{
    private readonly Simulator _sim = new();

    // ---------- 驗收標準 8：精度 ----------

    [Fact]
    public void RcStepResponseMatchesAnalyticSolution()
    {
        const double r = 1e3, cap = 1e-6, tau = r * cap, td = 1e-4, tr = 1e-9;
        var c = Circuit.Create([
            new VoltageSource("V1", "in", "0", new SourceSpec(null, new PulseWaveform(0, 1, td, tr, tr, 1, 0), null)),
            new Resistor("R1", "in", "out", r),
            new Capacitor("C1", "out", "0", cap),
        ]);
        var res = _sim.Transient(c, new TransientRequest(5e-3), SimulationOptions.Default);
        var t = res.Data.X;
        var v = res.Data[SignalId.V("out")];

        double Analytic(double time)
        {
            var tp = time - td;
            if (tp <= 0) return 0;
            if (tp <= tr) return (tp - tau * (1 - Math.Exp(-tp / tau))) / tr;
            return 1 - tau / tr * (Math.Exp(tr / tau) - 1) * Math.Exp(-tp / tau);
        }

        var maxErr = 0.0;
        for (var i = 0; i < t.Length; i++)
        {
            maxErr = Math.Max(maxErr, Math.Abs(v[i] - Analytic(t[i])));
        }

        Assert.True(maxErr <= 1e-3 * 1 + 1e-6, $"最大誤差 {maxErr:E3}，共 {t.Length} 點");
    }

    [Fact]
    public void UnderdampedRlcFrequencyAndDecayMatchAnalytic()
    {
        const double r = 2, l = 1e-3, cap = 1e-6;
        var c = Circuit.Create([
            new VoltageSource("V1", "in", "0", new SourceSpec(null, new PulseWaveform(0, 1, 1e-5, 1e-9, 1e-9, 1, 0), null)),
            new Resistor("R1", "in", "a", r),
            new Inductor("L1", "a", "b", l),
            new Capacitor("C1", "b", "0", cap),
        ]);
        // 振盪電路依 README 建議：最大步長設為振盪週期的 1/100（TR 的衰減率誤差約為 (ωh)²/4）
        var period = 2 * Math.PI * Math.Sqrt(l * cap);
        var res = _sim.Transient(c, new TransientRequest(2e-3, period / 100), SimulationOptions.Default);
        var t = res.Data.X;
        var v = res.Data[SignalId.V("b")].Select(x => x - 1).ToArray(); // 減去終值

        var alpha = r / (2 * l);
        var omega0 = 1 / Math.Sqrt(l * cap);
        var omegaD = Math.Sqrt(omega0 * omega0 - alpha * alpha);

        // 以最小平方法擬合極值：時間 t_k = t0 + k·π/ωd，ln|峰值| = c − α·t_k
        var extrema = WaveformMath.Extrema(t, v).Where(e => e.Time > 2e-5).ToList();
        Assert.True(extrema.Count >= 8);
        var k = Enumerable.Range(0, extrema.Count).Select(i => (double)i).ToArray();
        var times = extrema.Select(e => e.Time).ToArray();
        var logs = extrema.Select(e => Math.Log(Math.Abs(e.Value))).ToArray();
        var measuredOmega = Math.PI / WaveformMath.Slope(k, times);
        var measuredAlpha = -WaveformMath.Slope(times, logs);

        Assert.True(Math.Abs(measuredOmega / omegaD - 1) <= 0.002, $"ωd 誤差 {measuredOmega / omegaD - 1:P3}");
        Assert.True(Math.Abs(measuredAlpha / alpha - 1) <= 0.002, $"α 誤差 {measuredAlpha / alpha - 1:P3}");
    }

    // ---------- 驗收標準 9：能量與開關 ----------

    [Fact]
    public void LosslessLcOscillatesHundredPeriodsWithinOnePercentEnergyDrift()
    {
        const double l = 1e-3, cap = 1e-6;
        var period = 2 * Math.PI * Math.Sqrt(l * cap);
        var c = Circuit.Create([
            new CurrentSource("I1", "0", "a", new SourceSpec(null, new PwlWaveform([(0.0, 1e-3), (1e-9, 0.0)]), null)),
            new Inductor("L1", "a", "0", l),
            new Capacitor("C1", "a", "0", cap),
        ]);
        var res = _sim.Transient(c, new TransientRequest(100 * period), SimulationOptions.Default);
        var iL = res.Data[SignalId.I("L1")];
        var vC = res.Data[SignalId.V("a")];
        double Energy(int k) => 0.5 * l * iL[k] * iL[k] + 0.5 * cap * vC[k] * vC[k];

        var initial = 0.5 * l * 1e-6;
        var worst = Enumerable.Range(1, iL.Length - 1).Max(k => Math.Abs(Energy(k) / initial - 1));
        Assert.True(worst <= 0.01, $"能量漂移 {worst:P3}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SwitchInterruptingInductorCurrentHasNoStepwiseRinging(bool withLoadResistor)
    {
        var parts = new List<Component>
        {
            VoltageSource.Dc("V1", "in", "0", 10),
            new Resistor("R1", "in", "a", 10),
            new Inductor("L1", "a", "b", 1e-3),
            new TimedSwitch("S1", "b", "0", [1e-3], InitiallyOn: true),
        };
        if (withLoadResistor)
        {
            parts.Add(new Resistor("R2", "b", "0", 1e3));
        }

        var res = _sim.Transient(Circuit.Create(parts), new TransientRequest(3e-3), SimulationOptions.Default);
        var t = res.Data.X;
        var vb = res.Data[SignalId.V("b")];
        var after = Enumerable.Range(0, t.Length).Where(i => t[i] >= 1e-3).ToArray();
        var segment = after.Select(i => vb[i]).ToArray();
        var max = segment.Max(Math.Abs);
        Assert.True(max > 100, "開關切斷時應出現感應電壓");
        Assert.Equal(0, WaveformMath.StepwiseAlternations(segment, 1e-6 * max));

        var il = res.Data[SignalId.I("L1")];
        Assert.Equal(10 / 10.001, il[Array.IndexOf(t, 1e-3) - 1], 6);
    }

    // ---------- 驗收標準 10：轉折點 ----------

    [Fact]
    public void PulsePwlAndSwitchBreakpointsAreExactTimePoints()
    {
        var pulse = new PulseWaveform(0, 1, 1.1e-4, 3.3e-6, 7.7e-6, 2.2e-5, 1.9e-4);
        var pwl = new PwlWaveform([(0.0, 0.0), (1.234e-4, 2.0), (3.21e-4, -1.0), (7.77e-4, 0.5)]);
        var toggles = ImmutableArray.Create(2.345e-4, 6.789e-4);
        var c = Circuit.Create([
            new VoltageSource("V1", "a", "0", new SourceSpec(null, pulse, null)),
            new Resistor("R1", "a", "b", 1e3),
            new Capacitor("C1", "b", "0", 1e-8),
            new VoltageSource("V2", "c", "0", new SourceSpec(null, pwl, null)),
            new Resistor("R2", "c", "d", 100),
            new TimedSwitch("S1", "d", "b", toggles, InitiallyOn: false),
            new Capacitor("C2", "d", "0", 1e-8),
        ]);
        const double stop = 1e-3;
        var times = _sim.Transient(c, new TransientRequest(stop), SimulationOptions.Default).Data.X;

        var expected = pulse.Breakpoints(0, stop).Concat(pwl.Breakpoints(0, stop)).Concat(toggles).Where(x => x > 0 && x <= stop);
        foreach (var bp in expected)
        {
            var nearest = times.MinBy(x => Math.Abs(x - bp));
            Assert.True(Math.Abs(nearest - bp) <= 1e-15, $"轉折點 {bp:R} 最接近的時間點為 {nearest:R}");
        }

        Assert.Equal(stop, times[^1]);
    }

    [Fact]
    public void PulseValuesAtCornersAreHitExactly()
    {
        var pulse = new PulseWaveform(0, 5, 1e-5, 1e-6, 1e-6, 5e-6, 2e-5);
        var c = Circuit.Create([new VoltageSource("V1", "a", "0", new SourceSpec(null, pulse, null)), new Resistor("R1", "a", "0", 1)]);
        var res = _sim.Transient(c, new TransientRequest(1e-4), SimulationOptions.Default);
        var t = res.Data.X;
        var v = res.Data[SignalId.V("a")];
        var plateauStarts = Enumerable.Range(0, 5).Select(k => pulse.Corner(k, 1));
        foreach (var corner in plateauStarts)
        {
            var i = Array.IndexOf(t, corner);
            Assert.True(i >= 0);
            Assert.Equal(5, v[i], 12);
        }
    }

    // ---------- 驗收標準 11：步長控制 ----------

    [Fact]
    public void Property_StepControlInvariants()
    {
        PropertyTest.ForAll(40, (rng, _) =>
        {
            var circuit = RandomCircuits.Linear(rng, includeReactive: true);
            // 加一個隨機脈衝源驅動
            var nodes = circuit.Nodes.Where(n => !n.IsGround).ToList();
            var driveNode = nodes[rng.Next(nodes.Count)];
            var pulse = new PulseWaveform(0, rng.NextDouble() * 10, rng.NextDouble() * 1e-4, 1e-7 + rng.NextDouble() * 1e-5,
                1e-7 + rng.NextDouble() * 1e-5, rng.NextDouble() * 1e-4, 3e-4 + rng.NextDouble() * 1e-4);
            var withDrive = Circuit.Create(circuit.Components.Append(
                new CurrentSource("Idrive", "0", driveNode, new SourceSpec(null, pulse, null))));
            var stop = 1e-3;
            var maxStep = stop / (20 + rng.Next(200));
            var res = _sim.Transient(withDrive, new TransientRequest(stop, maxStep), SimulationOptions.Default);
            var t = res.Data.X;

            Assert.Equal(0, t[0]);
            Assert.Equal(stop, t[^1]);
            for (var i = 1; i < t.Length; i++)
            {
                Assert.True(t[i] > t[i - 1], $"時間未嚴格遞增於索引 {i}");
                Assert.True(t[i] - t[i - 1] <= maxStep * (1 + 1e-12), $"步長 {t[i] - t[i - 1]:E3} 超過最大步長 {maxStep:E3}");
            }

            // 結果只包含被接受的步驟
            Assert.Equal(res.Statistics.AcceptedSteps + 1, t.Length);
            foreach (var column in res.Data.Signals.Values)
            {
                Assert.Equal(t.Length, column.Length);
            }
        });
    }

    [Fact]
    public void SharpPulseCausesRejectedStepsThatDoNotAppearInResults()
    {
        var c = Circuit.Create([
            new VoltageSource("V1", "a", "0", new SourceSpec(null, new SineWaveform(0, 1, 1e4), null)),
            new Resistor("R1", "a", "b", 1e3),
            new Capacitor("C1", "b", "0", 1e-7),
        ]);
        var res = _sim.Transient(c, new TransientRequest(1e-3, 1e-4), SimulationOptions.Default);
        Assert.True(res.Statistics.RejectedSteps > 0);
        Assert.Equal(res.Statistics.AcceptedSteps + 1, res.Data.X.Length);
    }

    // ---------- 驗收標準 12：整流 ----------

    [Fact]
    public void HalfWaveRectifierMatchesFineStepReference()
    {
        var c = Circuit.Create([
            new VoltageSource("V1", "in", "0", new SourceSpec(null, new SineWaveform(0, 10, 50), null)),
            new Diode("D1", "in", "out", DiodeModel.Default),
            new Resistor("RL", "out", "0", 1e3),
        ]);
        AssertMatchesReference(c, 40e-3, 2e-6, "out", 10);
    }

    [Fact]
    public void FullWaveBridgeRectifierWithFilterMatchesFineStepReference()
    {
        var c = Circuit.Create([
            new VoltageSource("V1", "a", "b", new SourceSpec(null, new SineWaveform(0, 10, 50), null)),
            new Resistor("Rb", "b", "0", 1e6),
            new Diode("D1", "a", "p", DiodeModel.Default),
            new Diode("D2", "b", "p", DiodeModel.Default),
            new Diode("D3", "0", "a", DiodeModel.Default),
            new Diode("D4", "0", "b", DiodeModel.Default),
            new Resistor("RL", "p", "0", 1e3),
            new Capacitor("CF", "p", "0", 10e-6),
        ]);
        AssertMatchesReference(c, 40e-3, 2e-6, "p", 10);
    }

    private void AssertMatchesReference(Circuit c, double stop, double refStep, string node, double peak)
    {
        var res = _sim.Transient(c, new TransientRequest(stop), SimulationOptions.Default);
        var reference = new RefSolver(CoreToReference.Convert(c)).Transient(stop, refStep, RefIntegration.Trapezoidal);
        var rt = reference.Select(p => p.Time).ToArray();
        var rv = reference.Select(p => p.Values[$"V({node.ToUpperInvariant()})"]).ToArray();
        var t = res.Data.X;
        var v = res.Data[SignalId.V(node)];
        var worst = 0.0;
        for (var i = 0; i < t.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(v[i] - WaveformMath.Interpolate(rt, rv, t[i])));
        }

        Assert.True(worst <= 0.005 * peak, $"與參考解最大誤差 {worst:E3} V（{t.Length} 點）");
    }
}
