using System.Numerics;

namespace CircuitBench.TestKit;

/// <summary>產生「列置換後的對角優勢」稀疏矩陣：條件數良好，但對角可能為零，必須靠主元選擇。</summary>
public static class RandomMatrices
{
    public static double[,] SparseReal(Random rng, int n, double density, bool permuteRows)
    {
        var a = new double[n, n];
        for (var r = 0; r < n; r++)
        {
            var rowSum = 0.0;
            for (var c = 0; c < n; c++)
            {
                if (r != c && rng.NextDouble() < density)
                {
                    a[r, c] = rng.NextDouble() * 2 - 1;
                    rowSum += Math.Abs(a[r, c]);
                }
            }

            a[r, r] = (rowSum + 0.5 + rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
        }

        return permuteRows ? PermuteRows(rng, a) : a;
    }

    public static Complex[,] SparseComplex(Random rng, int n, double density, bool permuteRows)
    {
        var a = new Complex[n, n];
        for (var r = 0; r < n; r++)
        {
            var rowSum = 0.0;
            for (var c = 0; c < n; c++)
            {
                if (r != c && rng.NextDouble() < density)
                {
                    a[r, c] = new Complex(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
                    rowSum += a[r, c].Magnitude;
                }
            }

            var mag = rowSum + 0.5 + rng.NextDouble();
            a[r, r] = Complex.FromPolarCoordinates(mag, rng.NextDouble() * 2 * Math.PI);
        }

        return permuteRows ? PermuteRows(rng, a) : a;
    }

    public static T[,] PermuteRows<T>(Random rng, T[,] a)
    {
        var n = a.GetLength(0);
        var perm = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).ToArray();
        var b = new T[n, n];
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                b[perm[r], c] = a[r, c];
            }
        }

        return b;
    }

    public static double RelativeError(ReadOnlySpan<double> x, ReadOnlySpan<double> reference)
    {
        double num = 0, den = 0;
        for (var i = 0; i < x.Length; i++)
        {
            num += (x[i] - reference[i]) * (x[i] - reference[i]);
            den += reference[i] * reference[i];
        }

        return Math.Sqrt(num) / Math.Max(Math.Sqrt(den), double.Epsilon);
    }

    public static double RelativeError(ReadOnlySpan<Complex> x, ReadOnlySpan<Complex> reference)
    {
        double num = 0, den = 0;
        for (var i = 0; i < x.Length; i++)
        {
            num += Math.Pow((x[i] - reference[i]).Magnitude, 2);
            den += Math.Pow(reference[i].Magnitude, 2);
        }

        return Math.Sqrt(num) / Math.Max(Math.Sqrt(den), double.Epsilon);
    }
}
