using System.Numerics;
using CircuitBench.Core.LinearAlgebra;
using CircuitBench.Core.Model;

namespace CircuitBench.Core.Mna.Devices;

internal static class SourceValues
{
    /// <summary>直流取直流值（或掃描覆寫）；暫態起點的工作點與暫態取波形值。</summary>
    public static double Evaluate(SourceSpec spec, double? dcOverride, RealLoadContext ctx) =>
        ctx.Mode == LoadMode.Dc && !ctx.UseWaveformsInDc
            ? dcOverride ?? spec.EffectiveDcValue
            : spec.Transient.Evaluate(ctx.Mode == LoadMode.Dc ? 0 : ctx.Time);
}

internal sealed class ResistorDevice(string name, int p, int q, double resistance) : Device(name)
{
    private TwoTerminalSlots _slots = new(p, q);
    private readonly double _g = 1.0 / resistance;

    public override void Register(PatternBuilder pattern) => _slots.Register(pattern);

    public override void Bind(SparsityPattern pattern) => _slots.Bind(pattern);

    public override void Load(RealLoadContext ctx) => _slots.Stamp(ctx.Matrix, _g);

    public override void LoadAc(ComplexLoadContext ctx) => _slots.Stamp(ctx.Matrix, new Complex(_g, 0));

    public override double Current(double[] x, RealLoadContext ctx) => _slots.Voltage(x) * _g;
}

internal sealed class SwitchDevice(TimedSwitch model, int p, int q) : Device(model.Name)
{
    private TwoTerminalSlots _slots = new(p, q);

    private double Conductance(double t) => 1.0 / (model.IsOnAt(t) ? model.Ron : model.Roff);

    public override void Register(PatternBuilder pattern) => _slots.Register(pattern);

    public override void Bind(SparsityPattern pattern) => _slots.Bind(pattern);

    public override void Load(RealLoadContext ctx) =>
        _slots.Stamp(ctx.Matrix, Conductance(ctx.Mode == LoadMode.Dc ? 0 : ctx.Time));

    public override void LoadAc(ComplexLoadContext ctx) => _slots.Stamp(ctx.Matrix, new Complex(Conductance(0), 0));

    public override IEnumerable<double> Breakpoints(double tStart, double tStop) =>
        model.ToggleTimes.Where(t => t >= tStart && t <= tStop);

    public override double Current(double[] x, RealLoadContext ctx) =>
        _slots.Voltage(x) * Conductance(ctx.Mode == LoadMode.Dc ? 0 : ctx.Time);
}

/// <summary>電容：直流開路；暫態使用 BE 或梯形伴隨模型。</summary>
internal sealed class CapacitorDevice(string name, int p, int q, double capacitance) : Device(name)
{
    private TwoTerminalSlots _slots = new(p, q);
    private double _vPrev;
    private double _iPrev;

    public override void Register(PatternBuilder pattern) => _slots.Register(pattern);

    public override void Bind(SparsityPattern pattern) => _slots.Bind(pattern);

    private (double G, double Ieq) Companion(RealLoadContext ctx)
    {
        if (ctx.Method == IntegrationMethod.Trapezoidal)
        {
            var g = 2 * capacitance / ctx.TimeStep;
            return (g, g * _vPrev + _iPrev);
        }

        var gb = capacitance / ctx.TimeStep;
        return (gb, gb * _vPrev);
    }

    public override void Load(RealLoadContext ctx)
    {
        if (ctx.Mode != LoadMode.Transient)
        {
            return;
        }

        var (g, ieq) = Companion(ctx);
        _slots.Stamp(ctx.Matrix, g);
        _slots.StampCurrent(ctx.Rhs, -ieq);
    }

    public override void LoadAc(ComplexLoadContext ctx) =>
        _slots.Stamp(ctx.Matrix, new Complex(0, ctx.Omega * capacitance));

    public override void InitializeTransient(double[] x)
    {
        _vPrev = _slots.Voltage(x);
        _iPrev = 0;
    }

    public override void AcceptTransient(double[] x, RealLoadContext ctx)
    {
        var (g, ieq) = Companion(ctx);
        var v = _slots.Voltage(x);
        _iPrev = g * v - ieq;
        _vPrev = v;
    }

    public override double Current(double[] x, RealLoadContext ctx) => ctx.Mode == LoadMode.Transient ? _iPrev : 0;

    public override void CollectStates(double[] x, List<StateQuantity> states) =>
        states.Add(new StateQuantity(_slots.Voltage(x), IsVoltage: true));
}

/// <summary>電感：以支路電流為未知數；直流為短路。</summary>
internal sealed class InductorDevice(string name, int p, int q, int k, double inductance) : Device(name)
{
    private BranchSlots _slots = new(p, q, k);
    private double _iPrev;
    private double _vPrev;

    public override void Register(PatternBuilder pattern) => _slots.Register(pattern, withDiagonal: true);

    public override void Bind(SparsityPattern pattern) => _slots.Bind(pattern, withDiagonal: true);

    public override void Load(RealLoadContext ctx)
    {
        _slots.StampIncidence(ctx.Matrix);
        if (ctx.Mode != LoadMode.Transient)
        {
            return;
        }

        if (ctx.Method == IntegrationMethod.Trapezoidal)
        {
            var r = 2 * inductance / ctx.TimeStep;
            _slots.StampDiagonal(ctx.Matrix, -r);
            ctx.Rhs[k] -= r * _iPrev + _vPrev;
        }
        else
        {
            var r = inductance / ctx.TimeStep;
            _slots.StampDiagonal(ctx.Matrix, -r);
            ctx.Rhs[k] -= r * _iPrev;
        }
    }

    public override void LoadAc(ComplexLoadContext ctx)
    {
        _slots.StampIncidence(ctx.Matrix);
        _slots.StampDiagonal(ctx.Matrix, new Complex(0, -ctx.Omega * inductance));
    }

    public override void InitializeTransient(double[] x)
    {
        _iPrev = x[k];
        _vPrev = _slots.Voltage(x);
    }

    public override void AcceptTransient(double[] x, RealLoadContext ctx)
    {
        _iPrev = x[k];
        _vPrev = _slots.Voltage(x);
    }

    public override double Current(double[] x, RealLoadContext ctx) => x[k];

    public override void CollectStates(double[] x, List<StateQuantity> states) =>
        states.Add(new StateQuantity(x[k], IsVoltage: false));
}

internal sealed class VoltageSourceDevice(VoltageSource model, int p, int q, int k) : Device(model.Name)
{
    private BranchSlots _slots = new(p, q, k);

    /// <summary>直流掃描時覆寫直流值。</summary>
    public double? DcOverride { get; set; }

    public override void Register(PatternBuilder pattern) => _slots.Register(pattern, withDiagonal: false);

    public override void Bind(SparsityPattern pattern) => _slots.Bind(pattern, withDiagonal: false);

    public override void Load(RealLoadContext ctx)
    {
        _slots.StampIncidence(ctx.Matrix);
        ctx.Rhs[k] += SourceValues.Evaluate(model.Spec, DcOverride, ctx) * ctx.SourceScale;
    }

    public override void LoadAc(ComplexLoadContext ctx)
    {
        _slots.StampIncidence(ctx.Matrix);
        if (model.Spec.Ac is { } ac)
        {
            ctx.Rhs[k] += Complex.FromPolarCoordinates(ac.Magnitude, ac.PhaseDegrees * Math.PI / 180);
        }
    }

    public override IEnumerable<double> Breakpoints(double tStart, double tStop) => model.Spec.Transient.Breakpoints(tStart, tStop);

    public override double Current(double[] x, RealLoadContext ctx) => x[k];
}

internal sealed class CurrentSourceDevice(CurrentSource model, int p, int q) : Device(model.Name)
{
    private TwoTerminalSlots _slots = new(p, q);

    public double? DcOverride { get; set; }

    public override void Register(PatternBuilder pattern)
    {
    }

    public override void Bind(SparsityPattern pattern)
    {
    }

    private double Value(RealLoadContext ctx) => SourceValues.Evaluate(model.Spec, DcOverride, ctx);

    public override void Load(RealLoadContext ctx) => _slots.StampCurrent(ctx.Rhs, Value(ctx) * ctx.SourceScale);

    public override void LoadAc(ComplexLoadContext ctx)
    {
        if (model.Spec.Ac is { } ac)
        {
            _slots.StampCurrent(ctx.Rhs, Complex.FromPolarCoordinates(ac.Magnitude, ac.PhaseDegrees * Math.PI / 180));
        }
    }

    public override IEnumerable<double> Breakpoints(double tStart, double tStop) => model.Spec.Transient.Breakpoints(tStart, tStop);

    public override double Current(double[] x, RealLoadContext ctx) => Value(ctx) * ctx.SourceScale;
}

internal sealed class VcvsDevice(string name, int p, int q, int cp, int cn, int k, double gain) : Device(name)
{
    private BranchSlots _slots = new(p, q, k);
    private int _kcp = -1;
    private int _kcn = -1;

    public override void Register(PatternBuilder pattern)
    {
        _slots.Register(pattern, withDiagonal: false);
        if (cp >= 0) pattern.Add(k, cp);
        if (cn >= 0) pattern.Add(k, cn);
    }

    public override void Bind(SparsityPattern pattern)
    {
        _slots.Bind(pattern, withDiagonal: false);
        _kcp = cp >= 0 ? pattern.IndexOf(k, cp) : -1;
        _kcn = cn >= 0 ? pattern.IndexOf(k, cn) : -1;
    }

    private void StampControl<T>(T[] m)
        where T : INumberBase<T>
    {
        var g = T.CreateChecked(gain);
        if (_kcp >= 0) m[_kcp] -= g;
        if (_kcn >= 0) m[_kcn] += g;
    }

    public override void Load(RealLoadContext ctx)
    {
        _slots.StampIncidence(ctx.Matrix);
        StampControl(ctx.Matrix);
    }

    public override void LoadAc(ComplexLoadContext ctx)
    {
        _slots.StampIncidence(ctx.Matrix);
        StampControl(ctx.Matrix);
    }

    public override double Current(double[] x, RealLoadContext ctx) => x[k];
}

internal sealed class VccsDevice(string name, int p, int q, int cp, int cn, double gm) : Device(name)
{
    private int _pcp = -1;
    private int _pcn = -1;
    private int _qcp = -1;
    private int _qcn = -1;

    public override void Register(PatternBuilder pattern)
    {
        if (p >= 0 && cp >= 0) pattern.Add(p, cp);
        if (p >= 0 && cn >= 0) pattern.Add(p, cn);
        if (q >= 0 && cp >= 0) pattern.Add(q, cp);
        if (q >= 0 && cn >= 0) pattern.Add(q, cn);
    }

    public override void Bind(SparsityPattern pattern)
    {
        _pcp = p >= 0 && cp >= 0 ? pattern.IndexOf(p, cp) : -1;
        _pcn = p >= 0 && cn >= 0 ? pattern.IndexOf(p, cn) : -1;
        _qcp = q >= 0 && cp >= 0 ? pattern.IndexOf(q, cp) : -1;
        _qcn = q >= 0 && cn >= 0 ? pattern.IndexOf(q, cn) : -1;
    }

    private void Stamp<T>(T[] m)
        where T : INumberBase<T>
    {
        var g = T.CreateChecked(gm);
        if (_pcp >= 0) m[_pcp] += g;
        if (_pcn >= 0) m[_pcn] -= g;
        if (_qcp >= 0) m[_qcp] -= g;
        if (_qcn >= 0) m[_qcn] += g;
    }

    public override void Load(RealLoadContext ctx) => Stamp(ctx.Matrix);

    public override void LoadAc(ComplexLoadContext ctx) => Stamp(ctx.Matrix);

    public override double Current(double[] x, RealLoadContext ctx) =>
        gm * ((cp >= 0 ? x[cp] : 0) - (cn >= 0 ? x[cn] : 0));
}
