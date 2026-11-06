using System.Collections.Immutable;
using CircuitBench.Core.Model;

namespace CircuitBench.Core.Validation;

public enum IssueCode
{
    NoGround,
    FloatingNode,
    VoltageSourceInductorLoop,
    UnconnectedPin,
    InvalidParameter,
    DuplicateName,
    TooManyNodes,
}

/// <summary>電路檢查的問題，指出相關的元件與節點，讓 UI 能在圖上標示。</summary>
public sealed record CircuitIssue(
    IssueCode Code,
    string MessageZh,
    ImmutableArray<string> ComponentNames,
    ImmutableArray<NodeName> Nodes,
    string? PinName = null);

/// <summary>
/// 分析前的電路檢查。直流路徑：電阻、電感、電壓源、受控電壓源的輸出端、二極體、開關；
/// 電容與電流源（含受控電流源）不算。
/// </summary>
public static class CircuitValidator
{
    public const int MaxNodes = 5000;

    public static IReadOnlyList<CircuitIssue> Validate(Circuit circuit, IEnumerable<CircuitIssue>? extractionIssues = null, bool enforceNodeLimit = true)
    {
        var issues = new List<CircuitIssue>(extractionIssues ?? []);
        if (circuit.Components.Count == 0)
        {
            return issues;
        }

        if (enforceNodeLimit && circuit.Nodes.Count > MaxNodes)
        {
            issues.Add(new CircuitIssue(IssueCode.TooManyNodes,
                $"電路有 {circuit.Nodes.Count:N0} 個節點，超過上限 {MaxNodes:N0} 個。", [], []));
        }

        CheckParameters(circuit, issues);

        if (!circuit.Nodes.Any(n => n.IsGround))
        {
            issues.Add(new CircuitIssue(IssueCode.NoGround,
                "電路沒有接地。請放置接地符號（節點 0），所有電壓都以接地為基準。",
                [.. circuit.Components.Select(c => c.Name)], []));
        }
        else
        {
            CheckFloatingNodes(circuit, issues);
        }

        CheckVoltageLoops(circuit, issues);
        if (extractionIssues is null)
        {
            CheckDanglingNodes(circuit, issues);
        }

        return issues;
    }

    private static IEnumerable<(NodeName A, NodeName B, Component C)> DcPathEdges(Component c) => c switch
    {
        Resistor r => [(r.A, r.B, c)],
        Inductor l => [(l.A, l.B, c)],
        VoltageSource v => [(v.Pos, v.Neg, c)],
        Vcvs e => [(e.OutPos, e.OutNeg, c)],
        Diode d => [(d.Anode, d.Cathode, c)],
        TimedSwitch s => [(s.A, s.B, c)],
        _ => [],
    };

    private static void CheckFloatingNodes(Circuit circuit, List<CircuitIssue> issues)
    {
        var uf = new UnionFind<NodeName>();
        foreach (var n in circuit.Nodes)
        {
            uf.Add(n);
        }

        foreach (var c in circuit.Components)
        {
            foreach (var (a, b, _) in DcPathEdges(c))
            {
                uf.Union(a, b);
            }
        }

        var groundRoot = uf.Find(NodeName.Ground);
        var floatingGroups = circuit.Nodes
            .Where(n => !uf.Find(n).Equals(groundRoot))
            .GroupBy(uf.Find);

        foreach (var group in floatingGroups)
        {
            var nodes = group.ToImmutableArray();
            var set = nodes.ToHashSet();
            var related = circuit.Components.Where(c => c.Nodes.Any(set.Contains)).Select(c => c.Name).ToImmutableArray();
            var nodeText = string.Join("、", nodes.Select(n => n.Value));
            issues.Add(new CircuitIssue(IssueCode.FloatingNode,
                $"節點 {nodeText} 是浮接節點：沒有直流路徑通到接地（只經過電容或電流源相連），直流電壓無法決定。" +
                $"請加一個到接地的電阻或其他直流路徑。相關元件：{string.Join("、", related)}。",
                related, nodes));
        }
    }

    private static void CheckVoltageLoops(Circuit circuit, List<CircuitIssue> issues)
    {
        // 森林中的鄰接表，用來在發現迴路時找出路徑
        var adjacency = new Dictionary<NodeName, List<(NodeName Other, Component C)>>();
        var uf = new UnionFind<NodeName>();
        foreach (var n in circuit.Nodes)
        {
            uf.Add(n);
            adjacency[n] = [];
        }

        foreach (var c in circuit.Components)
        {
            (NodeName A, NodeName B)? edge = c switch
            {
                VoltageSource v => (v.Pos, v.Neg),
                Vcvs e => (e.OutPos, e.OutNeg),
                Inductor l => (l.A, l.B),
                _ => null,
            };
            if (edge is null)
            {
                continue;
            }

            var (a, b) = edge.Value;

            if (!uf.Union(a, b))
            {
                var path = FindPath(adjacency, a, b) ?? [];
                var loop = path.Append(c.Name).ToImmutableArray();
                issues.Add(new CircuitIssue(IssueCode.VoltageSourceInductorLoop,
                    $"元件 {string.Join("、", loop)} 形成只由電壓源（含受控電壓源）與電感構成的迴路，" +
                    "迴路電流無法決定。請在迴路中串聯一個電阻。",
                    loop, a.Equals(b) ? [a] : [a, b]));
                continue;
            }

            adjacency[a].Add((b, c));
            adjacency[b].Add((a, c));
        }
    }

    private static List<string>? FindPath(Dictionary<NodeName, List<(NodeName Other, Component C)>> adjacency, NodeName from, NodeName to)
    {
        if (from.Equals(to))
        {
            return [];
        }

        var previous = new Dictionary<NodeName, (NodeName Node, string Component)>();
        var queue = new Queue<NodeName>([from]);
        var visited = new HashSet<NodeName> { from };
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var (other, c) in adjacency[n])
            {
                if (!visited.Add(other))
                {
                    continue;
                }

                previous[other] = (n, c.Name);
                if (other.Equals(to))
                {
                    var path = new List<string>();
                    var cur = to;
                    while (!cur.Equals(from))
                    {
                        var (p, comp) = previous[cur];
                        path.Add(comp);
                        cur = p;
                    }

                    path.Reverse();
                    return path;
                }

                queue.Enqueue(other);
            }
        }

        return null;
    }

    /// <summary>網表層級：只被一個接腳使用的節點，視為該接腳未連接。</summary>
    private static void CheckDanglingNodes(Circuit circuit, List<CircuitIssue> issues)
    {
        var uses = new Dictionary<NodeName, List<(Component C, int Pin)>>();
        foreach (var c in circuit.Components)
        {
            for (var i = 0; i < c.Nodes.Count; i++)
            {
                if (!uses.TryGetValue(c.Nodes[i], out var list))
                {
                    list = [];
                    uses[c.Nodes[i]] = list;
                }

                list.Add((c, i));
            }
        }

        foreach (var (node, list) in uses)
        {
            if (node.IsGround || list.Count != 1)
            {
                continue;
            }

            var (c, pin) = list[0];
            var pinName = PinNames.For(c)[pin];
            issues.Add(new CircuitIssue(IssueCode.UnconnectedPin,
                $"{c.KindNameZh} {c.Name} 的接腳「{pinName}」（節點 {node}）沒有連接到其他元件。",
                [c.Name], [node], pinName));
        }
    }

    private static void CheckParameters(Circuit circuit, List<CircuitIssue> issues)
    {
        foreach (var c in circuit.Components)
        {
            foreach (var problem in ParameterProblems(c))
            {
                issues.Add(new CircuitIssue(IssueCode.InvalidParameter,
                    $"{c.KindNameZh} {c.Name} 的參數無效：{problem}", [c.Name], [.. c.Nodes]));
            }
        }
    }

    private static IEnumerable<string> ParameterProblems(Component c)
    {
        static bool Positive(double v) => double.IsFinite(v) && v > 0;
        switch (c)
        {
            case Resistor r when !Positive(r.Resistance):
                yield return $"電阻值必須大於 0（目前為 {r.Resistance}）。";
                break;
            case Capacitor cap when !Positive(cap.Capacitance):
                yield return $"電容值必須大於 0（目前為 {cap.Capacitance}）。";
                break;
            case Inductor l when !Positive(l.Inductance):
                yield return $"電感值必須大於 0（目前為 {l.Inductance}）。";
                break;
            case Diode d:
                if (!Positive(d.Model.Is)) yield return $"飽和電流 Is 必須大於 0（目前為 {d.Model.Is}）。";
                if (!Positive(d.Model.N)) yield return $"理想因子 n 必須大於 0（目前為 {d.Model.N}）。";
                break;
            case Vcvs e when !double.IsFinite(e.Gain):
                yield return "增益必須是有限的數值。";
                break;
            case Vccs g when !double.IsFinite(g.Transconductance):
                yield return "轉導必須是有限的數值。";
                break;
            case TimedSwitch s:
                if (!Positive(s.Ron)) yield return "導通電阻 Ron 必須大於 0。";
                if (!Positive(s.Roff)) yield return "截止電阻 Roff 必須大於 0。";
                if (s.ToggleTimes.Any(t => !double.IsFinite(t) || t < 0)) yield return "切換時間不可為負數。";
                if (!StrictlyIncreasing(s.ToggleTimes)) yield return "切換時間必須嚴格遞增。";
                break;
            case VoltageSource v:
                foreach (var p in SourceProblems(v.Spec)) yield return p;
                break;
            case CurrentSource i:
                foreach (var p in SourceProblems(i.Spec)) yield return p;
                break;
        }
    }

    private static IEnumerable<string> SourceProblems(SourceSpec spec)
    {
        if (spec.DcValue is { } dc && !double.IsFinite(dc))
        {
            yield return "直流值必須是有限的數值。";
        }

        switch (spec.Transient)
        {
            case PulseWaveform p:
                if (p.Tr < 0 || p.Tf < 0 || p.Pw < 0 || p.Td < 0) yield return "PULSE 的 TD、TR、TF、PW 不可為負數。";
                if (p.Per < 0) yield return "PULSE 的週期 PER 不可為負數。";
                if (p.Per > 0 && p.Tr + p.Pw + p.Tf > p.Per) yield return "PULSE 的 TR+PW+TF 不可大於週期 PER。";
                break;
            case PwlWaveform w:
                if (w.Points.IsDefaultOrEmpty) yield return "PWL 至少需要一個點。";
                else if (!StrictlyIncreasing(w.Points.Select(x => x.T))) yield return "PWL 的時間必須嚴格遞增。";
                break;
            case SineWaveform s:
                if (s.Freq < 0) yield return "SIN 的頻率不可為負數。";
                if (s.Td < 0) yield return "SIN 的延遲 TD 不可為負數。";
                break;
        }

        if (spec.Ac is { } ac && (!double.IsFinite(ac.Magnitude) || !double.IsFinite(ac.PhaseDegrees)))
        {
            yield return "交流振幅與相位必須是有限的數值。";
        }
    }

    private static bool StrictlyIncreasing(IEnumerable<double> values)
    {
        var previous = double.NegativeInfinity;
        foreach (var v in values)
        {
            if (!(v > previous))
            {
                return false;
            }

            previous = v;
        }

        return true;
    }

    private sealed class UnionFind<T>
        where T : notnull
    {
        private readonly Dictionary<T, T> _parent = [];

        public void Add(T x) => _parent.TryAdd(x, x);

        public T Find(T x)
        {
            Add(x);
            var root = x;
            while (!_parent[root].Equals(root))
            {
                root = _parent[root];
            }

            while (!_parent[x].Equals(root))
            {
                var next = _parent[x];
                _parent[x] = root;
                x = next;
            }

            return root;
        }

        public bool Union(T a, T b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra.Equals(rb))
            {
                return false;
            }

            _parent[ra] = rb;
            return true;
        }
    }
}

/// <summary>各元件接腳的中文名稱（與 Component.Nodes 順序一致）。</summary>
public static class PinNames
{
    public static IReadOnlyList<string> For(Component c) => c switch
    {
        VoltageSource or CurrentSource => ["正端", "負端"],
        Diode => ["陽極", "陰極"],
        Vcvs or Vccs => ["輸出正端", "輸出負端", "控制正端", "控制負端"],
        _ => ["接腳1", "接腳2"],
    };
}
