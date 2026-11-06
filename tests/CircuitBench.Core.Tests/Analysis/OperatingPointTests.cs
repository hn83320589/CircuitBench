using CircuitBench.Core.Analysis;
using CircuitBench.Core.Mna;
using CircuitBench.Core.Model;
using CircuitBench.ReferenceSolver;
using CircuitBench.TestKit;
using CircuitBench.TestKit.Property;

namespace CircuitBench.Core.Tests.Analysis;

public class OperatingPointTests
{
    private readonly Simulator _sim = new();

    [Fact]
    public void VoltageDividerAndSourceCurrentSign()
    {
        var c = Circuit.Create([
            VoltageSource.Dc("V1", "in", "0", 10),
            new Resistor("R1", "in", "out", 1000),
            new Resistor("R2", "out", "0", 3000),
        ]);
        var op = _sim.OperatingPoint(c, SimulationOptions.Default);
        Assert.Equal(7.5, op[SignalId.V("out")], 12);
        // 電壓源電流以「由正端流入元件」為正：電流由 in 流出到外部電路，所以為負
        Assert.Equal(-2.5e-3, op[SignalId.I("V1")], 15);
        Assert.Equal(2.5e-3, op[SignalId.I("R1")], 15);
        Assert.Equal(ConvergenceMethod.Linear, op.Statistics.Method);
    }

    [Fact]
    public void CurrentSourceDirectionMatchesSpice()
    {
        // I1 0 n 1mA：電流由 0 經電源流到 n，n 的電壓為正
        var c = Circuit.Create([CurrentSource.Dc("I1", "0", "n", 1e-3), new Resistor("R1", "n", "0", 1000)]);
        Assert.Equal(1.0, _sim.OperatingPoint(c, SimulationOptions.Default)[SignalId.V("n")], 12);
    }

    [Fact]
    public void InductorIsShortAndCapacitorIsOpenAtDc()
    {
        var c = Circuit.Create([
            VoltageSource.Dc("V1", "a", "0", 5),
            new Inductor("L1", "a", "b", 1e-3),
            new Resistor("R1", "b", "0", 100),
            new Capacitor("C1", "b", "0", 1e-6),
        ]);
        var op = _sim.OperatingPoint(c, SimulationOptions.Default);
        Assert.Equal(5, op[SignalId.V("b")], 12);
        Assert.Equal(0.05, op[SignalId.I("L1")], 12);
        Assert.Equal(0, op[SignalId.I("C1")]);
    }

    [Fact]
    public void InvertingAmplifierBuiltFromVcvs()
    {
        var c = Circuit.Create([
            VoltageSource.Dc("Vin", "in", "0", 0.5),
            new Resistor("R1", "in", "m", 1e3),
            new Resistor("R2", "m", "out", 10e3),
            new Vcvs("E1", "out", "0", "0", "m", 1e6),
        ]);
        // 有限增益 A 的反相放大器：Vout = −Vin·(R2/R1) / (1 + (1 + R2/R1)/A)
        var expected = -0.5 * 10 / (1 + 11 / 1e6);
        Assert.Equal(expected, _sim.OperatingPoint(c, SimulationOptions.Default)[SignalId.V("out")], 10);
    }

    [Fact]
    public void Property_RandomLinearCircuitsMatchReferenceSolver()
    {
        PropertyTest.ForAll(300, (rng, _) =>
        {
            var circuit = RandomCircuits.Linear(rng);
            var reference = new RefSolver(CoreToReference.Convert(circuit)).OperatingPoint();
            var op = _sim.OperatingPoint(circuit, SimulationOptions.Default);

            var scale = reference.Where(p => p.Key.StartsWith("V(")).Select(p => Math.Abs(p.Value)).DefaultIfEmpty(0).Max();
            foreach (var node in circuit.Nodes.Where(n => !n.IsGround))
            {
                var expected = reference[$"V({node.Value.ToUpperInvariant()})"];
                var actual = op[SignalId.V(node.Value)];
                var err = Math.Abs(actual - expected);
                Assert.True(err <= 1e-9 * Math.Max(scale, 1e-12),
                    $"節點 {node}：{actual} vs 參考 {expected}，誤差 {err:E3}");
            }
        });
    }

    [Fact]
    public void SingularCircuitReportsChineseMessage()
    {
        // 浮接節點 b（只經電容相連）在直流下使矩陣奇異
        var c = Circuit.Create([
            VoltageSource.Dc("V1", "a", "0", 1),
            new Resistor("R1", "a", "0", 1),
            new Capacitor("C1", "a", "b", 1e-6),
        ]);
        var ex = Assert.Throws<SimulationException>(() => _sim.OperatingPoint(c, SimulationOptions.Default));
        Assert.Contains("奇異", ex.Message);
        Assert.Contains("節點 b", ex.Message);
    }

    [Fact]
    public void StatisticsReportMatrixSize()
    {
        var c = Circuit.Create([VoltageSource.Dc("V1", "a", "0", 1), new Resistor("R1", "a", "0", 1)]);
        var stats = _sim.OperatingPoint(c, SimulationOptions.Default).Statistics;
        Assert.Equal(2, stats.MatrixSize);
        Assert.True(stats.NonZeros >= 3);
        Assert.Equal(1, stats.FullFactorizations);
    }

    [Fact]
    public async Task AsyncApiRunsAndReportsProgressToOne()
    {
        var c = Circuit.Create([VoltageSource.Dc("V1", "a", "0", 1), new Resistor("R1", "a", "0", 1)]);
        var reports = new List<double>();
        var progress = new SynchronousProgress(p => reports.Add(p.Fraction));
        await _sim.OperatingPointAsync(c, SimulationOptions.Default, progress, CancellationToken.None);
        Assert.Equal(1, reports[^1]);
    }
}

public sealed class SynchronousProgress(Action<AnalysisProgress> handler) : IProgress<AnalysisProgress>
{
    public void Report(AnalysisProgress value) => handler(value);
}
