using System.Globalization;
using CircuitBench.Core.IO;
using CircuitBench.Core.Model;
using CircuitBench.Core.Units;
using CircuitBench.Schematic.Model;

namespace CircuitBench.Schematic.Connectivity;

/// <summary>把圖面元件（參數為字串）轉成 Core 元件；參數錯誤時回傳中文原因。</summary>
public static class ComponentFactory
{
    public static Component? TryCreate(ComponentSymbol symbol, IReadOnlyList<NodeName> nodes, List<string> errors)
    {
        double Value(string key, string label)
        {
            var text = symbol.Parameter(key);
            if (!EngineeringNotation.TryParse(text, out var v, out var e))
            {
                errors.Add($"{label}：{e.MessageZh}");
                return double.NaN;
            }

            return v;
        }

        var name = symbol.Name;
        switch (symbol.Kind)
        {
            case ComponentKind.Resistor:
                return new Resistor(name, nodes[0], nodes[1], Value("value", "電阻值"));
            case ComponentKind.Capacitor:
                return new Capacitor(name, nodes[0], nodes[1], Value("value", "電容值"));
            case ComponentKind.Inductor:
                return new Inductor(name, nodes[0], nodes[1], Value("value", "電感值"));
            case ComponentKind.VoltageSource:
            case ComponentKind.CurrentSource:
            {
                var spec = SourceText(symbol);
                if (!SpiceNetlistReader.TryParseSourceSpec(spec, name, out var s, out var err))
                {
                    errors.Add(err);
                    return null;
                }

                return symbol.Kind == ComponentKind.VoltageSource
                    ? new VoltageSource(name, nodes[0], nodes[1], s)
                    : new CurrentSource(name, nodes[0], nodes[1], s);
            }

            case ComponentKind.Diode:
                return new Diode(name, nodes[0], nodes[1], new DiodeModel($"D_{name}", Value("is", "飽和電流 Is"), Value("n", "理想因子 n")));
            case ComponentKind.Vcvs:
                return new Vcvs(name, nodes[0], nodes[1], nodes[2], nodes[3], Value("gain", "增益"));
            case ComponentKind.Vccs:
                return new Vccs(name, nodes[0], nodes[1], nodes[2], nodes[3], Value("gain", "轉導"));
            case ComponentKind.Switch:
            {
                if (!SpiceNetlistReader.TryParseNumberList(symbol.Parameter("times"), "切換時間", out var times, out var err))
                {
                    errors.Add(err);
                    return null;
                }

                var initial = symbol.Parameter("initial").Trim().ToLowerInvariant();
                if (initial is not ("on" or "off"))
                {
                    errors.Add($"初始狀態只能是 on 或 off（目前為「{symbol.Parameter("initial")}」）。");
                    return null;
                }

                return new TimedSwitch(name, nodes[0], nodes[1], times, initial == "on", Value("ron", "Ron"), Value("roff", "Roff"));
            }

            default:
                throw new NotSupportedException(symbol.Kind.ToString());
        }
    }

    /// <summary>電源參數組成與網表相同的規格文字：DC x [AC mag phase] [waveform]。</summary>
    public static string SourceText(ComponentSymbol symbol)
    {
        var parts = new List<string>();
        var dc = symbol.Parameter("dc").Trim();
        if (dc.Length > 0)
        {
            parts.Add($"DC {dc}");
        }

        var ac = symbol.Parameter("ac").Trim();
        if (ac.Length > 0)
        {
            parts.Add($"AC {ac}");
        }

        var waveform = symbol.Parameter("waveform").Trim();
        if (waveform.Length > 0)
        {
            parts.Add(waveform);
        }

        return string.Join(" ", parts);
    }

    /// <summary>反向：Core 元件 → 圖面參數字串（匯入網表時使用）。</summary>
    public static IReadOnlyDictionary<string, string> ToParameters(Component c)
    {
        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        return c switch
        {
            Resistor r => new Dictionary<string, string> { ["value"] = N(r.Resistance) },
            Capacitor cap => new Dictionary<string, string> { ["value"] = N(cap.Capacitance) },
            Inductor l => new Dictionary<string, string> { ["value"] = N(l.Inductance) },
            VoltageSource v => SourceParameters(v.Spec),
            CurrentSource i => SourceParameters(i.Spec),
            Diode d => new Dictionary<string, string> { ["is"] = N(d.Model.Is), ["n"] = N(d.Model.N) },
            Vcvs e => new Dictionary<string, string> { ["gain"] = N(e.Gain) },
            Vccs g => new Dictionary<string, string> { ["gain"] = N(g.Transconductance) },
            TimedSwitch s => new Dictionary<string, string>
            {
                ["times"] = string.Join(" ", s.ToggleTimes.Select(N)),
                ["initial"] = s.InitiallyOn ? "on" : "off",
                ["ron"] = N(s.Ron),
                ["roff"] = N(s.Roff),
            },
            _ => throw new NotSupportedException(c.GetType().Name),
        };
    }

    public static ComponentKind KindOf(Component c) => c switch
    {
        Resistor => ComponentKind.Resistor,
        Capacitor => ComponentKind.Capacitor,
        Inductor => ComponentKind.Inductor,
        VoltageSource => ComponentKind.VoltageSource,
        CurrentSource => ComponentKind.CurrentSource,
        Diode => ComponentKind.Diode,
        Vcvs => ComponentKind.Vcvs,
        Vccs => ComponentKind.Vccs,
        TimedSwitch => ComponentKind.Switch,
        _ => throw new NotSupportedException(c.GetType().Name),
    };

    private static Dictionary<string, string> SourceParameters(SourceSpec spec)
    {
        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        return new Dictionary<string, string>
        {
            ["dc"] = spec.DcValue is { } dc ? N(dc) : string.Empty,
            ["ac"] = spec.Ac is { } ac ? $"{N(ac.Magnitude)} {N(ac.PhaseDegrees)}" : string.Empty,
            ["waveform"] = spec.Waveform is { } w ? SpiceNetlistWriter.FormatWaveform(w) : string.Empty,
        };
    }
}
