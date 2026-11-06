using CircuitBench.Core.Analysis;
using CircuitBench.Core.Model;

namespace CircuitBench.Core.IO;

/// <summary>網表中的分析指令。</summary>
public abstract record AnalysisCommand;

public sealed record OpCommand : AnalysisCommand;

public sealed record DcCommand(string SourceName, double Start, double Stop, double Step) : AnalysisCommand
{
    public DcSweepRequest ToRequest() => new(SourceName, Start, Stop, Step);
}

/// <summary>.tran TSTEP TSTOP [TSTART [TMAX]]：TSTEP 只保留供輸出，最大步長取 TMAX。</summary>
public sealed record TranCommand(double Step, double Stop, double Start = 0, double? MaxStep = null) : AnalysisCommand
{
    public TransientRequest ToRequest() => new(Stop, MaxStep);
}

public sealed record AcCommand(AcSweepType Type, int Points, double Start, double Stop) : AnalysisCommand
{
    public AcRequest ToRequest() => new(Type, Start, Stop, Points);
}

public sealed record SpiceDeck(Circuit Circuit, IReadOnlyList<AnalysisCommand> Analyses, string? Title = null);

/// <summary>語法錯誤：行號（從 1 起算，接續行回報起始行）與中文原因。</summary>
public sealed record SpiceLineError(int LineNumber, string MessageZh)
{
    public override string ToString() => $"第 {LineNumber} 行：{MessageZh}";
}

public sealed record SpiceParseResult(SpiceDeck? Deck, IReadOnlyList<SpiceLineError> Errors)
{
    public bool Success => Deck is not null && Errors.Count == 0;
}
