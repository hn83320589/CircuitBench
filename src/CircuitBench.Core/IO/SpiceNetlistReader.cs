using System.Collections.Immutable;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.Model;
using CircuitBench.Core.Units;

namespace CircuitBench.Core.IO;

/// <summary>
/// SPICE 子集網表讀取。每行一個元件，名稱首字母決定類型（R C L V I D E G S）；
/// * 開頭為註解；+ 開頭為上一行的接續；不分大小寫；支援 .tran .ac .dc .op .model .title .end。
/// 任何錯誤都會累積成「行號 + 中文原因」，不會丟出例外。
/// </summary>
public static class SpiceNetlistReader
{
    private sealed record LogicalLine(int LineNumber, List<string> Tokens);

    private sealed class ParseException(string message) : Exception(message);

    public static SpiceParseResult Parse(string text)
    {
        var errors = new List<SpiceLineError>();
        var lines = JoinContinuations(text, errors);

        var models = new Dictionary<string, DiodeModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Where(l => l.Tokens[0].Equals(".model", StringComparison.OrdinalIgnoreCase)))
        {
            Try(line, errors, () =>
            {
                var model = ParseModel(line.Tokens);
                if (!models.TryAdd(model.ModelName, model))
                {
                    throw new ParseException($"模型「{model.ModelName}」重複定義。");
                }
            });
        }

        var components = new List<Component>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var analyses = new List<AnalysisCommand>();
        string? title = null;
        foreach (var line in lines)
        {
            var head = line.Tokens[0];
            if (head.StartsWith('.'))
            {
                var directive = head.ToLowerInvariant();
                if (directive == ".end")
                {
                    break;
                }

                if (directive == ".model")
                {
                    continue;
                }

                if (directive == ".title")
                {
                    title = string.Join(" ", line.Tokens.Skip(1));
                    continue;
                }

                Try(line, errors, () => analyses.Add(ParseAnalysis(directive, line.Tokens)));
                continue;
            }

            Try(line, errors, () =>
            {
                var component = ParseComponent(line.Tokens, models);
                if (!names.Add(component.Name))
                {
                    throw new ParseException($"元件名稱「{component.Name}」重複。");
                }

                components.Add(component);
            });
        }

        if (errors.Count > 0)
        {
            return new SpiceParseResult(null, errors);
        }

        return new SpiceParseResult(new SpiceDeck(Circuit.Create(components), analyses, title), errors);
    }

    /// <summary>
    /// 解析電源規格文字（與網表相同語法，例如 "DC 5 AC 1 0 SIN(0 1 1k)"），供圖面參數使用。
    /// 失敗時回傳中文錯誤訊息。
    /// </summary>
    public static bool TryParseSourceSpec(string text, string componentName, out SourceSpec spec, out string error)
    {
        try
        {
            spec = ParseSource(Tokenize(text), 0, componentName);
            error = string.Empty;
            return true;
        }
        catch (ParseException ex)
        {
            spec = SourceSpec.FromDc(0);
            error = ex.Message;
            return false;
        }
    }

    /// <summary>解析以空白分隔的時間清單（開關切換時間）。</summary>
    public static bool TryParseNumberList(string text, string what, out ImmutableArray<double> values, out string error)
    {
        try
        {
            values = [.. Tokenize(text).Select(t => Number(t, what))];
            error = string.Empty;
            return true;
        }
        catch (ParseException ex)
        {
            values = [];
            error = ex.Message;
            return false;
        }
    }

    private static void Try(LogicalLine line, List<SpiceLineError> errors, Action action)
    {
        try
        {
            action();
        }
        catch (ParseException ex)
        {
            errors.Add(new SpiceLineError(line.LineNumber, ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            // 防禦：任何非預期的格式問題都轉成該行的錯誤，不讓匯入崩潰
            errors.Add(new SpiceLineError(line.LineNumber, $"無法解析此行：{ex.Message}"));
        }
    }

    private static List<LogicalLine> JoinContinuations(string text, List<SpiceLineError> errors)
    {
        var result = new List<LogicalLine>();
        var raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < raw.Length; i++)
        {
            var line = raw[i].Trim();
            var number = i + 1;
            if (line.Length == 0 || line.StartsWith('*'))
            {
                continue;
            }

            if (line.StartsWith('+'))
            {
                if (result.Count == 0)
                {
                    errors.Add(new SpiceLineError(number, "接續行（+ 開頭）前面沒有可以接續的行。"));
                    continue;
                }

                result[^1].Tokens.AddRange(Tokenize(line[1..]));
                continue;
            }

            var tokens = Tokenize(line);
            if (tokens.Count > 0)
            {
                result.Add(new LogicalLine(number, tokens));
            }
        }

        return result;
    }

    /// <summary>以空白與逗號分隔；括號與等號各自成為獨立的 token。</summary>
    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        foreach (var ch in line)
        {
            if (char.IsWhiteSpace(ch) || ch == ',')
            {
                Flush();
            }
            else if (ch is '(' or ')' or '=')
            {
                Flush();
                tokens.Add(ch.ToString());
            }
            else
            {
                current.Append(ch);
            }
        }

        Flush();
        return tokens;
    }

    private static double Number(string token, string what)
    {
        if (!EngineeringNotation.TryParse(token, out var value, out var error))
        {
            throw new ParseException($"{what}「{token}」不是有效的數值：{error.MessageZh}");
        }

        return value;
    }

    private static NodeName Node(List<string> t, int index, string name)
    {
        if (index >= t.Count || t[index] is "(" or ")" or "=")
        {
            throw new ParseException($"元件 {name} 缺少第 {index} 個節點。");
        }

        return new NodeName(t[index]);
    }

    private static Component ParseComponent(List<string> t, Dictionary<string, DiodeModel> models)
    {
        var name = t[0];
        var kind = char.ToUpperInvariant(name[0]);
        switch (kind)
        {
            case 'R':
            case 'C':
            case 'L':
            {
                RequireCount(t, 4, name, "名稱 節點1 節點2 數值");
                var a = Node(t, 1, name);
                var b = Node(t, 2, name);
                var value = Number(t[3], "數值");
                ExpectEnd(t, 4, name);
                return kind switch
                {
                    'R' => new Resistor(name, a, b, value),
                    'C' => new Capacitor(name, a, b, value),
                    _ => new Inductor(name, a, b, value),
                };
            }

            case 'V':
            case 'I':
            {
                RequireCount(t, 3, name, "名稱 正端 負端 [DC 值] [AC 振幅 相位] [SIN/PULSE/PWL(...)]");
                var spec = ParseSource(t, 3, name);
                var pos = Node(t, 1, name);
                var neg = Node(t, 2, name);
                return kind == 'V' ? new VoltageSource(name, pos, neg, spec) : new CurrentSource(name, pos, neg, spec);
            }

            case 'D':
            {
                RequireCount(t, 3, name, "名稱 陽極 陰極 [模型]");
                var model = DiodeModel.Default;
                if (t.Count >= 4)
                {
                    if (!models.TryGetValue(t[3], out model!))
                    {
                        throw new ParseException($"二極體 {name} 使用的模型「{t[3]}」沒有以 .model 定義。");
                    }
                }

                ExpectEnd(t, 4, name);
                return new Diode(name, Node(t, 1, name), Node(t, 2, name), model);
            }

            case 'E':
            case 'G':
            {
                RequireCount(t, 6, name, "名稱 輸出正 輸出負 控制正 控制負 增益");
                var value = Number(t[5], kind == 'E' ? "增益" : "轉導");
                ExpectEnd(t, 6, name);
                return kind == 'E'
                    ? new Vcvs(name, Node(t, 1, name), Node(t, 2, name), Node(t, 3, name), Node(t, 4, name), value)
                    : new Vccs(name, Node(t, 1, name), Node(t, 2, name), Node(t, 3, name), Node(t, 4, name), value);
            }

            case 'S':
                return ParseSwitch(t, name);

            default:
                throw new ParseException($"無法辨識的元件「{name}」：名稱的第一個字母必須是 R、C、L、V、I、D、E、G 或 S。");
        }
    }

    private static void RequireCount(List<string> t, int count, string name, string syntax)
    {
        if (t.Count < count)
        {
            throw new ParseException($"元件 {name} 的欄位不足，格式應為：{syntax}。");
        }
    }

    private static void ExpectEnd(List<string> t, int count, string name)
    {
        if (t.Count > count)
        {
            throw new ParseException($"元件 {name} 有多餘的欄位「{t[count]}」。");
        }
    }

    private static SourceSpec ParseSource(List<string> t, int start, string name)
    {
        double? dc = null;
        AcSpec? ac = null;
        Waveform? waveform = null;
        var i = start;
        while (i < t.Count)
        {
            var token = t[i].ToUpperInvariant();
            switch (token)
            {
                case "DC":
                    if (i + 1 >= t.Count) throw new ParseException($"電源 {name} 的 DC 後面缺少數值。");
                    dc = Number(t[i + 1], "直流值");
                    i += 2;
                    break;
                case "AC":
                {
                    if (i + 1 >= t.Count) throw new ParseException($"電源 {name} 的 AC 後面缺少振幅。");
                    var mag = Number(t[i + 1], "交流振幅");
                    var phase = 0.0;
                    i += 2;
                    if (i < t.Count && EngineeringNotation.TryParse(t[i], out var p, out _))
                    {
                        phase = p;
                        i++;
                    }

                    ac = new AcSpec(mag, phase);
                    break;
                }

                case "SIN":
                case "PULSE":
                case "PWL":
                {
                    if (waveform is not null) throw new ParseException($"電源 {name} 只能有一個暫態函數。");
                    var (args, next) = ReadArguments(t, i + 1, token, name);
                    waveform = token switch
                    {
                        "SIN" => MakeSine(args, name),
                        "PULSE" => MakePulse(args, name),
                        _ => MakePwl(args, name),
                    };
                    i = next;
                    break;
                }

                default:
                    if (i == start && EngineeringNotation.TryParse(t[i], out var bare, out _))
                    {
                        dc = bare;
                        i++;
                        break;
                    }

                    throw new ParseException($"電源 {name} 有無法辨識的參數「{t[i]}」。");
            }
        }

        return new SourceSpec(dc, waveform, ac);
    }

    private static (List<double> Args, int Next) ReadArguments(List<string> t, int i, string function, string name)
    {
        var args = new List<double>();
        var parenthesized = i < t.Count && t[i] == "(";
        if (parenthesized)
        {
            i++;
        }

        while (i < t.Count && t[i] != ")")
        {
            if (!parenthesized && !EngineeringNotation.TryParse(t[i], out _, out _))
            {
                break;
            }

            args.Add(Number(t[i], $"{function} 的參數"));
            i++;
        }

        if (parenthesized)
        {
            if (i >= t.Count) throw new ParseException($"電源 {name} 的 {function}( 缺少右括號。");
            i++;
        }

        return (args, i);
    }

    private static SineWaveform MakeSine(List<double> a, string name)
    {
        if (a.Count < 3 || a.Count > 6)
        {
            throw new ParseException($"電源 {name} 的 SIN 需要 3 到 6 個參數：SIN(VO VA FREQ [TD [THETA [PHASE]]])。");
        }

        return new SineWaveform(a[0], a[1], a[2], At(a, 3), At(a, 4), At(a, 5));
    }

    private static PulseWaveform MakePulse(List<double> a, string name)
    {
        if (a.Count < 2 || a.Count > 7)
        {
            throw new ParseException($"電源 {name} 的 PULSE 需要 2 到 7 個參數：PULSE(V1 V2 [TD TR TF PW PER])。");
        }

        return new PulseWaveform(a[0], a[1], At(a, 2), At(a, 3), At(a, 4),
            a.Count > 5 ? a[5] : double.PositiveInfinity, a.Count > 6 ? a[6] : 0);
    }

    private static PwlWaveform MakePwl(List<double> a, string name)
    {
        if (a.Count < 2 || a.Count % 2 != 0)
        {
            throw new ParseException($"電源 {name} 的 PWL 需要成對的「時間 數值」。");
        }

        var points = Enumerable.Range(0, a.Count / 2).Select(k => (a[2 * k], a[2 * k + 1])).ToImmutableArray();
        for (var k = 1; k < points.Length; k++)
        {
            if (!(points[k].Item1 > points[k - 1].Item1))
            {
                throw new ParseException($"電源 {name} 的 PWL 時間必須嚴格遞增（第 {k + 1} 點）。");
            }
        }

        return new PwlWaveform(points);
    }

    private static double At(List<double> a, int i) => i < a.Count ? a[i] : 0;

    private static TimedSwitch ParseSwitch(List<string> t, string name)
    {
        RequireCount(t, 3, name, "S名稱 n1 n2 times=(t1 t2 …) initial=off");
        var a = Node(t, 1, name);
        var b = Node(t, 2, name);
        var times = ImmutableArray<double>.Empty;
        var initialOn = false;
        double ron = 1e-3, roff = 1e12;
        var i = 3;
        while (i < t.Count)
        {
            var key = t[i].ToLowerInvariant();
            if (i + 1 >= t.Count || t[i + 1] != "=")
            {
                throw new ParseException($"開關 {name} 的參數「{t[i]}」格式錯誤，應為 名稱=值。");
            }

            i += 2;
            switch (key)
            {
                case "times":
                {
                    if (i >= t.Count || t[i] != "(") throw new ParseException($"開關 {name} 的 times 必須寫成 times=(t1 t2 …)。");
                    var (args, next) = ReadArguments(t, i, "times", name);
                    times = [.. args];
                    i = next;
                    break;
                }

                case "initial":
                    if (i >= t.Count) throw new ParseException($"開關 {name} 的 initial 缺少值。");
                    initialOn = t[i].ToLowerInvariant() switch
                    {
                        "on" => true,
                        "off" => false,
                        _ => throw new ParseException($"開關 {name} 的 initial 只能是 on 或 off（目前為「{t[i]}」）。"),
                    };
                    i++;
                    break;
                case "ron":
                    ron = Number(t[i++], "Ron");
                    break;
                case "roff":
                    roff = Number(t[i++], "Roff");
                    break;
                default:
                    throw new ParseException($"開關 {name} 有無法辨識的參數「{key}」。");
            }
        }

        return new TimedSwitch(name, a, b, times, initialOn, ron, roff);
    }

    private static DiodeModel ParseModel(List<string> t)
    {
        if (t.Count < 3)
        {
            throw new ParseException(".model 格式應為：.model 名稱 D(Is=值 N=值)。");
        }

        if (!t[2].Equals("D", StringComparison.OrdinalIgnoreCase))
        {
            throw new ParseException($".model 只支援二極體（D），不支援「{t[2]}」。");
        }

        double isat = 1e-14, n = 1;
        for (var i = 3; i < t.Count; i++)
        {
            if (t[i] is "(" or ")")
            {
                continue;
            }

            if (i + 2 >= t.Count || t[i + 1] != "=")
            {
                throw new ParseException($".model 的參數「{t[i]}」格式錯誤，應為 名稱=值。");
            }

            var value = Number(t[i + 2], t[i]);
            switch (t[i].ToLowerInvariant())
            {
                case "is":
                    isat = value;
                    break;
                case "n":
                    n = value;
                    break;
                default:
                    throw new ParseException($".model 只支援 Is 與 N 參數，不支援「{t[i]}」。");
            }

            i += 2;
        }

        return new DiodeModel(t[1], isat, n);
    }

    private static AnalysisCommand ParseAnalysis(string directive, List<string> t)
    {
        switch (directive)
        {
            case ".op":
                return new OpCommand();
            case ".tran":
                if (t.Count < 3 || t.Count > 5) throw new ParseException(".tran 格式應為：.tran TSTEP TSTOP [TSTART [TMAX]]。");
                return new TranCommand(Number(t[1], "TSTEP"), Number(t[2], "TSTOP"),
                    t.Count > 3 ? Number(t[3], "TSTART") : 0, t.Count > 4 ? Number(t[4], "TMAX") : null);
            case ".ac":
            {
                if (t.Count != 5) throw new ParseException(".ac 格式應為：.ac DEC|LIN 點數 起始頻率 終止頻率。");
                var type = t[1].ToLowerInvariant() switch
                {
                    "dec" => AcSweepType.Decade,
                    "lin" => AcSweepType.Linear,
                    _ => throw new ParseException($".ac 的掃描方式只支援 DEC 或 LIN（目前為「{t[1]}」）。"),
                };
                var points = Number(t[2], "點數");
                if (points < 1 || points != Math.Floor(points)) throw new ParseException(".ac 的點數必須是正整數。");
                return new AcCommand(type, (int)points, Number(t[3], "起始頻率"), Number(t[4], "終止頻率"));
            }

            case ".dc":
                if (t.Count != 5) throw new ParseException(".dc 格式應為：.dc 電源名稱 起始值 終止值 步進。");
                return new DcCommand(t[1], Number(t[2], "起始值"), Number(t[3], "終止值"), Number(t[4], "步進"));
            default:
                throw new ParseException($"不支援的指令「{t[0]}」。支援：.op .dc .tran .ac .model .title .end。");
        }
    }
}
