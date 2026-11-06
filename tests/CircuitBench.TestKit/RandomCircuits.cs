using CircuitBench.Core.Model;

namespace CircuitBench.TestKit;

/// <summary>
/// 隨機線性電路產生器。保證：每個節點都有電阻路徑到接地（生成樹），
/// 電壓源與受控電壓源的輸出不形成迴路，受控源增益適中以避免奇異。
/// </summary>
public static class RandomCircuits
{
    public static Circuit Linear(Random rng, bool includeReactive = false, bool includeAc = false)
    {
        var nodeCount = rng.Next(1, 16);
        string Node(int i) => i == 0 ? "0" : $"n{i}";
        var components = new List<Component>();
        var vForest = new UnionFind(nodeCount + 1);
        var id = 0;

        double RandomR() => Math.Pow(10, rng.NextDouble() * 4); // 1 Ω ~ 10 kΩ
        AcSpec? Ac() => includeAc ? new AcSpec(rng.NextDouble() * 2, rng.NextDouble() * 360 - 180) : null;

        // 生成樹：每個節點以電阻連到某個較小編號的節點
        for (var i = 1; i <= nodeCount; i++)
        {
            components.Add(new Resistor($"R{++id}", Node(i), Node(rng.Next(0, i)), RandomR()));
        }

        var extras = rng.Next(0, nodeCount * 2 + 2);
        for (var e = 0; e < extras; e++)
        {
            var a = rng.Next(0, nodeCount + 1);
            var b = rng.Next(0, nodeCount + 1);
            if (a == b)
            {
                continue;
            }

            var kind = rng.Next(includeReactive ? 8 : 6);
            switch (kind)
            {
                case 0:
                case 1:
                    components.Add(new Resistor($"R{++id}", Node(a), Node(b), RandomR()));
                    break;
                case 2:
                    if (vForest.Union(a, b))
                    {
                        components.Add(new VoltageSource($"V{++id}", Node(a), Node(b),
                            new SourceSpec(rng.NextDouble() * 20 - 10, null, Ac())));
                    }

                    break;
                case 3:
                    components.Add(new CurrentSource($"I{++id}", Node(a), Node(b),
                        new SourceSpec(rng.NextDouble() * 0.02 - 0.01, null, Ac())));
                    break;
                case 4:
                    if (vForest.Union(a, b))
                    {
                        components.Add(new Vcvs($"E{++id}", Node(a), Node(b),
                            Node(rng.Next(0, nodeCount + 1)), Node(rng.Next(0, nodeCount + 1)), rng.NextDouble() - 0.5));
                    }

                    break;
                case 5:
                    components.Add(new Vccs($"G{++id}", Node(a), Node(b),
                        Node(rng.Next(0, nodeCount + 1)), Node(rng.Next(0, nodeCount + 1)), (rng.NextDouble() - 0.5) * 1e-4));
                    break;
                case 6:
                    components.Add(new Capacitor($"C{++id}", Node(a), Node(b), Math.Pow(10, -9 + rng.NextDouble() * 4)));
                    break;
                case 7:
                    if (vForest.Union(a, b))
                    {
                        components.Add(new Inductor($"L{++id}", Node(a), Node(b), Math.Pow(10, -6 + rng.NextDouble() * 4)));
                    }

                    break;
            }
        }

        return Circuit.Create(components);
    }

    private sealed class UnionFind(int n)
    {
        private readonly int[] _parent = Enumerable.Range(0, n).ToArray();

        private int Find(int x) => _parent[x] == x ? x : _parent[x] = Find(_parent[x]);

        public bool Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra == rb)
            {
                return false;
            }

            _parent[ra] = rb;
            return true;
        }
    }
}
