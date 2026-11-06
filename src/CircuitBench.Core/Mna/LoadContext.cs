using System.Numerics;

namespace CircuitBench.Core.Mna;

public enum IntegrationMethod
{
    BackwardEuler,
    Trapezoidal,
    Bdf2,
}

/// <summary>
/// 微分近似：x'(n+1) ≈ A0·x(n+1) + A1·x(n) + A2·x(n−1) + B·x'(n)。
/// 元件以此組出伴隨模型，因此新增積分方法不需改動元件。
/// </summary>
public readonly record struct IntegrationCoefficients(double A0, double A1, double A2, double B)
{
    public static IntegrationCoefficients BackwardEuler(double h) => new(1 / h, -1 / h, 0, 0);

    public static IntegrationCoefficients Trapezoidal(double h) => new(2 / h, -2 / h, 0, -1);

    /// <summary>可變步長 BDF2：h = t(n+1) − t(n)，hPrev = t(n) − t(n−1)。</summary>
    public static IntegrationCoefficients Bdf2(double h, double hPrev)
    {
        var rho = h / hPrev;
        return new((1 + 2 * rho) / ((1 + rho) * h), -(1 + rho) / h, rho * rho / ((1 + rho) * h), 0);
    }
}

public enum LoadMode
{
    /// <summary>直流：電容開路、電感短路，電源取直流值（含掃描覆寫）。</summary>
    Dc,

    /// <summary>暫態：電容與電感使用伴隨模型，電源取時刻 Time 的波形值。</summary>
    Transient,
}

/// <summary>實數 MNA 組裝時傳給各元件的資訊。</summary>
public sealed class RealLoadContext(double[] matrixValues, double[] rhs)
{
    public double[] Matrix { get; } = matrixValues;

    public double[] Rhs { get; } = rhs;

    /// <summary>目前的 Newton 迭代值（非線性元件在此線性化）。</summary>
    public double[] Solution { get; set; } = [];

    public LoadMode Mode { get; set; }

    public double Time { get; set; }

    /// <summary>暫態起點的直流工作點：電源改用波形在 t=0 的值。</summary>
    public bool UseWaveformsInDc { get; set; }

    /// <summary>電源步進用的縮放比例（0~1）。</summary>
    public double SourceScale { get; set; } = 1;

    /// <summary>二極體並聯的 gmin。</summary>
    public double Gmin { get; set; } = 1e-12;

    public double TimeStep { get; set; }

    public IntegrationMethod Method { get; set; }

    public IntegrationCoefficients Coefficients { get; set; }
}

/// <summary>交流（複數）MNA 組裝資訊；非線性元件在直流工作點線性化。</summary>
public sealed class ComplexLoadContext(Complex[] matrixValues, Complex[] rhs)
{
    public Complex[] Matrix { get; } = matrixValues;

    public Complex[] Rhs { get; } = rhs;

    public double Omega { get; set; }

    public double[] OperatingPoint { get; set; } = [];

    public double Gmin { get; set; } = 1e-12;
}
