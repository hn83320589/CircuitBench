using System.Numerics;

namespace CircuitBench.Core.Mna;

public enum IntegrationMethod
{
    BackwardEuler,
    Trapezoidal,
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
