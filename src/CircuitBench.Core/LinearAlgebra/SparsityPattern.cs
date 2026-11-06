namespace CircuitBench.Core.LinearAlgebra;

/// <summary>收集非零元素位置，建好後轉成不可變的 <see cref="SparsityPattern"/>。</summary>
public sealed class PatternBuilder(int size)
{
    private readonly HashSet<(int Row, int Col)> _entries = [];

    public int Size { get; } = size;

    public void Add(int row, int col)
    {
        if ((uint)row >= (uint)Size || (uint)col >= (uint)Size)
        {
            throw new ArgumentOutOfRangeException(nameof(row), $"({row},{col}) 超出矩陣大小 {Size}。");
        }

        _entries.Add((row, col));
    }

    public SparsityPattern Build() => new(Size, _entries);
}

/// <summary>CSC 格式的稀疏結構（每欄的列索引遞增）。結構固定後可重用符號分解。</summary>
public sealed class SparsityPattern
{
    internal SparsityPattern(int size, IEnumerable<(int Row, int Col)> entries)
    {
        Size = size;
        var byCol = new List<int>[size];
        for (var c = 0; c < size; c++)
        {
            byCol[c] = [];
        }

        foreach (var (r, c) in entries)
        {
            byCol[c].Add(r);
        }

        ColumnPointers = new int[size + 1];
        var rows = new List<int>();
        for (var c = 0; c < size; c++)
        {
            byCol[c].Sort();
            ColumnPointers[c] = rows.Count;
            rows.AddRange(byCol[c]);
        }

        ColumnPointers[size] = rows.Count;
        RowIndices = [.. rows];
    }

    public int Size { get; }

    public int NonZeros => RowIndices.Length;

    public int[] ColumnPointers { get; }

    public int[] RowIndices { get; }

    /// <summary>回傳 (row, col) 在 Values 陣列中的位置；不存在時回傳 -1。</summary>
    public int IndexOf(int row, int col)
    {
        var lo = ColumnPointers[col];
        var hi = ColumnPointers[col + 1] - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var r = RowIndices[mid];
            if (r == row)
            {
                return mid;
            }

            if (r < row)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
    }
}

/// <summary>依附在固定結構上的數值；MNA 組裝時以 slot 直接累加。</summary>
public sealed class SparseMatrix<T>
    where T : struct
{
    public SparseMatrix(SparsityPattern pattern)
    {
        Pattern = pattern;
        Values = new T[pattern.NonZeros];
    }

    public SparsityPattern Pattern { get; }

    public T[] Values { get; }

    public void Clear() => Array.Clear(Values);

    public static SparseMatrix<T> FromDense(T[,] dense, Func<T, bool> isNonZero)
    {
        var n = dense.GetLength(0);
        var builder = new PatternBuilder(n);
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                if (isNonZero(dense[r, c]))
                {
                    builder.Add(r, c);
                }
            }
        }

        var m = new SparseMatrix<T>(builder.Build());
        for (var c = 0; c < n; c++)
        {
            for (var p = m.Pattern.ColumnPointers[c]; p < m.Pattern.ColumnPointers[c + 1]; p++)
            {
                m.Values[p] = dense[m.Pattern.RowIndices[p], c];
            }
        }

        return m;
    }
}
