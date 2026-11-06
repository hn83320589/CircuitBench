using CommunityToolkit.Mvvm.ComponentModel;
using CircuitBench.Schematic.Persistence;

namespace CircuitBench.App.ViewModels.Analysis;

/// <summary>分析設定表單：把不可變的 <see cref="AnalysisSettings"/> 拆成可繫結的欄位。</summary>
public sealed class AnalysisSettingsViewModel : ObservableObject
{
    private readonly AnalysisViewModel _analysis;

    public AnalysisSettingsViewModel(AnalysisViewModel analysis)
    {
        _analysis = analysis;
        _analysis.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AnalysisViewModel.Settings))
            {
                OnPropertyChanged(string.Empty); // 全部欄位重新讀取
            }
        };
    }

    public static IReadOnlyList<string> KindNames { get; } = ["直流工作點 (.op)", "直流掃描 (.dc)", "暫態分析 (.tran)", "交流分析 (.ac)"];

    private AnalysisSettings S => _analysis.Settings;

    private void Set(Func<AnalysisSettings, AnalysisSettings> f, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        _analysis.Settings = f(S);
        OnPropertyChanged(name);
    }

    public int KindIndex
    {
        get => (int)S.Kind;
        set => Set(s => s with { Kind = (AnalysisKind)Math.Clamp(value, 0, 3) });
    }

    public string DcSource { get => S.DcSource; set => Set(s => s with { DcSource = value }); }

    public string DcStart { get => S.DcStart; set => Set(s => s with { DcStart = value }); }

    public string DcStop { get => S.DcStop; set => Set(s => s with { DcStop = value }); }

    public string DcStep { get => S.DcStep; set => Set(s => s with { DcStep = value }); }

    public string TranStop { get => S.TranStop; set => Set(s => s with { TranStop = value }); }

    public string TranMaxStep { get => S.TranMaxStep; set => Set(s => s with { TranMaxStep = value }); }

    public bool AcDecade { get => S.AcDecade; set => Set(s => s with { AcDecade = value }); }

    public string AcStart { get => S.AcStart; set => Set(s => s with { AcStart = value }); }

    public string AcStop { get => S.AcStop; set => Set(s => s with { AcStop = value }); }

    public string AcPoints { get => S.AcPoints; set => Set(s => s with { AcPoints = value }); }

    public double RelTol { get => S.RelTol; set => Set(s => s with { RelTol = value }); }

    public double Gmin { get => S.Gmin; set => Set(s => s with { Gmin = value }); }
}

/// <summary>波形頁的訊號勾選項目。</summary>
public sealed class SignalToggle(Core.Analysis.SignalId id, bool shown, Action<Core.Analysis.SignalId, bool> changed) : ObservableObject
{
    private bool _shown = shown;

    public Core.Analysis.SignalId Id { get; } = id;

    public string Label => Id.ToString();

    /// <summary>由程式同步勾選狀態（不觸發新增/移除曲線）。</summary>
    public void SetShownSilently(bool shown)
    {
        if (_shown != shown)
        {
            _shown = shown;
            OnPropertyChanged(nameof(IsShown));
        }
    }

    public bool IsShown
    {
        get => _shown;
        set
        {
            if (SetProperty(ref _shown, value))
            {
                changed(Id, value);
            }
        }
    }
}
