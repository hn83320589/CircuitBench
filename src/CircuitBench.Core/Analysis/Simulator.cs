using CircuitBench.Core.Mna;
using CircuitBench.Core.Mna.Devices;
using CircuitBench.Core.Model;

namespace CircuitBench.Core.Analysis;

public interface ISimulator
{
    Task<OperatingPointResult> OperatingPointAsync(Circuit circuit, SimulationOptions options, IProgress<AnalysisProgress>? progress, CancellationToken cancellation);

    Task<DcSweepResult> DcSweepAsync(Circuit circuit, DcSweepRequest request, SimulationOptions options, IProgress<AnalysisProgress>? progress, CancellationToken cancellation);

    Task<TransientResult> TransientAsync(Circuit circuit, TransientRequest request, SimulationOptions options, IProgress<AnalysisProgress>? progress, CancellationToken cancellation);

    Task<AcResult> AcAsync(Circuit circuit, AcRequest request, SimulationOptions options, IProgress<AnalysisProgress>? progress, CancellationToken cancellation);
}

/// <summary>
/// 分析入口。所有分析在背景執行緒進行；結果在分析完成後才組成不可變物件回傳，
/// 取消時只會丟出 <see cref="OperationCanceledException"/>，不會留下半套結果。
/// </summary>
public sealed class Simulator(TimeProvider? time = null) : ISimulator
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<OperatingPointResult> OperatingPointAsync(Circuit circuit, SimulationOptions options, IProgress<AnalysisProgress>? progress, CancellationToken cancellation) =>
        Task.Run(() => OperatingPoint(circuit, options, progress, cancellation), cancellation);

    public Task<DcSweepResult> DcSweepAsync(Circuit circuit, DcSweepRequest request, SimulationOptions options, IProgress<AnalysisProgress>? progress, CancellationToken cancellation) =>
        Task.Run(() => DcSweep(circuit, request, options, progress, cancellation), cancellation);

    public Task<TransientResult> TransientAsync(Circuit circuit, TransientRequest request, SimulationOptions options, IProgress<AnalysisProgress>? progress, CancellationToken cancellation) =>
        Task.Run(() => Transient(circuit, request, options, progress, cancellation), cancellation);

    public Task<AcResult> AcAsync(Circuit circuit, AcRequest request, SimulationOptions options, IProgress<AnalysisProgress>? progress, CancellationToken cancellation) =>
        Task.Run(() => Ac(circuit, request, options, progress, cancellation), cancellation);

    public OperatingPointResult OperatingPoint(Circuit circuit, SimulationOptions options, IProgress<AnalysisProgress>? progress = null, CancellationToken cancellation = default)
    {
        var start = _time.GetTimestamp();
        var reporter = new MonotonicProgress(progress, "直流工作點");
        reporter.Report(0);
        var system = CreateSystem(circuit, options, LoadMode.Dc);
        var solver = new DcSolver(system, options, cancellation);
        var x = solver.SolveOperatingPoint();
        var values = Snapshot(system, x).ToDictionary(p => p.Key, p => p.Value);
        reporter.Complete();
        return new OperatingPointResult(values, Stats(system, solver.Iterations, 0, 0, solver.LastMethod, start));
    }

    public DcSweepResult DcSweep(Circuit circuit, DcSweepRequest request, SimulationOptions options, IProgress<AnalysisProgress>? progress = null, CancellationToken cancellation = default)
    {
        var start = _time.GetTimestamp();
        if (request.Step == 0 || Math.Sign(request.Stop - request.Start) * Math.Sign(request.Step) < 0)
        {
            throw new SimulationException("直流掃描的步進值必須不為零，且方向需與起訖值一致。");
        }

        var system = CreateSystem(circuit, options, LoadMode.Dc);
        var device = system.FindDevice(request.SourceName);
        Action<double> setValue = device switch
        {
            VoltageSourceDevice v => value => v.DcOverride = value,
            CurrentSourceDevice i => value => i.DcOverride = value,
            _ => throw new SimulationException($"找不到可掃描的獨立電源「{request.SourceName}」。"),
        };

        var count = (int)Math.Floor((request.Stop - request.Start) / request.Step * (1 + 1e-12)) + 1;
        var reporter = new MonotonicProgress(progress, "直流掃描");
        var solver = new DcSolver(system, options, cancellation);
        var xs = new double[count];
        var columns = new Dictionary<SignalId, double[]>();
        double[]? previous = null;
        var method = ConvergenceMethod.Linear;
        for (var i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var value = request.Start + i * request.Step;
            xs[i] = value;
            setValue(value);
            var x = solver.SolveOperatingPoint(previous);
            method = (ConvergenceMethod)Math.Max((int)method, (int)solver.LastMethod);
            previous = x;
            foreach (var (id, v) in Snapshot(system, x))
            {
                if (!columns.TryGetValue(id, out var col))
                {
                    col = new double[count];
                    columns[id] = col;
                }

                col[i] = v;
            }

            reporter.Report((i + 1.0) / count);
        }

        reporter.Complete();
        return new DcSweepResult(new RealSignalSet(xs, columns, request.SourceName), Stats(system, solver.Iterations, 0, 0, method, start));
    }

    public TransientResult Transient(Circuit circuit, TransientRequest request, SimulationOptions options, IProgress<AnalysisProgress>? progress = null, CancellationToken cancellation = default)
    {
        var start = _time.GetTimestamp();
        var system = CreateSystem(circuit, options, LoadMode.Dc);
        var engine = new TransientEngine(system, request, options, new MonotonicProgress(progress, "暫態分析"), cancellation);
        var (data, iterations, accepted, rejected, method) = engine.Run();
        return new TransientResult(data, Stats(system, iterations, accepted, rejected, method, start));
    }

    public AcResult Ac(Circuit circuit, AcRequest request, SimulationOptions options, IProgress<AnalysisProgress>? progress = null, CancellationToken cancellation = default)
    {
        var start = _time.GetTimestamp();
        var frequencies = AcFrequencies(request);
        var system = CreateSystem(circuit, options, LoadMode.Dc);
        var solver = new DcSolver(system, options, cancellation);
        var op = solver.SolveOperatingPoint();
        var reporter = new MonotonicProgress(progress, "交流分析");
        var columns = new Dictionary<SignalId, System.Numerics.Complex[]>();
        for (var i = 0; i < system.Layout.NodeCount; i++)
        {
            columns[SignalId.V(system.Layout.NodeNames[i].Value)] = new System.Numerics.Complex[frequencies.Length];
        }

        foreach (var b in system.Layout.BranchNames)
        {
            columns[SignalId.I(b)] = new System.Numerics.Complex[frequencies.Length];
        }

        for (var f = 0; f < frequencies.Length; f++)
        {
            cancellation.ThrowIfCancellationRequested();
            var x = system.SolveAc(op, 2 * Math.PI * frequencies[f], options.Gmin);
            for (var i = 0; i < system.Layout.NodeCount; i++)
            {
                columns[SignalId.V(system.Layout.NodeNames[i].Value)][f] = x[i];
            }

            foreach (var b in system.Layout.BranchNames)
            {
                columns[SignalId.I(b)][f] = x[system.Layout.BranchIndex(b)];
            }

            reporter.Report((f + 1.0) / frequencies.Length);
        }

        reporter.Complete();
        var lu = system.ComplexLuStatistics;
        var baseStats = Stats(system, solver.Iterations, 0, 0, solver.LastMethod, start);
        return new AcResult(
            new ComplexSignalSet(frequencies, columns),
            baseStats with
            {
                FullFactorizations = baseStats.FullFactorizations + lu.FullFactorizations,
                Refactorizations = baseStats.Refactorizations + lu.Refactorizations,
                FactorTime = baseStats.FactorTime + lu.TotalFactorTime,
            });
    }

    public static double[] AcFrequencies(AcRequest request)
    {
        if (request.StartFrequency <= 0 || request.StopFrequency < request.StartFrequency || request.Points < 1)
        {
            throw new SimulationException("交流分析的頻率範圍必須大於 0 且終止頻率不小於起始頻率，點數至少 1。");
        }

        if (request.Type == AcSweepType.Linear)
        {
            if (request.Points == 1)
            {
                return [request.StartFrequency];
            }

            var step = (request.StopFrequency - request.StartFrequency) / (request.Points - 1);
            return Enumerable.Range(0, request.Points).Select(i => i == request.Points - 1 ? request.StopFrequency : request.StartFrequency + i * step).ToArray();
        }

        var decades = Math.Log10(request.StopFrequency / request.StartFrequency);
        var total = (int)Math.Floor(decades * request.Points + 1e-9) + 1;
        return Enumerable.Range(0, total).Select(i => request.StartFrequency * Math.Pow(10, (double)i / request.Points)).ToArray();
    }

    internal static IEnumerable<KeyValuePair<SignalId, double>> Snapshot(MnaSystem system, double[] x)
    {
        for (var i = 0; i < system.Layout.NodeCount; i++)
        {
            yield return new(SignalId.V(system.Layout.NodeNames[i].Value), x[i]);
        }

        foreach (var d in system.Devices)
        {
            yield return new(SignalId.I(d.Name), d.Current(x, system.Context));
        }
    }

    private MnaSystem CreateSystem(Circuit circuit, SimulationOptions options, LoadMode mode)
    {
        if (circuit.Components.Count == 0)
        {
            throw new SimulationException("電路是空的，沒有可分析的元件。");
        }

        var system = new MnaSystem(circuit, _time);
        system.Context.Mode = mode;
        system.Context.Gmin = options.Gmin;
        system.Context.Time = 0;
        return system;
    }

    private SolverStatistics Stats(MnaSystem system, int iterations, int accepted, int rejected, ConvergenceMethod method, long start)
    {
        var lu = system.Lu.Statistics;
        return new SolverStatistics(
            iterations, accepted, rejected, system.Size, system.Pattern.NonZeros, lu.FillNonZeros,
            lu.FullFactorizations, lu.Refactorizations, lu.TotalFactorTime, _time.GetElapsedTime(start), method);
    }
}
