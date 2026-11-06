using System.Numerics;
using CircuitBench.Core.LinearAlgebra;
using CircuitBench.Core.Model;

namespace CircuitBench.Core.Mna.Devices;

/// <summary>
/// 二極體 I = Is·(exp(V/(n·Vt)) − 1)，並聯 gmin。Newton 迭代時以 pnjlim 限制接面電壓變化，
/// 避免指數溢位與振盪。
/// </summary>
internal sealed class DiodeDevice(Diode model, int p, int q) : Device(model.Name)
{
    public const double ThermalVoltage = 0.025852;
    private const double MaxExponent = 80;

    private TwoTerminalSlots _slots = new(p, q);
    private readonly double _nvt = model.Model.N * ThermalVoltage;
    private readonly double _is = model.Model.Is;
    private double _vdLast;
    private bool _limited;

    public override bool IsNonlinear => true;

    public override bool WasLimited => _limited;

    public double JunctionVoltage => _vdLast;

    private double VCrit => _nvt * Math.Log(_nvt / (Math.Sqrt(2) * _is));

    public override void Register(PatternBuilder pattern) => _slots.Register(pattern);

    public override void Bind(SparsityPattern pattern) => _slots.Bind(pattern);

    /// <summary>給定新的迭代起點時（例如新的分析），重設限制的參考電壓。</summary>
    public void ResetJunction(double[] x) => _vdLast = _slots.Voltage(x);

    public override void Load(RealLoadContext ctx)
    {
        var vRaw = _slots.Voltage(ctx.Solution);
        var vd = Limit(vRaw, _vdLast);
        _limited = Math.Abs(vd - vRaw) > 1e-12;
        _vdLast = vd;

        var (id, gd) = Evaluate(vd);
        _slots.Stamp(ctx.Matrix, gd + ctx.Gmin, ctx.Scale);
        var ieq = id - gd * vd;
        _slots.StampCurrent(ctx.Rhs, ieq);
    }

    public override void LoadAc(ComplexLoadContext ctx)
    {
        var vd = _slots.Voltage(ctx.OperatingPoint);
        var (_, gd) = Evaluate(vd);
        _slots.Stamp(ctx.Matrix, new Complex(gd + ctx.Gmin, 0));
    }

    public override double Current(double[] x, RealLoadContext ctx)
    {
        var vd = _slots.Voltage(x);
        return Evaluate(vd).Id + ctx.Gmin * vd;
    }

    /// <summary>小訊號電導（含 gmin）於電壓 vd。</summary>
    public double SmallSignalConductance(double vd, double gmin) => Evaluate(vd).Gd + gmin;

    private (double Id, double Gd) Evaluate(double vd)
    {
        var arg = vd / _nvt;
        if (arg > MaxExponent)
        {
            // 超過範圍以切線延伸，避免溢位
            var e = Math.Exp(MaxExponent);
            var gdMax = _is * e / _nvt;
            var idMax = _is * (e - 1);
            return (idMax + gdMax * (vd - MaxExponent * _nvt), gdMax);
        }

        var ex = Math.Exp(arg);
        return (_is * (ex - 1), _is * ex / _nvt);
    }

    private double Limit(double vNew, double vOld)
    {
        var vcrit = VCrit;
        if (vNew > vcrit && Math.Abs(vNew - vOld) > 2 * _nvt)
        {
            if (vOld > 0)
            {
                var arg = 1 + (vNew - vOld) / _nvt;
                return arg > 0 ? vOld + _nvt * Math.Log(arg) : vcrit;
            }

            return _nvt * Math.Log(vNew / _nvt);
        }

        return vNew;
    }
}
