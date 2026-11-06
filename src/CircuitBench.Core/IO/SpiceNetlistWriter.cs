using System.Globalization;
using System.Text;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.Model;

namespace CircuitBench.Core.IO;

/// <summary>寫出 SPICE 子集網表。數值使用 "R" 格式，保證匯出再匯入後完全相同。</summary>
public static class SpiceNetlistWriter
{
    public static string Write(SpiceDeck deck)
    {
        var sb = new StringBuilder();
        sb.AppendLine("* CircuitBench 匯出的 SPICE 子集網表");
        if (!string.IsNullOrWhiteSpace(deck.Title))
        {
            sb.AppendLine($".title {deck.Title}");
        }

        var modelNames = AssignModelNames(deck.Circuit);
        foreach (var (model, name) in modelNames.DistinctBy(p => p.Value))
        {
            sb.AppendLine($".model {name} D(Is={N(model.Is)} N={N(model.N)})");
        }

        foreach (var c in deck.Circuit.Components)
        {
            sb.AppendLine(c switch
            {
                Resistor r => $"{r.Name} {r.A} {r.B} {N(r.Resistance)}",
                Capacitor cap => $"{cap.Name} {cap.A} {cap.B} {N(cap.Capacitance)}",
                Inductor l => $"{l.Name} {l.A} {l.B} {N(l.Inductance)}",
                VoltageSource v => $"{v.Name} {v.Pos} {v.Neg}{Source(v.Spec)}",
                CurrentSource i => $"{i.Name} {i.Pos} {i.Neg}{Source(i.Spec)}",
                Diode d => $"{d.Name} {d.Anode} {d.Cathode} {modelNames[d.Model]}",
                Vcvs e => $"{e.Name} {e.OutPos} {e.OutNeg} {e.CtrlPos} {e.CtrlNeg} {N(e.Gain)}",
                Vccs g => $"{g.Name} {g.OutPos} {g.OutNeg} {g.CtrlPos} {g.CtrlNeg} {N(g.Transconductance)}",
                TimedSwitch s => $"{s.Name} {s.A} {s.B} times=({string.Join(" ", s.ToggleTimes.Select(N))}) initial={(s.InitiallyOn ? "on" : "off")} ron={N(s.Ron)} roff={N(s.Roff)}",
                _ => throw new NotSupportedException(c.GetType().Name),
            });
        }

        foreach (var a in deck.Analyses)
        {
            sb.AppendLine(a switch
            {
                OpCommand => ".op",
                DcCommand dc => $".dc {dc.SourceName} {N(dc.Start)} {N(dc.Stop)} {N(dc.Step)}",
                TranCommand tr => tr.MaxStep is { } max
                    ? $".tran {N(tr.Step)} {N(tr.Stop)} {N(tr.Start)} {N(max)}"
                    : tr.Start != 0 ? $".tran {N(tr.Step)} {N(tr.Stop)} {N(tr.Start)}" : $".tran {N(tr.Step)} {N(tr.Stop)}",
                AcCommand ac => $".ac {(ac.Type == AcSweepType.Decade ? "dec" : "lin")} {ac.Points} {N(ac.Start)} {N(ac.Stop)}",
                _ => throw new NotSupportedException(a.GetType().Name),
            });
        }

        sb.AppendLine(".end");
        return sb.ToString();
    }

    /// <summary>每個不同的二極體模型取得唯一名稱（同名但參數不同時加上序號）。</summary>
    private static Dictionary<DiodeModel, string> AssignModelNames(Circuit circuit)
    {
        var result = new Dictionary<DiodeModel, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in circuit.Components.OfType<Diode>())
        {
            if (result.ContainsKey(d.Model))
            {
                continue;
            }

            var name = d.Model.ModelName;
            var k = 2;
            while (!used.Add(name))
            {
                name = $"{d.Model.ModelName}_{k++}";
            }

            result[d.Model] = name;
        }

        return result;
    }

    private static string Source(SourceSpec spec)
    {
        var sb = new StringBuilder();
        if (spec.DcValue is { } dc)
        {
            sb.Append($" DC {N(dc)}");
        }

        if (spec.Ac is { } ac)
        {
            sb.Append($" AC {N(ac.Magnitude)} {N(ac.PhaseDegrees)}");
        }

        switch (spec.Waveform)
        {
            case null:
                break;
            case SineWaveform s:
                sb.Append($" SIN({N(s.Vo)} {N(s.Va)} {N(s.Freq)} {N(s.Td)} {N(s.Theta)} {N(s.PhaseDeg)})");
                break;
            case PulseWaveform p:
            {
                // 省略尾端預設值（PER=0、PW=無限大），讓往返保持完全相同
                var args = new List<double> { p.V1, p.V2, p.Td, p.Tr, p.Tf, p.Pw, p.Per };
                if (p.Per == 0)
                {
                    args.RemoveAt(6);
                    if (double.IsPositiveInfinity(p.Pw))
                    {
                        args.RemoveAt(5);
                    }
                }

                sb.Append($" PULSE({string.Join(" ", args.Select(N))})");
                break;
            }
            case PwlWaveform w:
                sb.Append($" PWL({string.Join(" ", w.Points.Select(pt => $"{N(pt.T)} {N(pt.V)}"))})");
                break;
            case DcWaveform d:
                sb.Append($" PWL(0 {N(d.Value)})");
                break;
        }

        return sb.ToString();
    }

    private static string N(double v) =>
        double.IsPositiveInfinity(v) ? "1e308" : v.ToString("R", CultureInfo.InvariantCulture);
}
