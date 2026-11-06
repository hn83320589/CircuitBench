using System.Numerics;

namespace CircuitBench.Core.LinearAlgebra;

/// <summary>矩陣奇異（或數值上接近奇異）。<see cref="Column"/> 為出問題的未知數索引。</summary>
public sealed class SingularMatrixException(int column, string message) : Exception(message)
{
    public int Column { get; } = column;
}

public sealed record LuStatistics(int FullFactorizations, int Refactorizations, int RefactorFallbacks, int FillNonZeros, TimeSpan TotalFactorTime);

/// <summary>
/// 稀疏 LU 分解（左看式 Gilbert–Peierls），支援實數與複數。
/// <list type="bullet">
/// <item>欄排序：在 A+Aᵀ 上做最小度排序，只在第一次分解時計算。</item>
/// <item>主元：門檻式部分主元選擇；若對角元素夠大（≥ threshold·最大值）則優先選對角。</item>
/// <item><see cref="Refactor"/>：沿用上次的主元順序與 L/U 結構，只重做數值；
/// 若主元變得太小則自動退回完整分解。</item>
/// </list>
/// 分解形式：P·A·Q = L·U，L 為單位下三角。
/// </summary>
public sealed class SparseLu<T>
    where T : struct, INumberBase<T>
{
    private readonly SparsityPattern _pattern;
    private readonly double _pivotThreshold;
    private readonly TimeProvider _time;
    private readonly int _n;

    private int[]? _colOrder;           // q[k]：第 k 步處理的原始欄
    private readonly int[] _pivotRow;   // p[k]：第 k 步的主元原始列
    private readonly int[] _pinv;       // pinv[row]：原始列 row 在第幾步成為主元

    // L：每步一欄，列索引為原始列（不含對角的 1）
    private readonly List<int>[] _lRows;
    private readonly List<T>[] _lVals;

    // U：每步一欄，依計算時的拓樸順序保存（不含對角），列索引為步驟編號
    private readonly List<int>[] _uSteps;
    private readonly List<T>[] _uVals;
    private readonly T[] _uDiag;

    private readonly T[] _work;
    private readonly bool[] _mark;
    private bool _factored;
    private int _full;
    private int _refactor;
    private int _fallback;
    private TimeSpan _totalTime;

    public SparseLu(SparsityPattern pattern, double pivotThreshold = 0.1, TimeProvider? time = null)
    {
        if (typeof(T) != typeof(double) && typeof(T) != typeof(Complex))
        {
            throw new NotSupportedException("SparseLu 只支援 double 與 Complex。");
        }

        _pattern = pattern;
        _pivotThreshold = pivotThreshold;
        _time = time ?? TimeProvider.System;
        _n = pattern.Size;
        _pivotRow = new int[_n];
        _pinv = new int[_n];
        _lRows = new List<int>[_n];
        _lVals = new List<T>[_n];
        _uSteps = new List<int>[_n];
        _uVals = new List<T>[_n];
        _uDiag = new T[_n];
        for (var k = 0; k < _n; k++)
        {
            _lRows[k] = [];
            _lVals[k] = [];
            _uSteps[k] = [];
            _uVals[k] = [];
        }

        _work = new T[_n];
        _mark = new bool[_n];
    }

    public SparsityPattern Pattern => _pattern;

    public bool IsFactored => _factored;

    public LuStatistics Statistics => new(_full, _refactor, _fallback, FillNonZeros, _totalTime);

    public int FillNonZeros
    {
        get
        {
            var total = _n;
            for (var k = 0; k < _n; k++)
            {
                total += _lRows[k].Count + _uSteps[k].Count;
            }

            return total;
        }
    }

    /// <summary>
    /// 主元判定的相對門檻：主元 ≤ 門檻 × 尺度 即視為奇異。
    /// 尺度取「該欄原始元素的蓋印量級」與「消去過程中出現的最大值」兩者較大者，
    /// 因此同一元素內的抵消（例如負電阻剛好抵消電導）與累積捨入都能被偵測。
    /// </summary>
    public const double SingularPivotTolerance = 1e-13;

    /// <summary>縮放後條件數下界超過此值即視為接近奇異（解約只剩 3 位有效數字以下）。</summary>
    public const double NearSingularConditionBound = 1e12;

    /// <summary>完整分解：排序（只做一次）、符號分析與數值分解，並重新選擇主元。</summary>
    /// <param name="a">矩陣數值。</param>
    /// <param name="elementScale">
    /// 選用：每個非零元素「抵消前」的量級（組裝時所有蓋印值絕對值的最大者），與 Values 同索引。
    /// </param>
    public void Factor(SparseMatrix<T> a, double[]? elementScale = null)
    {
        EnsureSamePattern(a);
        var start = _time.GetTimestamp();
        _factored = false;
        _colOrder ??= MinimumDegreeOrdering.Compute(_pattern);
        Array.Fill(_pinv, -1);

        var stack = new int[_n];
        var childIndex = new int[_n];
        var visited = new bool[_n];
        var topo = new List<int>(_n);
        var touched = new List<int>(_n);

        for (var k = 0; k < _n; k++)
        {
            var col = _colOrder[k];
            _lRows[k].Clear();
            _lVals[k].Clear();
            _uSteps[k].Clear();
            _uVals[k].Clear();

            // 1. 符號：從 A(:,col) 的非零列出發，沿 L 的圖做 DFS，得到需要的已完成步驟（拓樸順序）。
            topo.Clear();
            var colScale = 0.0;
            for (var p = _pattern.ColumnPointers[col]; p < _pattern.ColumnPointers[col + 1]; p++)
            {
                var row = _pattern.RowIndices[p];
                colScale = Math.Max(colScale, Magnitude(a.Values[p]));
                if (elementScale is not null)
                {
                    colScale = Math.Max(colScale, elementScale[p]);
                }

                _work[row] += a.Values[p];
                Touch(row, touched);
                if (_pinv[row] >= 0 && !visited[_pinv[row]])
                {
                    Dfs(_pinv[row], stack, childIndex, visited, topo);
                }
            }

            // topo 為後序，反轉即為拓樸順序
            for (var t = topo.Count - 1; t >= 0; t--)
            {
                var j = topo[t];
                visited[j] = false;
                var xj = _work[_pivotRow[j]];
                _uSteps[k].Add(j);
                _uVals[k].Add(xj);
                _work[_pivotRow[j]] = T.Zero;
                var rows = _lRows[j];
                var vals = _lVals[j];
                for (var e = 0; e < rows.Count; e++)
                {
                    var r = rows[e];
                    var update = vals[e] * xj;
                    colScale = Math.Max(colScale, Magnitude(update));
                    _work[r] -= update;
                    Touch(r, touched);
                }
            }

            // 2. 選主元：在尚未成為主元的列中
            var best = -1;
            var bestMag = 0.0;
            var candidates = _lRows[k];
            foreach (var r in touched)
            {
                if (_pinv[r] >= 0)
                {
                    _mark[r] = false;
                    continue;
                }

                candidates.Add(r);
                var m = Magnitude(_work[r]);
                if (m > bestMag)
                {
                    bestMag = m;
                    best = r;
                }
            }

            touched.Clear();
            if (best < 0 || bestMag == 0 || bestMag <= SingularPivotTolerance * colScale)
            {
                Array.Clear(_work);
                Array.Clear(_mark);
                candidates.Clear();
                throw new SingularMatrixException(col, $"矩陣奇異：第 {col} 個未知數無法求解（主元為零）。");
            }

            if (_mark[col] && _pinv[col] < 0 && Magnitude(_work[col]) >= _pivotThreshold * bestMag)
            {
                best = col;
            }

            var pivot = _work[best];
            _pivotRow[k] = best;
            _pinv[best] = k;
            _uDiag[k] = pivot;

            // 3. L 欄
            var lRows = new List<int>(candidates.Count);
            _lVals[k].Clear();
            foreach (var r in candidates)
            {
                if (r != best)
                {
                    lRows.Add(r);
                    _lVals[k].Add(_work[r] / pivot);
                }

                _work[r] = T.Zero;
                _mark[r] = false;
            }

            _lRows[k] = lRows;
        }

        _factored = true;
        _full++;
        _totalTime += _time.GetElapsedTime(start);
    }

    /// <summary>
    /// 結構不變時只重做數值分解（沿用主元順序與 L/U 結構）。
    /// 若某個主元相對於該欄的候選值太小，會自動退回 <see cref="Factor"/>。
    /// </summary>
    public void Refactor(SparseMatrix<T> a, double[]? elementScale = null)
    {
        EnsureSamePattern(a);
        if (!_factored)
        {
            Factor(a, elementScale);
            return;
        }

        var start = _time.GetTimestamp();
        for (var k = 0; k < _n; k++)
        {
            var col = _colOrder![k];
            var colScale = 0.0;
            for (var p = _pattern.ColumnPointers[col]; p < _pattern.ColumnPointers[col + 1]; p++)
            {
                _work[_pattern.RowIndices[p]] += a.Values[p];
                colScale = Math.Max(colScale, Magnitude(a.Values[p]));
                if (elementScale is not null)
                {
                    colScale = Math.Max(colScale, elementScale[p]);
                }
            }

            var uSteps = _uSteps[k];
            var uVals = _uVals[k];
            for (var t = 0; t < uSteps.Count; t++)
            {
                var j = uSteps[t];
                var pr = _pivotRow[j];
                var xj = _work[pr];
                _work[pr] = T.Zero;
                uVals[t] = xj;
                var rows = _lRows[j];
                var vals = _lVals[j];
                for (var e = 0; e < rows.Count; e++)
                {
                    var update = vals[e] * xj;
                    colScale = Math.Max(colScale, Magnitude(update));
                    _work[rows[e]] -= update;
                }
            }

            var pivotRow = _pivotRow[k];
            var pivot = _work[pivotRow];
            _work[pivotRow] = T.Zero;
            var pivotMag = Magnitude(pivot);
            var lRows = _lRows[k];
            var maxOther = 0.0;
            for (var e = 0; e < lRows.Count; e++)
            {
                maxOther = Math.Max(maxOther, Magnitude(_work[lRows[e]]));
            }

            // 主元不再適合（相對其他候選太小），或相對尺度已接近奇異：退回完整分解重新選主元；
            // 若真的奇異，Factor 會丟出 SingularMatrixException。
            if (pivotMag == 0 || pivotMag < _pivotThreshold * maxOther || pivotMag <= SingularPivotTolerance * colScale)
            {
                for (var e = 0; e < lRows.Count; e++)
                {
                    _work[lRows[e]] = T.Zero;
                }

                _totalTime += _time.GetElapsedTime(start);
                _fallback++;
                Factor(a, elementScale);
                return;
            }

            _uDiag[k] = pivot;
            var lVals = _lVals[k];
            for (var e = 0; e < lRows.Count; e++)
            {
                lVals[e] = _work[lRows[e]] / pivot;
                _work[lRows[e]] = T.Zero;
            }
        }

        _refactor++;
        _totalTime += _time.GetElapsedTime(start);
    }

    /// <summary>
    /// 列縮放後的條件數下界：以每列「抵消前」的最大元素量級 rᵢ 縮放後，
    /// κ(D·A) ≥ ‖x‖∞ / maxᵢ(|bᵢ| / rᵢ)。解比「每列單獨看」大上許多個數量級，代表矩陣接近奇異。
    /// 列縮放讓「電流灌進只經 Roff 接地的節點」這類量級懸殊但正確的解不會被誤判。
    /// </summary>
    public static double ScaledConditionLowerBound(SparseMatrix<T> a, double[]? elementScale, ReadOnlySpan<T> x, ReadOnlySpan<T> b)
    {
        var n = a.Pattern.Size;
        var rowScale = new double[n];
        for (var c = 0; c < n; c++)
        {
            for (var p = a.Pattern.ColumnPointers[c]; p < a.Pattern.ColumnPointers[c + 1]; p++)
            {
                var r = a.Pattern.RowIndices[p];
                var m = Magnitude(a.Values[p]);
                if (elementScale is not null)
                {
                    m = Math.Max(m, elementScale[p]);
                }

                rowScale[r] = Math.Max(rowScale[r], m);
            }
        }

        var scaledB = 0.0;
        for (var i = 0; i < n; i++)
        {
            if (rowScale[i] > 0)
            {
                scaledB = Math.Max(scaledB, Magnitude(b[i]) / rowScale[i]);
            }
        }

        var xNorm = 0.0;
        foreach (var v in x)
        {
            xNorm = Math.Max(xNorm, Magnitude(v));
        }

        if (xNorm == 0)
        {
            return 0;
        }

        return scaledB == 0 ? double.PositiveInfinity : xNorm / scaledB;
    }

    /// <summary>解 A·x = b，結果寫回 <paramref name="rhs"/>。</summary>
    public void Solve(Span<T> rhs)
    {
        if (!_factored)
        {
            throw new InvalidOperationException("尚未分解。");
        }

        if (rhs.Length != _n)
        {
            throw new ArgumentException("右側向量長度不符。", nameof(rhs));
        }

        // 前代：L z = P b，在原始列索引上就地進行
        var z = new T[_n];
        rhs.CopyTo(_work);
        for (var k = 0; k < _n; k++)
        {
            var zk = _work[_pivotRow[k]];
            z[k] = zk;
            if (zk == T.Zero)
            {
                continue;
            }

            var rows = _lRows[k];
            var vals = _lVals[k];
            for (var e = 0; e < rows.Count; e++)
            {
                _work[rows[e]] -= vals[e] * zk;
            }
        }

        // 回代：U y = z（U 以欄儲存）
        for (var k = _n - 1; k >= 0; k--)
        {
            var yk = z[k] / _uDiag[k];
            z[k] = yk;
            if (yk == T.Zero)
            {
                continue;
            }

            var steps = _uSteps[k];
            var vals = _uVals[k];
            for (var e = 0; e < steps.Count; e++)
            {
                z[steps[e]] -= vals[e] * yk;
            }
        }

        Array.Clear(_work);
        for (var k = 0; k < _n; k++)
        {
            rhs[_colOrder![k]] = z[k];
        }
    }

    private void Dfs(int startStep, int[] stack, int[] childIndex, bool[] visited, List<int> postOrder)
    {
        var top = 0;
        stack[0] = startStep;
        childIndex[0] = 0;
        visited[startStep] = true;
        while (top >= 0)
        {
            var j = stack[top];
            var rows = _lRows[j];
            var advanced = false;
            while (childIndex[top] < rows.Count)
            {
                var r = rows[childIndex[top]++];
                var next = _pinv[r];
                if (next >= 0 && !visited[next])
                {
                    visited[next] = true;
                    top++;
                    stack[top] = next;
                    childIndex[top] = 0;
                    advanced = true;
                    break;
                }
            }

            if (!advanced)
            {
                postOrder.Add(j);
                top--;
            }
        }
    }

    private void Touch(int row, List<int> touched)
    {
        if (!_mark[row])
        {
            _mark[row] = true;
            touched.Add(row);
        }
    }

    private void EnsureSamePattern(SparseMatrix<T> a)
    {
        if (!ReferenceEquals(a.Pattern, _pattern))
        {
            throw new ArgumentException("矩陣結構與分解器不同。", nameof(a));
        }
    }

    private static double Magnitude(T v) =>
        typeof(T) == typeof(double) ? Math.Abs((double)(object)v) : ((Complex)(object)v).Magnitude;
}
