using System.Numerics;

namespace CircuitBench.Core.Analysis;

/// <summary>數值設定（預設值同需求規格）。</summary>
public sealed record SimulationOptions
{
    public double RelTol { get; init; } = 1e-3;

    public double VnTol { get; init; } = 1e-6;

    public double AbsTol { get; init; } = 1e-12;

    public double Gmin { get; init; } = 1e-12;

    public int MaxNewtonIterations { get; init; } = 100;

    /// <summary>
    /// 暫態 LTE 容許值的放大係數（意義同 SPICE trtol，但預設 1 而非 7，以換取較高精度）。
    /// </summary>
    public double TrTol { get; init; } = 1;

    /// <summary>暫態結果最多時間點數。</summary>
    public int MaxTimePoints { get; init; } = 5_000_000;

    /// <summary>關閉直接 Newton，強制從 gmin 步進開始（測試與診斷用）。</summary>
    public bool SkipDirectNewton { get; init; }

    public static SimulationOptions Default { get; } = new();
}

public enum ConvergenceMethod
{
    Linear,
    Newton,
    GminStepping,
    SourceStepping,
}

public sealed record AnalysisProgress(double Fraction, string StageZh);

public sealed record SolverStatistics(
    int NewtonIterations,
    int AcceptedSteps,
    int RejectedSteps,
    int MatrixSize,
    int NonZeros,
    int FillNonZeros,
    int FullFactorizations,
    int Refactorizations,
    TimeSpan FactorTime,
    TimeSpan TotalTime,
    ConvergenceMethod Method);

public enum SignalKind
{
    Voltage,
    Current,
}

/// <summary>訊號識別：V(節點) 或 I(元件)。</summary>
public readonly record struct SignalId(SignalKind Kind, string Name)
{
    public static SignalId V(string node) => new(SignalKind.Voltage, node);

    public static SignalId I(string component) => new(SignalKind.Current, component);

    public override string ToString() => Kind == SignalKind.Voltage ? $"V({Name})" : $"I({Name})";

    public bool Equals(SignalId other) => Kind == other.Kind && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Name));
}

/// <summary>實數訊號集合（直流掃描與暫態）：X 為掃描值或時間。</summary>
public sealed class RealSignalSet(double[] x, IReadOnlyDictionary<SignalId, double[]> signals, string xLabel)
{
    public double[] X { get; } = x;

    public IReadOnlyDictionary<SignalId, double[]> Signals { get; } = signals;

    public string XLabel { get; } = xLabel;

    public double[] this[SignalId id] => Signals[id];
}

public sealed class ComplexSignalSet(double[] frequency, IReadOnlyDictionary<SignalId, Complex[]> signals)
{
    public double[] Frequency { get; } = frequency;

    public IReadOnlyDictionary<SignalId, Complex[]> Signals { get; } = signals;

    public Complex[] this[SignalId id] => Signals[id];
}

public sealed record OperatingPointResult(IReadOnlyDictionary<SignalId, double> Values, SolverStatistics Statistics)
{
    public double this[SignalId id] => Values[id];
}

public sealed record DcSweepRequest(string SourceName, double Start, double Stop, double Step);

public sealed record DcSweepResult(RealSignalSet Data, SolverStatistics Statistics);

public sealed record TransientRequest(double StopTime, double? MaxStep = null)
{
    /// <summary>只儲存這些訊號（null 代表全部）。</summary>
    public IReadOnlyCollection<SignalId>? SavedSignals { get; init; }

    public double EffectiveMaxStep => MaxStep ?? StopTime / 50;
}

public sealed record TransientResult(RealSignalSet Data, SolverStatistics Statistics);

public enum AcSweepType
{
    Linear,
    Decade,
}

/// <summary>交流掃描。Linear：Points 為總點數；Decade：Points 為每十倍頻點數。</summary>
public sealed record AcRequest(AcSweepType Type, double StartFrequency, double StopFrequency, int Points);

public sealed record AcResult(ComplexSignalSet Data, SolverStatistics Statistics);
