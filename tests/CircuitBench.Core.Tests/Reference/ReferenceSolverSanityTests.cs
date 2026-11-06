using System.Numerics;
using CircuitBench.ReferenceSolver;

namespace CircuitBench.Core.Tests.Reference;

/// <summary>先用手算值確認參考求解器本身正確，之後才拿它來驗證正式程式碼。</summary>
public class ReferenceSolverSanityTests
{
    [Fact]
    public void VoltageDivider()
    {
        var c = new RefCircuit()
            .Add(new RefElement(RefKind.VoltageSource, "V1", ["in", "0"], 10))
            .Add(new RefElement(RefKind.Resistor, "R1", ["in", "out"], 1000))
            .Add(new RefElement(RefKind.Resistor, "R2", ["out", "0"], 3000));
        var r = new RefSolver(c).OperatingPoint();
        Assert.Equal(7.5, r["V(OUT)"], 12);
        Assert.Equal(-10.0 / 4000, r["I(V1)"], 15);
    }

    [Fact]
    public void InvertingAmplifierWithVcvs()
    {
        var c = new RefCircuit()
            .Add(new RefElement(RefKind.VoltageSource, "V1", ["in", "0"], 1))
            .Add(new RefElement(RefKind.Resistor, "R1", ["in", "m"], 1000))
            .Add(new RefElement(RefKind.Resistor, "R2", ["m", "out"], 10000))
            .Add(new RefElement(RefKind.Vcvs, "E1", ["out", "0", "0", "m"], 1e6));
        var r = new RefSolver(c).OperatingPoint();
        Assert.Equal(-10, r["V(OUT)"], 3);
    }

    [Fact]
    public void RcStepResponseWithSmallStep()
    {
        var c = new RefCircuit()
            .Add(new RefElement(RefKind.VoltageSource, "V1", ["in", "0"], 0) { Waveform = t => t > 0 ? 1 : 0 })
            .Add(new RefElement(RefKind.Resistor, "R1", ["in", "out"], 1000))
            .Add(new RefElement(RefKind.Capacitor, "C1", ["out", "0"], 1e-6));
        var res = new RefSolver(c).Transient(5e-3, 1e-6, RefIntegration.Trapezoidal);
        var (t, v) = res[^1];
        Assert.Equal(1 - Math.Exp(-t / 1e-3), v["V(OUT)"], 4);
    }

    [Fact]
    public void RcLowPassAcAtCorner()
    {
        var fc = 1 / (2 * Math.PI * 1000 * 1e-6);
        var c = new RefCircuit()
            .Add(new RefElement(RefKind.VoltageSource, "V1", ["in", "0"], 0) { AcMagnitude = 1 })
            .Add(new RefElement(RefKind.Resistor, "R1", ["in", "out"], 1000))
            .Add(new RefElement(RefKind.Capacitor, "C1", ["out", "0"], 1e-6));
        var v = new RefSolver(c).Ac(fc)["V(OUT)"];
        Assert.Equal(1 / Math.Sqrt(2), v.Magnitude, 12);
        Assert.Equal(-45, v.Phase * 180 / Math.PI, 10);
    }

    [Fact]
    public void DiodeSeriesResistorSatisfiesKcl()
    {
        var c = new RefCircuit()
            .Add(new RefElement(RefKind.VoltageSource, "V1", ["in", "0"], 5))
            .Add(new RefElement(RefKind.Resistor, "R1", ["in", "a"], 1000))
            .Add(new RefElement(RefKind.Diode, "D1", ["a", "0"], 0));
        var r = new RefSolver(c, gmin: 0).OperatingPoint();
        var vd = r["V(A)"];
        var iR = (5 - vd) / 1000;
        var iD = 1e-14 * (Math.Exp(vd / 0.025852) - 1);
        Assert.Equal(iR, iD, 12);
    }

    [Fact]
    public void LcOscillatorConservesEnergyWithTrapezoidal()
    {
        var c = new RefCircuit()
            .Add(new RefElement(RefKind.CurrentSource, "I1", ["0", "a"], 0) { Waveform = t => t == 0 ? 1e-3 : 0 })
            .Add(new RefElement(RefKind.Inductor, "L1", ["a", "0"], 1e-3))
            .Add(new RefElement(RefKind.Capacitor, "C1", ["a", "0"], 1e-6));
        var res = new RefSolver(c).Transient(2e-3, 1e-6, RefIntegration.Trapezoidal);
        double Energy(IReadOnlyDictionary<string, double> v) => 0.5 * 1e-3 * Math.Pow(v["I(L1)"], 2) + 0.5 * 1e-6 * Math.Pow(v["V(A)"], 2);
        Assert.Equal(Energy(res[1].Values), Energy(res[^1].Values), 12);
    }

    [Fact]
    public void ComplexDenseSolveMatchesKnownSystem()
    {
        var a = new Complex[,] { { new(1, 1), 2 }, { 3, new(0, -1) } };
        var x = new[] { new Complex(1, 2), new Complex(-1, 0.5) };
        var b = new[] { a[0, 0] * x[0] + a[0, 1] * x[1], a[1, 0] * x[0] + a[1, 1] * x[1] };
        var solved = RefSolver.DenseSolve(a, b);
        Assert.True((solved[0] - x[0]).Magnitude < 1e-14);
        Assert.True((solved[1] - x[1]).Magnitude < 1e-14);
    }
}
