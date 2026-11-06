using CircuitBench.Core.Model;
using CircuitBench.ReferenceSolver;

namespace CircuitBench.TestKit;

/// <summary>把 Core 的電路轉成參考求解器的描述。只做欄位對應，不含任何求解邏輯。</summary>
public static class CoreToReference
{
    public static RefCircuit Convert(Circuit circuit)
    {
        var rc = new RefCircuit();
        foreach (var c in circuit.Components)
        {
            rc.Add(c switch
            {
                Resistor r => new RefElement(RefKind.Resistor, r.Name, [r.A.Value, r.B.Value], r.Resistance),
                Capacitor cap => new RefElement(RefKind.Capacitor, cap.Name, [cap.A.Value, cap.B.Value], cap.Capacitance),
                Inductor l => new RefElement(RefKind.Inductor, l.Name, [l.A.Value, l.B.Value], l.Inductance),
                VoltageSource v => Source(RefKind.VoltageSource, v.Name, v.Pos, v.Neg, v.Spec),
                CurrentSource i => Source(RefKind.CurrentSource, i.Name, i.Pos, i.Neg, i.Spec),
                Diode d => new RefElement(RefKind.Diode, d.Name, [d.Anode.Value, d.Cathode.Value], 0) { Is = d.Model.Is, N = d.Model.N },
                Vcvs e => new RefElement(RefKind.Vcvs, e.Name, [e.OutPos.Value, e.OutNeg.Value, e.CtrlPos.Value, e.CtrlNeg.Value], e.Gain),
                Vccs g => new RefElement(RefKind.Vccs, g.Name, [g.OutPos.Value, g.OutNeg.Value, g.CtrlPos.Value, g.CtrlNeg.Value], g.Transconductance),
                TimedSwitch s => new RefElement(RefKind.Switch, s.Name, [s.A.Value, s.B.Value], 0) { SwitchOn = s.IsOnAt, Ron = s.Ron, Roff = s.Roff },
                _ => throw new NotSupportedException(c.GetType().Name),
            });
        }

        return rc;
    }

    private static RefElement Source(RefKind kind, string name, NodeName pos, NodeName neg, SourceSpec spec) =>
        new(kind, name, [pos.Value, neg.Value], spec.EffectiveDcValue)
        {
            Waveform = spec.Transient.Evaluate,
            AcMagnitude = spec.Ac?.Magnitude ?? 0,
            AcPhaseDegrees = spec.Ac?.PhaseDegrees ?? 0,
        };
}
