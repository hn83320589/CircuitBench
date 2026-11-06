using CircuitBench.Core.Analysis;
using CircuitBench.Core.Model;
using CircuitBench.ReferenceSolver;
using CircuitBench.TestKit;

namespace CircuitBench.Core.Tests.Analysis;

public class NonlinearDcTests
{
    private readonly Simulator _sim = new();
    private static readonly SimulationOptions NoGmin = SimulationOptions.Default with { Gmin = 0 };

    [Fact]
    public void LambertWHelperIsAccurate()
    {
        foreach (var x in new[] { 0.1, 1, 10, 1e5, 1e50 })
        {
            var w = LambertW.W0(x);
            Assert.Equal(x, w * Math.Exp(w), x * 1e-13);
        }
    }

    [Theory]
    [InlineData(5.0, 1000.0, 1e-14, 1.0)]
    [InlineData(1.0, 100.0, 1e-14, 1.0)]
    [InlineData(12.0, 10.0, 2.52e-9, 1.752)]
    [InlineData(0.3, 1e4, 1e-14, 1.0)]
    [InlineData(100.0, 1e3, 1e-12, 2.0)]
    public void SeriesResistorDiodeMatchesLambertW(double vs, double r, double isat, double n)
    {
        var c = Circuit.Create([
            VoltageSource.Dc("V1", "in", "0", vs),
            new Resistor("R1", "in", "a", r),
            new Diode("D1", "a", "0", new DiodeModel("D", isat, n)),
        ]);
        var op = _sim.OperatingPoint(c, NoGmin);
        var expected = LambertW.SeriesDiodeCurrent(vs, r, isat, n);
        Assert.Equal(expected, op[SignalId.I("D1")], Math.Abs(expected) * 1e-6);
        Assert.Equal(vs - expected * r, op[SignalId.V("a")], 1e-6);
    }

    [Fact]
    public void ReverseBiasCurrentIsMinusIsPlusGmin()
    {
        var c = Circuit.Create([
            VoltageSource.Dc("V1", "in", "0", -5),
            new Resistor("R1", "in", "a", 1000),
            new Diode("D1", "a", "0", DiodeModel.Default),
        ]);
        var op = _sim.OperatingPoint(c, SimulationOptions.Default);
        var va = op[SignalId.V("a")];
        Assert.Equal(-5, va, 6);
        Assert.Equal(-1e-14 + 1e-12 * va, op[SignalId.I("D1")], 1e-18);
    }

    [Fact]
    public void BridgeRectifierDcPointMatchesReference()
    {
        var c = BridgeRectifier(10);
        var op = _sim.OperatingPoint(c, SimulationOptions.Default);
        var reference = new RefSolver(CoreToReference.Convert(c)).OperatingPoint();
        foreach (var node in new[] { "p", "n", "a", "b" })
        {
            Assert.Equal(reference[$"V({node.ToUpperInvariant()})"], op[SignalId.V(node)], 1e-6);
        }

        // 兩個二極體壓降，負載約 10 − 2·0.7 V
        var vload = op[SignalId.V("p")] - op[SignalId.V("n")];
        Assert.InRange(vload, 8.2, 8.8);
    }

    [Fact]
    public void DiodeOverflowRegionStaysFinite()
    {
        // 巨大電源直接加在二極體與很小的電阻上
        var c = Circuit.Create([
            VoltageSource.Dc("V1", "in", "0", 1000),
            new Resistor("R1", "in", "a", 0.01),
            new Diode("D1", "a", "0", DiodeModel.Default),
        ]);
        var op = _sim.OperatingPoint(c, SimulationOptions.Default);
        var expected = LambertW.SeriesDiodeCurrent(1000, 0.01, 1e-14, 1);
        Assert.Equal(expected, op[SignalId.I("D1")], expected * 1e-5);
    }

    [Fact]
    public void GminSteppingIsUsedAndReportedWhenDirectNewtonIsSkipped()
    {
        var c = BridgeRectifier(10);
        var op = _sim.OperatingPoint(c, SimulationOptions.Default with { SkipDirectNewton = true });
        Assert.Equal(ConvergenceMethod.GminStepping, op.Statistics.Method);
        var direct = _sim.OperatingPoint(c, SimulationOptions.Default);
        Assert.Equal(direct[SignalId.V("p")], op[SignalId.V("p")], 1e-6);
    }

    [Fact]
    public void HardCircuitConvergesThroughSteppingWithFewIterations()
    {
        // 迭代上限很低時直接 Newton 會失敗，必須靠步進
        var c = DiodeStack(20, 50);
        var options = SimulationOptions.Default with { MaxNewtonIterations = 8 };
        var op = _sim.OperatingPoint(c, options);
        Assert.NotEqual(ConvergenceMethod.Newton, op.Statistics.Method);
        Assert.NotEqual(ConvergenceMethod.Linear, op.Statistics.Method);
        var reference = new RefSolver(CoreToReference.Convert(c)).OperatingPoint();
        Assert.Equal(reference["V(A1)"], op[SignalId.V("a1")], 1e-5);
    }

    [Fact]
    public void NonConvergenceReportsLastUnconvergedNode()
    {
        var c = DiodeStack(20, 50);
        var options = SimulationOptions.Default with { MaxNewtonIterations = 1 };
        var ex = Assert.Throws<CircuitBench.Core.Mna.ConvergenceException>(() => _sim.OperatingPoint(c, options));
        Assert.Contains("不收斂", ex.Message);
        Assert.False(string.IsNullOrWhiteSpace(ex.UnknownDescription));
        Assert.Contains(ex.UnknownDescription, ex.Message);
    }

    [Fact]
    public void NewtonIterationsAreCounted()
    {
        var op = _sim.OperatingPoint(BridgeRectifier(10), SimulationOptions.Default);
        Assert.True(op.Statistics.NewtonIterations >= 2);
        Assert.Equal(ConvergenceMethod.Newton, op.Statistics.Method);
    }

    internal static Circuit BridgeRectifier(double vin) => Circuit.Create([
        VoltageSource.Dc("V1", "a", "b", vin),
        new Resistor("Rb", "b", "0", 1e6),
        new Diode("D1", "a", "p", DiodeModel.Default),
        new Diode("D2", "b", "p", DiodeModel.Default),
        new Diode("D3", "n", "a", DiodeModel.Default),
        new Diode("D4", "n", "b", DiodeModel.Default),
        new Resistor("RL", "p", "n", 1000),
        new Resistor("Rn", "n", "0", 1e6),
    ]);

    private static Circuit DiodeStack(int count, double vs)
    {
        var parts = new List<Component> { VoltageSource.Dc("V1", "in", "0", vs), new Resistor("R1", "in", "a1", 10) };
        for (var i = 1; i <= count; i++)
        {
            parts.Add(new Diode($"D{i}", $"a{i}", i == count ? "0" : $"a{i + 1}", DiodeModel.Default));
        }

        return Circuit.Create(parts);
    }
}
