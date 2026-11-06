using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CircuitBench.App.ViewModels.Editor;
using CircuitBench.App.ViewModels.Results;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.Mna;
using CircuitBench.Core.Units;
using CircuitBench.Schematic.Persistence;

namespace CircuitBench.App.ViewModels.Analysis;

/// <summary>
/// 分析設定與執行。執行前一定先做電路檢查；分析在背景執行，可取消並回報進度；
/// 取消或失敗時不會更新任何結果頁（不留下半套結果）。
/// </summary>
public sealed partial class AnalysisViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private readonly ISimulator _simulator;
    private CancellationTokenSource? _cts;

    public AnalysisViewModel(EditorViewModel editor, ISimulator simulator, WaveformViewModel waveform, BodeViewModel bode, DiagnosticsViewModel diagnostics)
    {
        _editor = editor;
        _simulator = simulator;
        Waveform = waveform;
        Bode = bode;
        Diagnostics = diagnostics;
    }

    public WaveformViewModel Waveform { get; }

    public BodeViewModel Bode { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    [ObservableProperty]
    private AnalysisSettings _settings = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = "就緒";

    /// <summary>直流工作點結果（顯示在診斷頁的結果表）。</summary>
    [ObservableProperty]
    private IReadOnlyList<DiagnosticRow> _operatingPointRows = [];

    /// <summary>分析完成後應切換到的結果頁。</summary>
    public event EventHandler<AnalysisKind>? Completed;

    public event EventHandler<string>? Failed;

    public SimulationOptions BuildOptions() => new()
    {
        RelTol = Settings.RelTol,
        VnTol = Settings.VnTol,
        AbsTol = Settings.AbsTol,
        Gmin = Settings.Gmin,
        MaxNewtonIterations = Settings.MaxNewtonIterations,
    };

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task RunAsync()
    {
        if (!_editor.RunCheck())
        {
            var first = _editor.Issues[0].MessageZh;
            Status = $"電路有 {_editor.Issues.Count} 個問題，請先修正：{first}";
            Failed?.Invoke(this, Status);
            return;
        }

        var circuit = _editor.Extraction.Circuit;
        var options = BuildOptions();
        string? parseError = null;
        double P(string text, string what)
        {
            if (!EngineeringNotation.TryParse(text, out var v, out var e))
            {
                parseError ??= $"{what}：{e.MessageZh}";
            }

            return v;
        }

        _cts = new CancellationTokenSource();
        var progress = new Progress<AnalysisProgress>(p => Progress = p.Fraction);
        IsRunning = true;
        Progress = 0;
        var kind = Settings.Kind;
        try
        {
            switch (kind)
            {
                case AnalysisKind.OperatingPoint:
                {
                    Status = "直流工作點計算中…";
                    var r = await _simulator.OperatingPointAsync(circuit, options, progress, _cts.Token);
                    OperatingPointRows = r.Values.OrderBy(v => v.Key.Kind).ThenBy(v => v.Key.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(v => new DiagnosticRow(v.Key.ToString(), EngineeringNotation.Format(v.Value, v.Key.Kind == SignalKind.Voltage ? "V" : "A", 6)))
                        .ToList();
                    Diagnostics.Load("直流工作點", r.Statistics);
                    break;
                }

                case AnalysisKind.DcSweep:
                {
                    var req = new DcSweepRequest(Settings.DcSource, P(Settings.DcStart, "起始值"), P(Settings.DcStop, "終止值"), P(Settings.DcStep, "步進"));
                    if (parseError is not null) throw new SimulationException(parseError);
                    Status = "直流掃描中…";
                    var r = await _simulator.DcSweepAsync(circuit, req, options, progress, _cts.Token);
                    Waveform.Load(r.Data);
                    Diagnostics.Load("直流掃描", r.Statistics);
                    break;
                }

                case AnalysisKind.Transient:
                {
                    var stop = P(Settings.TranStop, "停止時間");
                    double? maxStep = string.IsNullOrWhiteSpace(Settings.TranMaxStep) ? null : P(Settings.TranMaxStep, "最大步長");
                    if (parseError is not null) throw new SimulationException(parseError);
                    Status = "暫態分析中…";
                    var r = await _simulator.TransientAsync(circuit, new TransientRequest(stop, maxStep), options, progress, _cts.Token);
                    Waveform.Load(r.Data);
                    Diagnostics.Load("暫態分析", r.Statistics);
                    break;
                }

                case AnalysisKind.Ac:
                {
                    var req = new AcRequest(Settings.AcDecade ? AcSweepType.Decade : AcSweepType.Linear,
                        P(Settings.AcStart, "起始頻率"), P(Settings.AcStop, "終止頻率"), (int)P(Settings.AcPoints, "點數"));
                    if (parseError is not null) throw new SimulationException(parseError);
                    Status = "交流分析中…";
                    var r = await _simulator.AcAsync(circuit, req, options, progress, _cts.Token);
                    Bode.Load(r.Data);
                    Diagnostics.Load("交流分析", r.Statistics);
                    break;
                }
            }

            Progress = 1;
            Status = "分析完成";
            Completed?.Invoke(this, kind);
        }
        catch (OperationCanceledException)
        {
            Status = "已取消（未保留任何結果）";
        }
        catch (SimulationException ex)
        {
            Status = $"分析失敗：{ex.Message}";
            Failed?.Invoke(this, ex.Message);
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel() => _cts?.Cancel();
}
