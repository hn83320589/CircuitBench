using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CircuitBench.Core.Analysis;

namespace CircuitBench.App.ViewModels.Results;

public sealed record DiagnosticRow(string LabelZh, string Value);

/// <summary>診斷頁：Newton 迭代次數、接受/拒絕步數、矩陣大小與非零元素、分解耗時與收斂方法。</summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    public ObservableCollection<DiagnosticRow> Rows { get; } = [];

    [ObservableProperty]
    private string _analysisName = "（尚未執行分析）";

    public SolverStatistics? Statistics { get; private set; }

    public static string MethodNameZh(ConvergenceMethod m) => m switch
    {
        ConvergenceMethod.Linear => "線性電路（直接求解）",
        ConvergenceMethod.Newton => "Newton-Raphson",
        ConvergenceMethod.GminStepping => "gmin 步進",
        _ => "電源步進",
    };

    public void Load(string analysisNameZh, SolverStatistics stats)
    {
        AnalysisName = analysisNameZh;
        Statistics = stats;
        Rows.Clear();
        Rows.Add(new("工作點收斂方法", MethodNameZh(stats.Method)));
        Rows.Add(new("Newton 迭代次數", stats.NewtonIterations.ToString("N0")));
        Rows.Add(new("接受的時間步", stats.AcceptedSteps.ToString("N0")));
        Rows.Add(new("拒絕的時間步", stats.RejectedSteps.ToString("N0")));
        Rows.Add(new("矩陣大小", $"{stats.MatrixSize:N0} × {stats.MatrixSize:N0}"));
        Rows.Add(new("非零元素數", stats.NonZeros.ToString("N0")));
        Rows.Add(new("LU 填入後非零數", stats.FillNonZeros.ToString("N0")));
        Rows.Add(new("完整分解次數（含符號分析）", stats.FullFactorizations.ToString("N0")));
        Rows.Add(new("只做數值分解的次數（重用符號分解）", stats.Refactorizations.ToString("N0")));
        Rows.Add(new("分解總耗時", $"{stats.FactorTime.TotalMilliseconds:N2} ms"));
        Rows.Add(new("分析總耗時", $"{stats.TotalTime.TotalMilliseconds:N1} ms"));
    }
}
