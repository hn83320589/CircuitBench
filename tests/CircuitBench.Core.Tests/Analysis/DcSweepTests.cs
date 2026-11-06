using CircuitBench.Core.Analysis;
using CircuitBench.Core.Mna;
using CircuitBench.Core.Model;

namespace CircuitBench.Core.Tests.Analysis;

public class DcSweepTests
{
    private readonly Simulator _sim = new();

    private static Circuit DiodeCircuit(double vs) => Circuit.Create([
        VoltageSource.Dc("V1", "in", "0", vs),
        new Resistor("R1", "in", "a", 1000),
        new Diode("D1", "a", "0", DiodeModel.Default),
    ]);

    [Fact]
    public void SweepMatchesPointwiseOperatingPoints()
    {
        var sweep = _sim.DcSweep(DiodeCircuit(0), new DcSweepRequest("V1", -2, 5, 0.25), SimulationOptions.Default);
        Assert.Equal(29, sweep.Data.X.Length);
        for (var i = 0; i < sweep.Data.X.Length; i++)
        {
            var vs = sweep.Data.X[i];
            var op = _sim.OperatingPoint(DiodeCircuit(vs), SimulationOptions.Default);
            Assert.Equal(op[SignalId.V("a")], sweep.Data[SignalId.V("a")][i], 1e-7);
            Assert.Equal(op[SignalId.I("D1")], sweep.Data[SignalId.I("D1")][i], 1e-9);
        }
    }

    [Fact]
    public void CurrentSourceSweepIsLinearForResistor()
    {
        var c = Circuit.Create([CurrentSource.Dc("I1", "0", "n", 0), new Resistor("R1", "n", "0", 50)]);
        var sweep = _sim.DcSweep(c, new DcSweepRequest("I1", 0, 0.1, 0.01), SimulationOptions.Default);
        Assert.Equal(11, sweep.Data.X.Length);
        for (var i = 0; i < 11; i++)
        {
            Assert.Equal(sweep.Data.X[i] * 50, sweep.Data[SignalId.V("n")][i], 12);
        }
    }

    [Fact]
    public void DescendingSweepIsSupported()
    {
        var sweep = _sim.DcSweep(DiodeCircuit(0), new DcSweepRequest("V1", 1, -1, -0.5), SimulationOptions.Default);
        Assert.Equal([1, 0.5, 0, -0.5, -1], sweep.Data.X);
    }

    [Fact]
    public void UnknownSourceIsReportedInChinese()
    {
        var ex = Assert.Throws<SimulationException>(() =>
            _sim.DcSweep(DiodeCircuit(0), new DcSweepRequest("R1", 0, 1, 0.1), SimulationOptions.Default));
        Assert.Contains("R1", ex.Message);
    }

    [Fact]
    public void ZeroStepIsRejected()
    {
        Assert.Throws<SimulationException>(() =>
            _sim.DcSweep(DiodeCircuit(0), new DcSweepRequest("V1", 0, 1, 0), SimulationOptions.Default));
    }
}
