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
    private SparseLu<Complex>? _complexLu;
    private SparseMatrix<Complex>? _complexMatrix;

    public MnaSystem(Circuit circuit, TimeProvider? time = null)
    {
        Time = time ?? TimeProvider.System;
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
            }
        }

        try
        {
            Lu.Refactor(Matrix);
        }
        catch (SingularMatrixException ex)
        {
            throw new SimulationException($"電路矩陣奇異，無法求解：{Layout.DescribeUnknown(ex.Column)}無法決定。請檢查是否有浮接節點或電壓源迴路。", ex);
        }

        var result = (double[])Rhs.Clone();
        Lu.Solve(result);
        return result;
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

        _complexLu.Solve(rhs);
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
