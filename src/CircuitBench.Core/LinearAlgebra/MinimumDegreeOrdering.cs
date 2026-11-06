namespace CircuitBench.Core.LinearAlgebra;

/// <summary>
/// 在 A+Aᵀ 的圖上做最小度排序（明確建出消去圖）。每次消去度數最小的節點，
/// 並把它的鄰居連成團。用來降低 LU 分解的填入量。
/// </summary>
internal static class MinimumDegreeOrdering
{
    public static int[] Compute(SparsityPattern pattern)
    {
        var n = pattern.Size;
        var adj = new HashSet<int>[n];
        for (var i = 0; i < n; i++)
        {
            adj[i] = [];
        }

        for (var c = 0; c < n; c++)
        {
            for (var p = pattern.ColumnPointers[c]; p < pattern.ColumnPointers[c + 1]; p++)
            {
                var r = pattern.RowIndices[p];
                if (r != c)
                {
                    adj[r].Add(c);
                    adj[c].Add(r);
                }
            }
        }

        var queue = new SortedSet<(int Degree, int Node)>();
        for (var i = 0; i < n; i++)
        {
            queue.Add((adj[i].Count, i));
        }

        var order = new int[n];
        var eliminated = new bool[n];
        var neighbors = new List<int>();
        for (var k = 0; k < n; k++)
        {
            var (_, v) = queue.Min;
            queue.Remove(queue.Min);
            order[k] = v;
            eliminated[v] = true;

            neighbors.Clear();
            neighbors.AddRange(adj[v]);
            foreach (var u in neighbors)
            {
                queue.Remove((adj[u].Count, u));
                adj[u].Remove(v);
            }

            for (var a = 0; a < neighbors.Count; a++)
            {
                var ua = neighbors[a];
                for (var b = a + 1; b < neighbors.Count; b++)
                {
                    var ub = neighbors[b];
                    if (adj[ua].Add(ub))
                    {
                        adj[ub].Add(ua);
                    }
                }
            }

            foreach (var u in neighbors)
            {
                queue.Add((adj[u].Count, u));
            }

            adj[v].Clear();
        }

        return order;
    }
}
