using CircuitBench.Core.Model;

namespace CircuitBench.Core.Mna;

/// <summary>
/// MNA 未知數的排列：先是所有非接地節點電壓，接著是支路電流
/// （獨立電壓源、受控電壓源、電感）。接地節點 0 被消去，索引為 -1。
/// </summary>
public sealed class MnaLayout
{
    private readonly Dictionary<NodeName, int> _nodes = [];
    private readonly Dictionary<string, int> _branches = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _descriptions = [];

    public MnaLayout(Circuit circuit)
    {
        foreach (var n in circuit.Nodes)
        {
            if (!n.IsGround)
            {
                _nodes[n] = _nodes.Count;
                NodeNames.Add(n);
                _descriptions.Add($"節點 {n} 的電壓");
            }
        }

        foreach (var c in circuit.Components)
        {
            if (c is VoltageSource or Vcvs or Inductor)
            {
                _branches[c.Name] = _nodes.Count + _branches.Count;
                BranchNames.Add(c.Name);
                _descriptions.Add($"{c.Name} 的電流");
            }
        }
    }

    public int NodeCount => _nodes.Count;

    public int Size => _nodes.Count + _branches.Count;

    public List<NodeName> NodeNames { get; } = [];

    public List<string> BranchNames { get; } = [];

    public int NodeIndex(NodeName node) => node.IsGround ? -1 : _nodes[node];

    public int BranchIndex(string componentName) => _branches[componentName];

    public bool IsVoltageUnknown(int index) => index < _nodes.Count;

    public string DescribeUnknown(int index) =>
        index >= 0 && index < _descriptions.Count ? _descriptions[index] : $"未知數 #{index}";
}
