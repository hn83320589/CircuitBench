using System.Numerics;

namespace CircuitBench.ReferenceSolver;

public enum RefIntegration
{
    BackwardEuler,
    Trapezoidal,
}

/// <summary>
/// 參考求解器：每次都重新組出稠密 MNA 矩陣，以高斯消去（部分主元）求解。
/// 刻意採用最直接、最慢的作法，只求正確且容易檢查。與正式程式碼沒有任何共用。
/// 結果的鍵值：節點電壓 "V(節點大寫)"、支路電流 "I(元件大寫)"（電壓源、受控電壓源、電感）。
/// </summary>
public sealed class RefSolver
{
    private const double Vt = 0.025852;

    private readonly RefCircuit _circuit;
    private readonly double _gmin;
    private readonly Dictionary<string, int> _nodeIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _branchIndex = new(StringComparer.OrdinalIgnoreCase);

    public RefSolver(RefCircuit circuit, double gmin = 1e-12)
    {
        _circuit = circuit;
        _gmin = gmin;
        foreach (var e in circuit.Elements)
        {
            foreach (var n in e.Nodes)
            {
                if (n != "0" && !_nodeIndex.ContainsKey(n))
                {
                    _nodeIndex[n] = _nodeIndex.Count;
                }
            }
        }

        foreach (var e in circuit.Elements)
        {
            if (e.Kind is RefKind.VoltageSource or RefKind.Vcvs or RefKind.Inductor)
            {
                _branchIndex[e.Name] = _nodeIndex.Count + _branchIndex.Count;
            }
        }
    }

    public int Size => _nodeIndex.Count + _branchIndex.Count;

    /// <summary>直流工作點，電源取直流值（RefElement.Value）。</summary>
    public IReadOnlyDictionary<string, double> OperatingPoint() =>
        ToResult(SolveNonlinear(double.NaN, null, new double[Size]));

    public IReadOnlyDictionary<string, Complex> Ac(double frequency)
    {
        var op = SolveNonlinear(double.NaN, null, new double[Size]);
        var n = Size;
        var a = new Complex[n, n];
        var b = new Complex[n];
        var omega = 2 * Math.PI * frequency;
        foreach (var e in _circuit.Elements)
        {
            var p = Node(e.Nodes[0]);
            var q = Node(e.Nodes[1]);
            switch (e.Kind)
            {
                case RefKind.Resistor:
                    StampAdmittance(a, p, q, 1.0 / e.Value);
                    break;
                case RefKind.Switch:
                    StampAdmittance(a, p, q, 1.0 / (e.SwitchOn!(0) ? e.Ron : e.Roff));
                    break;
                case RefKind.Capacitor:
                    StampAdmittance(a, p, q, new Complex(0, omega * e.Value));
                    break;
                case RefKind.Diode:
                {
                    var vd = Voltage(op, p) - Voltage(op, q);
                    var nvt = e.N * Vt;
                    StampAdmittance(a, p, q, e.Is / nvt * Math.Exp(vd / nvt) + _gmin);
                    break;
                }

                case RefKind.Inductor:
                {
                    var k = _branchIndex[e.Name];
                    StampBranchIncidence(a, p, q, k);
                    a[k, k] -= new Complex(0, omega * e.Value);
                    break;
                }

                case RefKind.VoltageSource:
                {
                    var k = _branchIndex[e.Name];
                    StampBranchIncidence(a, p, q, k);
                    b[k] += Complex.FromPolarCoordinates(e.AcMagnitude, e.AcPhaseDegrees * Math.PI / 180);
                    break;
                }

                case RefKind.CurrentSource:
                {
                    var i = Complex.FromPolarCoordinates(e.AcMagnitude, e.AcPhaseDegrees * Math.PI / 180);
                    if (p >= 0) b[p] -= i;
                    if (q >= 0) b[q] += i;
                    break;
                }

                case RefKind.Vcvs:
                {
                    var k = _branchIndex[e.Name];
                    StampBranchIncidence(a, p, q, k);
                    var cp = Node(e.Nodes[2]);
                    var cn = Node(e.Nodes[3]);
                    if (cp >= 0) a[k, cp] -= e.Value;
                    if (cn >= 0) a[k, cn] += e.Value;
                    break;
                }

                case RefKind.Vccs:
                    StampTransconductance(a, p, q, Node(e.Nodes[2]), Node(e.Nodes[3]), e.Value);
                    break;
            }
        }

        var x = DenseSolve(a, b);
        var result = new Dictionary<string, Complex>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, idx) in _nodeIndex) result[$"V({name.ToUpperInvariant()})"] = x[idx];
        foreach (var (name, idx) in _branchIndex) result[$"I({name.ToUpperInvariant()})"] = x[idx];
        return result;
    }

    /// <summary>固定步長暫態，從 t=0 的直流工作點開始。回傳每個時間點的結果（含 t=0）。</summary>
    public IReadOnlyList<(double Time, IReadOnlyDictionary<string, double> Values)> Transient(double stopTime, double step, RefIntegration method)
    {
        var results = new List<(double, IReadOnlyDictionary<string, double>)>();
        var x = SolveNonlinear(0, null, new double[Size]);
        var state = new TransientHistory(_circuit.Elements.Count);
        InitializeHistory(state, x);
        results.Add((0, ToResult(x)));

        var steps = (int)Math.Round(stopTime / step);
        for (var s = 1; s <= steps; s++)
        {
            var t = s * step;
            var ctx = new TransientContext(step, method, state);
            x = SolveNonlinear(t, ctx, x);
            UpdateHistory(state, x, ctx);
            results.Add((t, ToResult(x)));
        }

        return results;
    }

    private double[] SolveNonlinear(double t, TransientContext? tran, double[] initial)
    {
        var hasNonlinear = _circuit.Elements.Any(e => e.Kind == RefKind.Diode);
        var x = (double[])initial.Clone();
        var junction = new double[_circuit.Elements.Count];
        for (var i = 0; i < _circuit.Elements.Count; i++)
        {
            var e = _circuit.Elements[i];
            if (e.Kind == RefKind.Diode)
            {
                junction[i] = Voltage(x, Node(e.Nodes[0])) - Voltage(x, Node(e.Nodes[1]));
            }
        }

        for (var iter = 0; iter < 2000; iter++)
        {
            var (a, b) = AssembleReal(t, tran, junction);
            var xNew = DenseSolve(a, b);
            if (!hasNonlinear)
            {
                return xNew;
            }

            var converged = true;
            for (var i = 0; i < x.Length; i++)
            {
                if (Math.Abs(xNew[i] - x[i]) > 1e-13 + 1e-12 * Math.Abs(xNew[i]))
                {
                    converged = false;
                }
            }

            for (var i = 0; i < _circuit.Elements.Count; i++)
            {
                var e = _circuit.Elements[i];
                if (e.Kind != RefKind.Diode)
                {
                    continue;
                }

                var vNew = Voltage(xNew, Node(e.Nodes[0])) - Voltage(xNew, Node(e.Nodes[1]));
                var limited = LimitJunction(vNew, junction[i], e.Is, e.N * Vt);
                if (Math.Abs(limited - vNew) > 1e-15)
                {
                    converged = false;
                }

                junction[i] = limited;
            }

            x = xNew;
            if (converged && iter > 0)
            {
                return x;
            }
        }

        throw new InvalidOperationException("參考求解器 Newton 未收斂。");
    }

    private static double LimitJunction(double vNew, double vOld, double isat, double nvt)
    {
        var vcrit = nvt * Math.Log(nvt / (Math.Sqrt(2) * isat));
        if (vNew > vcrit && Math.Abs(vNew - vOld) > 2 * nvt)
        {
            if (vOld > 0)
            {
                var arg = 1 + (vNew - vOld) / nvt;
                return arg > 0 ? vOld + nvt * Math.Log(arg) : vcrit;
            }

            return nvt * Math.Log(vNew / nvt);
        }

        return vNew;
    }

    private (double[,] A, double[] B) AssembleReal(double t, TransientContext? tran, double[] junction)
    {
        var n = Size;
        var a = new double[n, n];
        var b = new double[n];
        for (var idx = 0; idx < _circuit.Elements.Count; idx++)
        {
            var e = _circuit.Elements[idx];
            var p = Node(e.Nodes[0]);
            var q = Node(e.Nodes[1]);
            switch (e.Kind)
            {
                case RefKind.Resistor:
                    StampConductance(a, p, q, 1.0 / e.Value);
                    break;
                case RefKind.Switch:
                    StampConductance(a, p, q, 1.0 / (e.SwitchOn!(double.IsNaN(t) ? 0 : t) ? e.Ron : e.Roff));
                    break;
                case RefKind.Capacitor:
                    if (tran is { } tc)
                    {
                        var factor = tc.Method == RefIntegration.Trapezoidal ? 2.0 : 1.0;
                        var g = factor * e.Value / tc.Step;
                        var ieq = g * tc.History.Voltage[idx] + (tc.Method == RefIntegration.Trapezoidal ? tc.History.Current[idx] : 0);
                        StampConductance(a, p, q, g);
                        if (p >= 0) b[p] += ieq;
                        if (q >= 0) b[q] -= ieq;
                    }

                    break;
                case RefKind.Diode:
                {
                    var nvt = e.N * Vt;
                    var vd = junction[idx];
                    var ex = Math.Exp(vd / nvt);
                    var id = e.Is * (ex - 1);
                    var gd = e.Is / nvt * ex;
                    StampConductance(a, p, q, gd + _gmin);
                    var ieq = id - gd * vd;
                    if (p >= 0) b[p] -= ieq;
                    if (q >= 0) b[q] += ieq;
                    break;
                }

                case RefKind.Inductor:
                {
                    var k = _branchIndex[e.Name];
                    StampBranchIncidence(a, p, q, k);
                    if (tran is { } tl)
                    {
                        if (tl.Method == RefIntegration.Trapezoidal)
                        {
                            var r = 2 * e.Value / tl.Step;
                            a[k, k] -= r;
                            b[k] -= r * tl.History.Current[idx] + tl.History.Voltage[idx];
                        }
                        else
                        {
                            var r = e.Value / tl.Step;
                            a[k, k] -= r;
                            b[k] -= r * tl.History.Current[idx];
                        }
                    }

                    break;
                }

                case RefKind.VoltageSource:
                {
                    var k = _branchIndex[e.Name];
                    StampBranchIncidence(a, p, q, k);
                    b[k] += double.IsNaN(t) ? e.Value : e.SourceAt(t);
                    break;
                }

                case RefKind.CurrentSource:
                {
                    var i = double.IsNaN(t) ? e.Value : e.SourceAt(t);
                    if (p >= 0) b[p] -= i;
                    if (q >= 0) b[q] += i;
                    break;
                }

                case RefKind.Vcvs:
                {
                    var k = _branchIndex[e.Name];
                    StampBranchIncidence(a, p, q, k);
                    var cp = Node(e.Nodes[2]);
                    var cn = Node(e.Nodes[3]);
                    if (cp >= 0) a[k, cp] -= e.Value;
                    if (cn >= 0) a[k, cn] += e.Value;
                    break;
                }

                case RefKind.Vccs:
                    StampTransconductance(a, p, q, Node(e.Nodes[2]), Node(e.Nodes[3]), e.Value);
                    break;
            }
        }

        return (a, b);
    }

    private void InitializeHistory(TransientHistory h, double[] x)
    {
        for (var idx = 0; idx < _circuit.Elements.Count; idx++)
        {
            var e = _circuit.Elements[idx];
            var v = Voltage(x, Node(e.Nodes[0])) - Voltage(x, Node(e.Nodes[1]));
            h.Voltage[idx] = v;
            h.Current[idx] = e.Kind == RefKind.Inductor ? x[_branchIndex[e.Name]] : 0;
        }
    }

    private void UpdateHistory(TransientHistory h, double[] x, TransientContext ctx)
    {
        for (var idx = 0; idx < _circuit.Elements.Count; idx++)
        {
            var e = _circuit.Elements[idx];
            var v = Voltage(x, Node(e.Nodes[0])) - Voltage(x, Node(e.Nodes[1]));
            if (e.Kind == RefKind.Capacitor)
            {
                var factor = ctx.Method == RefIntegration.Trapezoidal ? 2.0 : 1.0;
                var g = factor * e.Value / ctx.Step;
                h.Current[idx] = ctx.Method == RefIntegration.Trapezoidal
                    ? g * (v - h.Voltage[idx]) - h.Current[idx]
                    : g * (v - h.Voltage[idx]);
            }
            else if (e.Kind == RefKind.Inductor)
            {
                h.Current[idx] = x[_branchIndex[e.Name]];
            }

            h.Voltage[idx] = v;
        }
    }

    private IReadOnlyDictionary<string, double> ToResult(double[] x)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, idx) in _nodeIndex) result[$"V({name.ToUpperInvariant()})"] = x[idx];
        foreach (var (name, idx) in _branchIndex) result[$"I({name.ToUpperInvariant()})"] = x[idx];
        return result;
    }

    private int Node(string name) => name == "0" ? -1 : _nodeIndex[name];

    private static double Voltage(double[] x, int node) => node < 0 ? 0 : x[node];

    private static void StampConductance(double[,] a, int p, int q, double g)
    {
        if (p >= 0) a[p, p] += g;
        if (q >= 0) a[q, q] += g;
        if (p >= 0 && q >= 0)
        {
            a[p, q] -= g;
            a[q, p] -= g;
        }
    }

    private static void StampAdmittance(Complex[,] a, int p, int q, Complex y)
    {
        if (p >= 0) a[p, p] += y;
        if (q >= 0) a[q, q] += y;
        if (p >= 0 && q >= 0)
        {
            a[p, q] -= y;
            a[q, p] -= y;
        }
    }

    private static void StampBranchIncidence<T>(T[,] a, int p, int q, int k)
        where T : INumberBase<T>
    {
        if (p >= 0)
        {
            a[p, k] += T.One;
            a[k, p] += T.One;
        }

        if (q >= 0)
        {
            a[q, k] -= T.One;
            a[k, q] -= T.One;
        }
    }

    private static void StampTransconductance<T>(T[,] a, int p, int q, int cp, int cn, double gm)
        where T : INumberBase<T>
    {
        var g = T.CreateChecked(gm);
        if (p >= 0 && cp >= 0) a[p, cp] += g;
        if (p >= 0 && cn >= 0) a[p, cn] -= g;
        if (q >= 0 && cp >= 0) a[q, cp] -= g;
        if (q >= 0 && cn >= 0) a[q, cn] += g;
    }

    /// <summary>稠密高斯消去，部分主元選擇。</summary>
    public static double[] DenseSolve(double[,] a, double[] b)
    {
        var n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
            {
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            }

            if (m[pivot, col] == 0)
            {
                throw new InvalidOperationException($"參考求解器：矩陣奇異（第 {col} 欄）。");
            }

            SwapRows(m, x, col, pivot);
            for (var r = col + 1; r < n; r++)
            {
                var f = m[r, col] / m[col, col];
                if (f == 0) continue;
                for (var c = col; c < n; c++) m[r, c] -= f * m[col, c];
                x[r] -= f * x[col];
            }
        }

        for (var r = n - 1; r >= 0; r--)
        {
            var s = x[r];
            for (var c = r + 1; c < n; c++) s -= m[r, c] * x[c];
            x[r] = s / m[r, r];
        }

        return x;
    }

    public static Complex[] DenseSolve(Complex[,] a, Complex[] b)
    {
        var n = b.Length;
        var m = (Complex[,])a.Clone();
        var x = (Complex[])b.Clone();
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
            {
                if (m[r, col].Magnitude > m[pivot, col].Magnitude) pivot = r;
            }

            if (m[pivot, col] == Complex.Zero)
            {
                throw new InvalidOperationException($"參考求解器：矩陣奇異（第 {col} 欄）。");
            }

            SwapRows(m, x, col, pivot);
            for (var r = col + 1; r < n; r++)
            {
                var f = m[r, col] / m[col, col];
                if (f == Complex.Zero) continue;
                for (var c = col; c < n; c++) m[r, c] -= f * m[col, c];
                x[r] -= f * x[col];
            }
        }

        for (var r = n - 1; r >= 0; r--)
        {
            var s = x[r];
            for (var c = r + 1; c < n; c++) s -= m[r, c] * x[c];
            x[r] = s / m[r, r];
        }

        return x;
    }

    private static void SwapRows<T>(T[,] m, T[] x, int r1, int r2)
    {
        if (r1 == r2) return;
        var n = x.Length;
        for (var c = 0; c < n; c++) (m[r1, c], m[r2, c]) = (m[r2, c], m[r1, c]);
        (x[r1], x[r2]) = (x[r2], x[r1]);
    }

    private sealed class TransientHistory(int count)
    {
        public double[] Voltage { get; } = new double[count];

        public double[] Current { get; } = new double[count];
    }

    private sealed record TransientContext(double Step, RefIntegration Method, TransientHistory History);
}
