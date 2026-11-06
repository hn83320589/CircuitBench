using System.Numerics;
using CircuitBench.Core.LinearAlgebra;
using CircuitBench.ReferenceSolver;
using CircuitBench.TestKit;
using CircuitBench.TestKit.Property;

namespace CircuitBench.Core.Tests.LinearAlgebra;

public class SparseLuTests
{
    [Fact]
    public void Property_RealRandomSparseMatchesDense()
    {
        PropertyTest.ForAll(200, (rng, _) =>
        {
            var n = rng.Next(1, 80);
            var a = RandomMatrices.SparseReal(rng, n, rng.NextDouble() * 0.2, permuteRows: rng.Next(2) == 0);
            var b = Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
            var reference = RefSolver.DenseSolve(a, b);

            var m = SparseMatrix<double>.FromDense(a, v => v != 0);
            var lu = new SparseLu<double>(m.Pattern);
            lu.Factor(m);
            var x = (double[])b.Clone();
            lu.Solve(x);
            var err = RandomMatrices.RelativeError(x, reference);
            Assert.True(err <= 1e-10, $"n={n} 相對誤差 {err:E3}");
        });
    }

    [Fact]
    public void Property_ComplexRandomSparseMatchesDense()
    {
        PropertyTest.ForAll(200, (rng, _) =>
        {
            var n = rng.Next(1, 60);
            var a = RandomMatrices.SparseComplex(rng, n, rng.NextDouble() * 0.2, permuteRows: rng.Next(2) == 0);
            var b = Enumerable.Range(0, n).Select(_ => new Complex(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5)).ToArray();
            var reference = RefSolver.DenseSolve(a, b);

            var m = SparseMatrix<Complex>.FromDense(a, v => v != Complex.Zero);
            var lu = new SparseLu<Complex>(m.Pattern);
            lu.Factor(m);
            var x = (Complex[])b.Clone();
            lu.Solve(x);
            var err = RandomMatrices.RelativeError(x, reference);
            Assert.True(err <= 1e-10, $"n={n} 相對誤差 {err:E3}");
        });
    }

    [Fact]
    public void Property_RefactorWithSameStructureEqualsFreshFactor()
    {
        PropertyTest.ForAll(100, (rng, _) =>
        {
            var n = rng.Next(2, 60);
            var a1 = RandomMatrices.SparseReal(rng, n, 0.1, permuteRows: true);
            var m = SparseMatrix<double>.FromDense(a1, v => v != 0);
            var lu = new SparseLu<double>(m.Pattern);
            lu.Factor(m);

            // 同結構、新數值（對角維持優勢，主元順序仍可用）
            for (var i = 0; i < m.Values.Length; i++)
            {
                m.Values[i] *= 0.8 + 0.4 * rng.NextDouble();
            }

            lu.Refactor(m);
            var fresh = new SparseLu<double>(m.Pattern);
            fresh.Factor(m);

            var b = Enumerable.Range(0, n).Select(_ => rng.NextDouble()).ToArray();
            var x1 = (double[])b.Clone();
            var x2 = (double[])b.Clone();
            lu.Solve(x1);
            fresh.Solve(x2);
            Assert.True(RandomMatrices.RelativeError(x1, x2) <= 1e-12);
            Assert.Equal(1, lu.Statistics.Refactorizations + lu.Statistics.RefactorFallbacks);
        });
    }

    [Fact]
    public void RefactorReusesSymbolicFactorization()
    {
        var a = new double[,] { { 4, 1, 0 }, { 1, 4, 1 }, { 0, 1, 4 } };
        var m = SparseMatrix<double>.FromDense(a, v => v != 0);
        var lu = new SparseLu<double>(m.Pattern);
        lu.Factor(m);
        for (var i = 0; i < 5; i++)
        {
            for (var j = 0; j < m.Values.Length; j++)
            {
                m.Values[j] *= 1.1;
            }

            lu.Refactor(m);
        }

        Assert.Equal(1, lu.Statistics.FullFactorizations);
        Assert.Equal(5, lu.Statistics.Refactorizations);
        Assert.Equal(0, lu.Statistics.RefactorFallbacks);
    }

    [Fact]
    public void RefactorFallsBackWhenPivotBecomesTiny()
    {
        var a = new double[,] { { 4, 1 }, { 1, 4 } };
        var m = SparseMatrix<double>.FromDense(a, v => v != 0);
        var lu = new SparseLu<double>(m.Pattern);
        lu.Factor(m);

        // 讓原本的主元 (0,0) 變得極小，必須重新選主元
        m.Values[m.Pattern.IndexOf(0, 0)] = 1e-20;
        lu.Refactor(m);
        var x = new double[] { 1, 2 };
        lu.Solve(x);
        var reference = RefSolver.DenseSolve(new double[,] { { 1e-20, 1 }, { 1, 4 } }, [1, 2]);
        Assert.True(RandomMatrices.RelativeError(x, reference) < 1e-12);
        Assert.Equal(1, lu.Statistics.RefactorFallbacks);
    }

    [Fact]
    public void MnaLikeZeroDiagonalRequiresPivoting()
    {
        // 電壓源支路：[[G, 1],[1, 0]]
        var a = new double[,] { { 1e-3, 1 }, { 1, 0 } };
        var m = SparseMatrix<double>.FromDense(a, v => v != 0);
        var lu = new SparseLu<double>(m.Pattern);
        lu.Factor(m);
        var x = new double[] { 0, 5 };
        lu.Solve(x);
        Assert.Equal(5, x[0], 12);
        Assert.Equal(-5e-3, x[1], 12);
    }

    [Fact]
    public void StructurallySingularMatrixIsDetected()
    {
        var a = new double[,] { { 1, 0, 0 }, { 0, 0, 0 }, { 0, 0, 2 } };
        var builder = new PatternBuilder(3);
        builder.Add(0, 0);
        builder.Add(2, 2);
        var m = new SparseMatrix<double>(builder.Build());
        m.Values[0] = a[0, 0];
        m.Values[1] = a[2, 2];
        var lu = new SparseLu<double>(m.Pattern);
        var ex = Assert.Throws<SingularMatrixException>(() => lu.Factor(m));
        Assert.Equal(1, ex.Column);
        Assert.Contains("奇異", ex.Message);
    }

    [Fact]
    public void NumericallySingularMatrixIsDetected()
    {
        var a = new double[,] { { 1, 2, 3 }, { 2, 4, 6 }, { 1, 0, 1 } };
        var m = SparseMatrix<double>.FromDense(a, v => v != 0);
        var lu = new SparseLu<double>(m.Pattern);
        Assert.Throws<SingularMatrixException>(() => lu.Factor(m));
    }

    /// <summary>
    /// 審查者回報：44 個秩虧 1 的隨機矩陣有 22 個被當成正常矩陣。
    /// 現在必須由分解（主元判定）或解後的條件數下界檢查偵測出來。
    /// </summary>
    [Fact]
    public void Property_RankDeficientMatricesAreDetected()
    {
        PropertyTest.ForAll(300, (rng, _) =>
        {
            var n = rng.Next(3, 60);
            var a = RandomMatrices.SparseReal(rng, n, 0.05 + rng.NextDouble() * 0.2, permuteRows: rng.Next(2) == 0);

            // 把某一欄換成另外兩欄的線性組合 → 秩恰好少 1（數值上奇異，結構上不奇異）
            int i = rng.Next(n), j, k;
            do { j = rng.Next(n); } while (j == i);
            do { k = rng.Next(n); } while (k == i || k == j);
            double cj = rng.NextDouble() * 2 - 1, ck = rng.NextDouble() * 2 - 1;
            for (var r = 0; r < n; r++)
            {
                a[r, i] = cj * a[r, j] + ck * a[r, k];
            }

            var m = SparseMatrix<double>.FromDense(a, v => v != 0);
            var lu = new SparseLu<double>(m.Pattern);
            var b = Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
            try
            {
                lu.Factor(m);
            }
            catch (SingularMatrixException)
            {
                return; // 分解時就偵測到
            }

            var x = (double[])b.Clone();
            lu.Solve(x);
            var bound = SparseLu<double>.ScaledConditionLowerBound(m, null, x, b);
            Assert.True(bound >= SparseLu<double>.NearSingularConditionBound,
                $"n={n} 秩虧矩陣未被偵測：條件數下界 {bound:E2}");
        });
    }

    [Fact]
    public void Property_WellConditionedMatricesAreNotFlagged()
    {
        PropertyTest.ForAll(200, (rng, _) =>
        {
            var n = rng.Next(1, 80);
            var a = RandomMatrices.SparseReal(rng, n, rng.NextDouble() * 0.2, permuteRows: rng.Next(2) == 0);
            var m = SparseMatrix<double>.FromDense(a, v => v != 0);
            var lu = new SparseLu<double>(m.Pattern);
            lu.Factor(m);
            var b = Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
            var x = (double[])b.Clone();
            lu.Solve(x);
            Assert.True(SparseLu<double>.ScaledConditionLowerBound(m, null, x, b) < SparseLu<double>.NearSingularConditionBound);
        });
    }

    [Fact]
    public void CancellationInsideOneElementIsDetectedWithElementScale()
    {
        // 同一元素內 1/3000 − 0.000333… 抵消到 ~1e-19：只看殘值的話判定永遠不會成立
        var g = 1.0 / 3000;
        var value = g + -0.000333333333333333;
        var builder = new PatternBuilder(1);
        builder.Add(0, 0);
        var m = new SparseMatrix<double>(builder.Build());
        m.Values[0] = value;
        var scale = new[] { g };
        var lu = new SparseLu<double>(m.Pattern);
        var ex = Assert.Throws<SingularMatrixException>(() => lu.Factor(m, scale));
        Assert.Equal(0, ex.Column);
    }

    [Fact]
    public void ComplexSingularMatrixIsDetected()
    {
        var a = new Complex[,] { { new(1, 1), new(2, 2) }, { new(1, 0), new(2, 0) } };
        var m = SparseMatrix<Complex>.FromDense(a, v => v != Complex.Zero);
        var lu = new SparseLu<Complex>(m.Pattern);
        Assert.Throws<SingularMatrixException>(() => lu.Factor(m));
    }

    [Fact]
    public void GridLaplacianFillStaysModerateWithOrdering()
    {
        const int side = 30;
        var n = side * side;
        var builder = new PatternBuilder(n);
        for (var i = 0; i < side; i++)
        {
            for (var j = 0; j < side; j++)
            {
                var k = i * side + j;
                builder.Add(k, k);
                if (i + 1 < side) { builder.Add(k, k + side); builder.Add(k + side, k); }
                if (j + 1 < side) { builder.Add(k, k + 1); builder.Add(k + 1, k); }
            }
        }

        var m = new SparseMatrix<double>(builder.Build());
        for (var c = 0; c < n; c++)
        {
            for (var p = m.Pattern.ColumnPointers[c]; p < m.Pattern.ColumnPointers[c + 1]; p++)
            {
                m.Values[p] = m.Pattern.RowIndices[p] == c ? 4.01 : -1;
            }
        }

        var lu = new SparseLu<double>(m.Pattern);
        lu.Factor(m);
        // 沒有排序時頻寬填入約 n·side·2 = 54,000；最小度排序應明顯更少
        Assert.True(lu.FillNonZeros < 30_000, $"fill={lu.FillNonZeros}");
    }
}
