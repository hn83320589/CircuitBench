using System.Collections.Immutable;

namespace CircuitBench.Core.Model;

/// <summary>電路元件（純資料，不含模擬行為）。所有數值為 SI 單位。</summary>
public abstract record Component(string Name)
{
    public abstract IReadOnlyList<NodeName> Nodes { get; }

    /// <summary>元件類型的中文名稱，用於錯誤訊息。</summary>
    public abstract string KindNameZh { get; }
}

public sealed record Resistor(string Name, NodeName A, NodeName B, double Resistance) : Component(Name)
{
    public override IReadOnlyList<NodeName> Nodes => [A, B];
    public override string KindNameZh => "電阻";
}

public sealed record Capacitor(string Name, NodeName A, NodeName B, double Capacitance) : Component(Name)
{
    public override IReadOnlyList<NodeName> Nodes => [A, B];
    public override string KindNameZh => "電容";
}

public sealed record Inductor(string Name, NodeName A, NodeName B, double Inductance) : Component(Name)
{
    public override IReadOnlyList<NodeName> Nodes => [A, B];
    public override string KindNameZh => "電感";
}

/// <summary>獨立電壓源。支路電流以「由正端流入元件」為正。</summary>
public sealed record VoltageSource(string Name, NodeName Pos, NodeName Neg, SourceSpec Spec) : Component(Name)
{
    public override IReadOnlyList<NodeName> Nodes => [Pos, Neg];
    public override string KindNameZh => "電壓源";

    public static VoltageSource Dc(string name, NodeName pos, NodeName neg, double value) =>
        new(name, pos, neg, SourceSpec.FromDc(value));
}

/// <summary>獨立電流源：電流由 Pos 經元件內部流向 Neg（與 SPICE 相同）。</summary>
public sealed record CurrentSource(string Name, NodeName Pos, NodeName Neg, SourceSpec Spec) : Component(Name)
{
    public override IReadOnlyList<NodeName> Nodes => [Pos, Neg];
    public override string KindNameZh => "電流源";

    public static CurrentSource Dc(string name, NodeName pos, NodeName neg, double value) =>
        new(name, pos, neg, SourceSpec.FromDc(value));
}

public sealed record DiodeModel(string ModelName, double Is = 1e-14, double N = 1.0)
{
    public static DiodeModel Default { get; } = new("D");
}

public sealed record Diode(string Name, NodeName Anode, NodeName Cathode, DiodeModel Model) : Component(Name)
{
    public override IReadOnlyList<NodeName> Nodes => [Anode, Cathode];
    public override string KindNameZh => "二極體";
}

/// <summary>電壓控制電壓源 E：V(OutPos,OutNeg) = Gain · V(CtrlPos,CtrlNeg)。</summary>
public sealed record Vcvs(string Name, NodeName OutPos, NodeName OutNeg, NodeName CtrlPos, NodeName CtrlNeg, double Gain) : Component(Name)
{
    public override IReadOnlyList<NodeName> Nodes => [OutPos, OutNeg, CtrlPos, CtrlNeg];
    public override string KindNameZh => "電壓控制電壓源";
}

/// <summary>電壓控制電流源 G：由 OutPos 經元件流向 OutNeg 的電流 = Gm · V(CtrlPos,CtrlNeg)。</summary>
public sealed record Vccs(string Name, NodeName OutPos, NodeName OutNeg, NodeName CtrlPos, NodeName CtrlNeg, double Transconductance) : Component(Name)
{
    public override IReadOnlyList<NodeName> Nodes => [OutPos, OutNeg, CtrlPos, CtrlNeg];
    public override string KindNameZh => "電壓控制電流源";
}

/// <summary>依時間切換的理想開關：在每個 ToggleTimes 時刻切換狀態。</summary>
public sealed record TimedSwitch(
    string Name, NodeName A, NodeName B, ImmutableArray<double> ToggleTimes, bool InitiallyOn,
    double Ron = 1e-3, double Roff = 1e12) : Component(Name)
{
    public override IReadOnlyList<NodeName> Nodes => [A, B];
    public override string KindNameZh => "開關";

    /// <summary>時刻 t 的狀態。切換時刻本身採用切換後的狀態。</summary>
    public bool IsOnAt(double t)
    {
        var on = InitiallyOn;
        foreach (var toggle in ToggleTimes)
        {
            if (t < toggle)
            {
                break;
            }

            on = !on;
        }

        return on;
    }

    public bool Equals(TimedSwitch? other) =>
        other is not null && Name == other.Name && A == other.A && B == other.B &&
        ToggleTimes.SequenceEqual(other.ToggleTimes) && InitiallyOn == other.InitiallyOn &&
        Ron.Equals(other.Ron) && Roff.Equals(other.Roff);

    public override int GetHashCode() => HashCode.Combine(Name, A, B, ToggleTimes.Length, InitiallyOn, Ron, Roff);
}
