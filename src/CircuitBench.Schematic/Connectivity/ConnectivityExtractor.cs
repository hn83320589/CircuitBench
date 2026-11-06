using System.Collections.Immutable;
using CircuitBench.Core.Model;
using CircuitBench.Core.Validation;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;

namespace CircuitBench.Schematic.Connectivity;

public sealed record PinRef(ElementId Component, string PinName);

/// <summary>連線萃取結果。</summary>
public sealed record ExtractionResult(
    Circuit Circuit,
    IReadOnlyDictionary<PinRef, NodeName> PinNets,
    IReadOnlyDictionary<NodeName, IReadOnlyList<GridPoint>> NetPoints,
    IReadOnlyList<GridPoint> Junctions,
    IReadOnlyList<CircuitIssue> Issues,
    IReadOnlyDictionary<string, ElementId> ComponentIds)
{
    /// <summary>某個網路的名稱（以格點查詢）；不在任何網路上時為 null。</summary>
    public NodeName? NetAt(GridPoint p) =>
        NetPoints.Where(kv => kv.Value.Contains(p)).Select(kv => (NodeName?)kv.Key).FirstOrDefault();
}

/// <summary>
/// 由圖面萃取網表。規則：
/// <list type="bullet">
/// <item>同一格點上的端子（元件接腳、導線端點、標籤、接地）相連。</item>
/// <item>導線兩端相連；端子落在另一條導線中段（不含端點）時與該導線相連，並產生接點。</item>
/// <item>兩條導線只是在內部交叉時不相連。</item>
/// <item>同名網路標籤（不分大小寫）視為同一節點；接地固定為節點 0；其餘網路自動命名為 N$1、N$2…</item>
/// </list>
/// </summary>
public static class ConnectivityExtractor
{
    public const string AutoNetPrefix = "N$";

    public static ExtractionResult Extract(SchematicDocument doc)
    {
        var uf = new PointUnion();
        var wires = doc.Wires.Where(w => w.IsValid).ToList();
        var components = doc.Components.ToList();
        var labels = doc.Elements.OfType<NetLabel>().ToList();
        var grounds = doc.Elements.OfType<GroundSymbol>().ToList();

        // 所有端子點
        var terminalPoints = new List<GridPoint>();
        foreach (var e in doc.Elements)
        {
            terminalPoints.AddRange(e.Terminals);
        }

        foreach (var p in terminalPoints)
        {
            uf.Add(p);
        }

        // 導線兩端相連
        foreach (var w in wires)
        {
            uf.Union(w.A, w.B);
        }

        // 端子落在導線中段 → T 字接點
        var junctions = new HashSet<GridPoint>();
        foreach (var p in terminalPoints.Distinct())
        {
            foreach (var w in wires)
            {
                if (GridMath.IsStrictlyInside(p, w.A, w.B))
                {
                    uf.Union(p, w.A);
                    junctions.Add(p);
                }
            }
        }

        // 同一點有三個以上的導線端點或接腳也畫接點
        var degree = new Dictionary<GridPoint, int>();
        foreach (var w in wires)
        {
            degree[w.A] = degree.GetValueOrDefault(w.A) + 1;
            degree[w.B] = degree.GetValueOrDefault(w.B) + 1;
        }

        foreach (var c in components)
        {
            foreach (var (_, pos) in c.PinPositions)
            {
                degree[pos] = degree.GetValueOrDefault(pos) + 1;
            }
        }

        foreach (var (p, d) in degree)
        {
            if (d >= 3)
            {
                junctions.Add(p);
            }
        }

        // 同名標籤合併
        foreach (var group in labels.GroupBy(l => l.NetName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First().Position;
            foreach (var l in group)
            {
                uf.Union(first, l.Position);
            }
        }

        // 接地全部合併
        foreach (var g in grounds)
        {
            uf.Union(grounds[0].Position, g.Position);
        }

        // 命名網路
        var groundRoot = grounds.Count > 0 ? uf.Find(grounds[0].Position) : (GridPoint?)null;
        var rootNames = new Dictionary<GridPoint, NodeName>();
        foreach (var l in labels.Where(l => l.NetName.Trim() == "0"))
        {
            groundRoot ??= uf.Find(l.Position);
            uf.Union(groundRoot.Value, l.Position);
            groundRoot = uf.Find(l.Position);
        }

        if (groundRoot is { } gr)
        {
            rootNames[uf.Find(gr)] = NodeName.Ground;
        }

        foreach (var l in labels.OrderBy(l => l.NetName, StringComparer.OrdinalIgnoreCase))
        {
            var name = l.NetName.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            rootNames.TryAdd(uf.Find(l.Position), new NodeName(name));
        }

        var autoIndex = 1;
        var usedNames = new HashSet<NodeName>(rootNames.Values);
        NodeName NetOf(GridPoint p)
        {
            var root = uf.Find(p);
            if (!rootNames.TryGetValue(root, out var name))
            {
                do
                {
                    name = new NodeName($"{AutoNetPrefix}{autoIndex++}");
                }
                while (usedNames.Contains(name));

                usedNames.Add(name);
                rootNames[root] = name;
            }

            return name;
        }

        // 元件接腳 → 網路
        var pinNets = new Dictionary<PinRef, NodeName>();
        var pinsPerNet = new Dictionary<NodeName, List<(ComponentSymbol C, string Pin)>>();
        foreach (var c in components)
        {
            foreach (var (pin, pos) in c.PinPositions)
            {
                var net = NetOf(pos);
                pinNets[new PinRef(c.Id, pin)] = net;
                if (!pinsPerNet.TryGetValue(net, out var list))
                {
                    list = [];
                    pinsPerNet[net] = list;
                }

                list.Add((c, pin));
            }
        }

        var netPoints = new Dictionary<NodeName, List<GridPoint>>();
        foreach (var p in terminalPoints.Distinct())
        {
            var net = NetOf(p);
            if (!netPoints.TryGetValue(net, out var list))
            {
                list = [];
                netPoints[net] = list;
            }

            list.Add(p);
        }

        // 問題：未連接的接腳、參數無效、名稱重複
        var issues = new List<CircuitIssue>();
        foreach (var (net, pins) in pinsPerNet)
        {
            if (net.IsGround || pins.Count != 1)
            {
                continue;
            }

            var (c, pin) = pins[0];
            var pinLabel = PinLabel(c.Kind, pin);
            issues.Add(new CircuitIssue(IssueCode.UnconnectedPin,
                $"{ComponentCatalog.Get(c.Kind).DisplayNameZh} {c.Name} 的接腳「{pinLabel}」沒有連接到其他元件。",
                [c.Name], [net], pinLabel));
        }

        var built = new List<Component>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var componentIds = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in components)
        {
            var nodes = c.PinPositions.Select(p => pinNets[new PinRef(c.Id, p.Name)]).ToList();
            if (string.IsNullOrWhiteSpace(c.Name) || !names.Add(c.Name))
            {
                issues.Add(new CircuitIssue(IssueCode.DuplicateName,
                    string.IsNullOrWhiteSpace(c.Name) ? "有元件沒有名稱。" : $"元件名稱「{c.Name}」重複，請改成不同的名稱。",
                    [c.Name], [.. nodes]));
                continue;
            }

            componentIds[c.Name] = c.Id;
            var errors = new List<string>();
            var component = ComponentFactory.TryCreate(c, nodes, errors);
            if (errors.Count > 0 || component is null)
            {
                issues.Add(new CircuitIssue(IssueCode.InvalidParameter,
                    $"{ComponentCatalog.Get(c.Kind).DisplayNameZh} {c.Name} 的參數無效：{string.Join("；", errors)}",
                    [c.Name], [.. nodes]));
                continue;
            }

            built.Add(component);
        }

        return new ExtractionResult(
            Circuit.Create(built),
            pinNets,
            netPoints.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<GridPoint>)kv.Value),
            [.. junctions.OrderBy(p => p.X).ThenBy(p => p.Y)],
            issues,
            componentIds);
    }

    public static string PinLabel(ComponentKind kind, string pin) => (kind, pin) switch
    {
        (ComponentKind.VoltageSource or ComponentKind.CurrentSource, "+") => "正端",
        (ComponentKind.VoltageSource or ComponentKind.CurrentSource, "-") => "負端",
        (ComponentKind.Diode, "A") => "陽極",
        (ComponentKind.Diode, "K") => "陰極",
        (_, "out+") => "輸出正端",
        (_, "out-") => "輸出負端",
        (_, "ctrl+") => "控制正端",
        (_, "ctrl-") => "控制負端",
        _ => $"接腳{pin}",
    };

    private sealed class PointUnion
    {
        private readonly Dictionary<GridPoint, GridPoint> _parent = [];

        public void Add(GridPoint p) => _parent.TryAdd(p, p);

        public GridPoint Find(GridPoint p)
        {
            Add(p);
            var root = p;
            while (_parent[root] != root)
            {
                root = _parent[root];
            }

            while (_parent[p] != root)
            {
                var next = _parent[p];
                _parent[p] = root;
                p = next;
            }

            return root;
        }

        public void Union(GridPoint a, GridPoint b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
            {
                _parent[ra] = rb;
            }
        }
    }
}
