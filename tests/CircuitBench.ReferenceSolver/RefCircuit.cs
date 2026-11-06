namespace CircuitBench.ReferenceSolver;

/// <summary>參考求解器自己的元件種類；刻意不使用 Core 的型別。</summary>
public enum RefKind
{
    Resistor,
    Capacitor,
    Inductor,
    VoltageSource,
    CurrentSource,
    Diode,
    Vcvs,
    Vccs,
    Switch,
}

/// <summary>
/// 參考求解器的元件描述。Nodes 長度：兩端元件 2，受控源 4（out+, out-, ctrl+, ctrl-）。
/// Value：R/C/L 的值、電源的直流值、受控源的增益或轉導。
/// </summary>
public sealed record RefElement(RefKind Kind, string Name, string[] Nodes, double Value)
{
    /// <summary>暫態波形（電源）；null 代表固定為 Value。</summary>
    public Func<double, double>? Waveform { get; init; }

    public double AcMagnitude { get; init; }

    public double AcPhaseDegrees { get; init; }

    public double Is { get; init; } = 1e-14;

    public double N { get; init; } = 1;

    /// <summary>開關狀態（時間 → 是否導通）。</summary>
    public Func<double, bool>? SwitchOn { get; init; }

    public double Ron { get; init; } = 1e-3;

    public double Roff { get; init; } = 1e12;

    public double SourceAt(double t) => Waveform?.Invoke(t) ?? Value;
}

public sealed class RefCircuit
{
    public List<RefElement> Elements { get; } = [];

    public RefCircuit Add(RefElement e)
    {
        Elements.Add(e);
        return this;
    }
}
