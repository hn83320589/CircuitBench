namespace CircuitBench.Core.Model;

/// <summary>不可變的電路（網表）。元件名稱不分大小寫且唯一。</summary>
public sealed class Circuit
{
    private readonly Dictionary<string, Component> _byName;

    private Circuit(IReadOnlyList<Component> components, Dictionary<string, Component> byName, IReadOnlyList<NodeName> nodes)
    {
        Components = components;
        _byName = byName;
        Nodes = nodes;
    }

    public IReadOnlyList<Component> Components { get; }

    /// <summary>所有出現過的節點（依首次出現順序，包含接地 0）。</summary>
    public IReadOnlyList<NodeName> Nodes { get; }

    public static Circuit Empty { get; } = Create([]);

    public static Circuit Create(IEnumerable<Component> components)
    {
        var list = components.ToList();
        var byName = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        var nodes = new List<NodeName>();
        var seen = new HashSet<NodeName>();
        foreach (var c in list)
        {
            if (string.IsNullOrWhiteSpace(c.Name))
            {
                throw new ArgumentException("元件名稱不可為空白。");
            }

            if (!byName.TryAdd(c.Name, c))
            {
                throw new ArgumentException($"元件名稱「{c.Name}」重複。");
            }

            foreach (var n in c.Nodes)
            {
                if (seen.Add(n))
                {
                    nodes.Add(n);
                }
            }
        }

        return new Circuit(list, byName, nodes);
    }

    public Component? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>以同名元件取代後回傳新電路。</summary>
    public Circuit With(Component replacement)
    {
        if (Find(replacement.Name) is null)
        {
            throw new ArgumentException($"找不到元件「{replacement.Name}」。");
        }

        return Create(Components.Select(c =>
            string.Equals(c.Name, replacement.Name, StringComparison.OrdinalIgnoreCase) ? replacement : c));
    }

    /// <summary>元件集合（不計順序、名稱不分大小寫）完全相同。</summary>
    public bool StructurallyEquals(Circuit other)
    {
        if (Components.Count != other.Components.Count)
        {
            return false;
        }

        foreach (var c in Components)
        {
            var o = other.Find(c.Name);
            if (o is null || !Normalize(c).Equals(Normalize(o with { Name = c.Name })))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>二極體模型名稱只是標籤（匯出時可能被改名以避免衝突），比較時忽略。</summary>
    private static Component Normalize(Component c) =>
        c is Diode d ? d with { Model = d.Model with { ModelName = string.Empty } } : c;
}
