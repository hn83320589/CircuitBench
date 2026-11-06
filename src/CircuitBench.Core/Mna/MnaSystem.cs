using System.Numerics;
using CircuitBench.Core.LinearAlgebra;
using CircuitBench.Core.Mna.Devices;
using CircuitBench.Core.Model;

namespace CircuitBench.Core.Mna;

/// <summary>
/// 一個電路的 MNA 系統：未知數排列、元件、固定的稀疏結構與實數/複數分解器。
/// 結構只建立一次，之後每次求解只重做數值分解。
/// </summary>
public sealed class MnaSystem
{
    private readonly Circuit _circuit;
    private SparseLu<Complex>? _complexLu;
    private SparseMatrix<Complex>? _complexMatrix;

    public MnaSystem(Circuit circuit, TimeProvider? time = null)
    {
        Time = time ?? TimeProvider.System;
        _circuit = circuit;
        Layout = new MnaLayout(circuit);
        Devices = CreateDevices(circuit, Layout);

        var builder = new PatternBuilder(Layout.Size);
        for (var i = 0; i < Layout.NodeCount; i++)
        {
            builder.Add(i, i); // gmin 步進需要每個節點的對角
        }

        foreach (var d in Devices)
        {
            d.Register(builder);
        }

        Pattern = builder.Build();
        foreach (var d in Devices)
        {
            d.Bind(Pattern);
        }

        NodeDiagonalSlots = Enumerable.Range(0, Layout.NodeCount).Select(i => Pattern.IndexOf(i, i)).ToArray();
        Matrix = new SparseMatrix<double>(Pattern);
        Rhs = new double[Layout.Size];
        Lu = new SparseLu<double>(Pattern, time: Time);
        Context = new RealLoadContext(Matrix.Values, Rhs);
        HasNonlinear = Devices.Any(d => d.IsNonlinear);
    }

    public TimeProvider Time { get; }

    public MnaLayout Layout { get; }

    public IReadOnlyList<Device> Devices { get; }

    public SparsityPattern Pattern { get; }

    public SparseMatrix<double> Matrix { get; }

    public double[] Rhs { get; }

    public SparseLu<double> Lu { get; }

    public RealLoadContext Context { get; }

    public bool HasNonlinear { get; }

    internal int[] NodeDiagonalSlots { get; }

    public int Size => Layout.Size;

    public Device? FindDevice(string name) =>
        Devices.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>以 Context 目前的設定組裝並求解一次線性化系統，回傳新的解。</summary>
    public double[] AssembleAndSolve(double[] x, double extraNodeGmin)
    {
        Matrix.Clear();
        Array.Clear(Rhs);
        Array.Clear(Context.Scale);
        Context.Solution = x;
        foreach (var d in Devices)
        {
            d.Load(Context);
        }

        if (extraNodeGmin > 0)
        {
            foreach (var slot in NodeDiagonalSlots)
            {
                Matrix.Values[slot] += extraNodeGmin;
                Context.Scale[slot] = Math.Max(Context.Scale[slot], extraNodeGmin);
            }
        }

        try
        {
            Lu.Refactor(Matrix, Context.Scale);
        }
        catch (SingularMatrixException ex)
        {
            throw Singular(ex.Column, ex);
        }

        var result = (double[])Rhs.Clone();
        Lu.Solve(result);
        CheckNotNearSingular(SparseLu<double>.ScaledConditionLowerBound(Matrix, Context.Scale, result, Rhs), result.Select(Math.Abs).ToArray());
        return result;
    }

    /// <summary>
    /// 解後的第二道防線：列縮放條件數下界過大，代表分解時的捨入誤差蓋過了主元判定，
    /// 解只剩極少的有效位數（典型情況：受控源造成的負電阻幾乎抵消其他電導）。
    /// </summary>
    private void CheckNotNearSingular(double conditionBound, double[] magnitudes)
    {
        if (conditionBound < SparseLu<double>.NearSingularConditionBound)
        {
            return;
        }

        var worst = Array.IndexOf(magnitudes, magnitudes.Max());
        throw Singular(worst, null, nearly: true);
    }

    private SimulationException Singular(int unknown, Exception? inner, bool nearly = false)
    {
        var what = Layout.DescribeUnknown(unknown);
        var related = RelatedComponents(unknown);
        var relatedText = related.Count > 0 ? $"相關元件：{string.Join("、", related)}。" : string.Empty;
        var head = nearly
            ? $"電路矩陣接近奇異，求得的解不可靠：{what}異常大。"
            : $"電路矩陣奇異，無法求解：{what}無法決定。";
        return new SimulationException(
            head + relatedText +
            "可能原因：浮接節點、只由電壓源與電感構成的迴路，或受控源形成的負電阻剛好抵消其他電導（淨電導為零）。", inner);
    }

    /// <summary>與某個未知數相關的元件（節點上的元件，或支路電流所屬的元件）。</summary>
    private List<string> RelatedComponents(int unknown)
    {
        if (unknown < 0 || unknown >= Layout.Size)
        {
            return [];
        }

        if (!Layout.IsVoltageUnknown(unknown))
        {
            return [Layout.BranchNames[unknown - Layout.NodeCount]];
        }

        var node = Layout.NodeNames[unknown];
        return _circuit.Components.Where(c => c.Nodes.Contains(node)).Select(c => c.Name).ToList();
    }

    /// <summary>交流：在工作點 xOp 線性化後解複數 MNA。</summary>
    public Complex[] SolveAc(double[] xOp, double omega, double gmin)
    {
        _complexMatrix ??= new SparseMatrix<Complex>(Pattern);
        _complexLu ??= new SparseLu<Complex>(Pattern, time: Time);
        _complexMatrix.Clear();
        var rhs = new Complex[Size];
        var ctx = new ComplexLoadContext(_complexMatrix.Values, rhs) { Omega = omega, OperatingPoint = xOp, Gmin = gmin };
        foreach (var d in Devices)
        {
            d.LoadAc(ctx);
        }

        try
        {
            _complexLu.Refactor(_complexMatrix);
        }
        catch (SingularMatrixException ex)
        {
            throw new SimulationException($"交流分析矩陣奇異：{Layout.DescribeUnknown(ex.Column)}無法決定（頻率 {omega / (2 * Math.PI):G4} Hz）。", ex);
        }

        var b = (Complex[])rhs.Clone();
        _complexLu.Solve(rhs);
        if (SparseLu<Complex>.ScaledConditionLowerBound(_complexMatrix, null, rhs, b) >= SparseLu<Complex>.NearSingularConditionBound)
        {
            var mags = rhs.Select(v => v.Magnitude).ToArray();
            throw new SimulationException($"交流分析矩陣接近奇異（頻率 {omega / (2 * Math.PI):G4} Hz）：{Layout.DescribeUnknown(Array.IndexOf(mags, mags.Max()))}異常大，結果不可靠。");
        }

        return rhs;
    }

    public LuStatistics ComplexLuStatistics => _complexLu?.Statistics ?? new LuStatistics(0, 0, 0, 0, TimeSpan.Zero);

    private static List<Device> CreateDevices(Circuit circuit, MnaLayout layout)
    {
        int N(NodeName n) => layout.NodeIndex(n);
        var devices = new List<Device>(circuit.Components.Count);
        foreach (var c in circuit.Components)
        {
            Device d = c switch
            {
                Resistor r => new ResistorDevice(r.Name, N(r.A), N(r.B), r.Resistance),
                Capacitor cap => new CapacitorDevice(cap.Name, N(cap.A), N(cap.B), cap.Capacitance),
                Inductor l => new InductorDevice(l.Name, N(l.A), N(l.B), layout.BranchIndex(l.Name), l.Inductance),
                VoltageSource v => new VoltageSourceDevice(v, N(v.Pos), N(v.Neg), layout.BranchIndex(v.Name)),
                CurrentSource i => new CurrentSourceDevice(i, N(i.Pos), N(i.Neg)),
                Diode dio => new DiodeDevice(dio, N(dio.Anode), N(dio.Cathode)),
                Vcvs e => new VcvsDevice(e.Name, N(e.OutPos), N(e.OutNeg), N(e.CtrlPos), N(e.CtrlNeg), layout.BranchIndex(e.Name), e.Gain),
                Vccs g => new VccsDevice(g.Name, N(g.OutPos), N(g.OutNeg), N(g.CtrlPos), N(g.CtrlNeg), g.Transconductance),
                TimedSwitch s => new SwitchDevice(s, N(s.A), N(s.B)),
                _ => throw new NotSupportedException($"不支援的元件類型：{c.GetType().Name}"),
            };
            devices.Add(d);
        }

        return devices;
    }
}

/// <summary>模擬過程中的錯誤，訊息為中文、可直接顯示給使用者。</summary>
public class SimulationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Newton 迭代未收斂。</summary>
public sealed class ConvergenceException(string message, string unknownDescription) : SimulationException(message)
{
    public string UnknownDescription { get; } = unknownDescription;
}
