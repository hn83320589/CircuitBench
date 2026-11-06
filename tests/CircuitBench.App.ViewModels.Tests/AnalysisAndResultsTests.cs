using System.Numerics;
using CircuitBench.App.ViewModels.Editor;
using CircuitBench.App.ViewModels.Results;
using CircuitBench.App.ViewModels.Services;
using CircuitBench.Core.Analysis;
using CircuitBench.Schematic.Model;
using CircuitBench.Schematic.Persistence;
using CircuitBench.TestKit;

namespace CircuitBench.App.ViewModels.Tests;

public class AnalysisViewModelTests
{
    /// <summary>RC 低通：V1 在 (0,0)，R1 由 (0,−2) 到 (4,−2)，C1 垂直在 (4,0)，兩個接地。</summary>
    internal static MainWindowViewModel RcCircuit(NullDialogs? dialogs = null)
    {
        dialogs ??= new NullDialogs();
        var vm = new MainWindowViewModel(dialogs, dialogs, new Simulator());
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 0, parameters: [("dc", "1"), ("ac", "1 0"), ("waveform", "PULSE(0 1 0 1u 1u 10m 20m)")]);
        b.Component(ComponentKind.Resistor, "R1", 2, -2, parameters: ("value", "1k"));
        b.Component(ComponentKind.Capacitor, "C1", 4, 0, Schematic.Geometry.Rotation.R90, parameters: ("value", "1u"));
        b.Label(4, -2, "out");
        b.Ground(0, 2);
        b.Ground(4, 2);
        vm.Editor.LoadDocument(b.Doc);
        return vm;
    }

    [Fact]
    public async Task TransientRunFillsWaveformAndDiagnosticsAndNavigates()
    {
        var vm = RcCircuit();
        vm.Analysis.Settings = vm.Analysis.Settings with { Kind = AnalysisKind.Transient, TranStop = "5m" };
        await vm.Analysis.RunAsync();
        Assert.Equal("分析完成", vm.Analysis.Status);
        Assert.Equal(Page.Waveform, vm.CurrentPage);
        Assert.True(vm.Waveform.HasData);
        Assert.Contains(vm.Waveform.AvailableSignals, s => s == SignalId.V("out"));
        Assert.Equal(1, vm.Analysis.Progress);
        Assert.Contains(vm.Diagnostics.Rows, r => r.LabelZh == "接受的時間步" && r.Value != "0");
        Assert.Contains(vm.Diagnostics.Rows, r => r.LabelZh == "矩陣大小");
    }

    [Fact]
    public async Task AcRunFillsBodeAndNavigates()
    {
        var vm = RcCircuit();
        vm.Analysis.Settings = vm.Analysis.Settings with { Kind = AnalysisKind.Ac, AcStart = "1", AcStop = "100k", AcPoints = "10" };
        await vm.Analysis.RunAsync();
        Assert.Equal(Page.Bode, vm.CurrentPage);
        vm.Bode.SelectedSignal = SignalId.V("out");
        Assert.Equal(0, vm.Bode.MagnitudeDb[0], 2);
        Assert.True(vm.Bode.PhaseDegrees[^1] < -80);
        Assert.Contains(vm.Bode.FrequencyTicks, t => t.IsMajor && t.Value == 1000);
    }

    [Fact]
    public async Task OperatingPointShowsTable()
    {
        var vm = RcCircuit();
        vm.Analysis.Settings = vm.Analysis.Settings with { Kind = AnalysisKind.OperatingPoint };
        await vm.Analysis.RunAsync();
        Assert.Contains(vm.Analysis.OperatingPointRows, r => r.LabelZh == "V(out)" && r.Value == "1V");
        Assert.Equal(Page.Diagnostics, vm.CurrentPage);
        Assert.Contains(vm.Diagnostics.Rows, r => r.Value == "線性電路（直接求解）");
    }

    [Fact]
    public async Task DcSweepUsesSourceName()
    {
        var vm = RcCircuit();
        vm.Analysis.Settings = vm.Analysis.Settings with { Kind = AnalysisKind.DcSweep, DcSource = "V1", DcStart = "0", DcStop = "2", DcStep = "0.5" };
        await vm.Analysis.RunAsync();
        Assert.Equal([0, 0.5, 1, 1.5, 2], vm.Waveform.Data!.X);
    }

    [Fact]
    public async Task CircuitErrorsBlockAnalysisAndMarkCanvas()
    {
        var dialogs = new NullDialogs();
        var vm = new MainWindowViewModel(dialogs, dialogs, new Simulator());
        var b = new SchematicBuilder();
        b.Component(ComponentKind.Resistor, "R1", 0, 0);
        vm.Editor.LoadDocument(b.Doc);
        await vm.Analysis.RunAsync();
        Assert.Contains("問題", vm.Analysis.Status);
        Assert.NotEmpty(vm.Editor.IssueMarkers);
        Assert.False(vm.Waveform.HasData);
        Assert.Single(dialogs.Errors);
    }

    [Fact]
    public async Task InvalidSettingTextIsReportedInChinese()
    {
        var vm = RcCircuit();
        vm.Analysis.Settings = vm.Analysis.Settings with { Kind = AnalysisKind.Transient, TranStop = "abc" };
        await vm.Analysis.RunAsync();
        Assert.Contains("停止時間", vm.Analysis.Status);
        Assert.False(vm.Waveform.HasData);
    }

    [Fact]
    public async Task CancellationLeavesPreviousResultsUntouched()
    {
        var vm = RcCircuit();
        vm.Analysis.Settings = vm.Analysis.Settings with { Kind = AnalysisKind.Transient, TranStop = "5m" };
        await vm.Analysis.RunAsync();
        var previous = vm.Waveform.Data;

        vm.Analysis.Settings = vm.Analysis.Settings with { TranStop = "10", TranMaxStep = "1u" };
        var run = vm.Analysis.RunAsync();
        Assert.True(vm.Analysis.IsRunning);
        Assert.True(vm.Analysis.CancelCommand.CanExecute(null));
        vm.Analysis.CancelCommand.Execute(null);
        await run;
        Assert.False(vm.Analysis.IsRunning);
        Assert.Contains("取消", vm.Analysis.Status);
        Assert.Same(previous, vm.Waveform.Data);
    }
}

public class WaveformViewModelTests
{
    private static RealSignalSet Sine(int n = 10001)
    {
        var x = Enumerable.Range(0, n).Select(i => i * 1e-5).ToArray();
        var v = x.Select(t => 2 * Math.Sin(2 * Math.PI * 1000 * t)).ToArray();
        var i = x.Select(t => 1e-3 * Math.Cos(2 * Math.PI * 1000 * t)).ToArray();
        return new RealSignalSet(x, new Dictionary<SignalId, double[]> { [SignalId.V("a")] = v, [SignalId.I("R1")] = i }, "時間 (s)");
    }

    [Fact]
    public void VoltageAndCurrentGetSeparateAxes()
    {
        var vm = new WaveformViewModel();
        vm.Load(Sine());
        vm.AddTrace(SignalId.I("R1"));
        Assert.Equal(2, vm.Traces.Count);
        Assert.Equal(["A", "V"], vm.Axes.Keys.Order().ToArray());
        Assert.InRange(vm.Axes["V"].Max, 2, 2.3);
        Assert.InRange(vm.Axes["A"].Max, 1e-3, 1.2e-3);
        vm.RemoveTrace(SignalId.I("R1"));
        Assert.False(vm.Axes.ContainsKey("A"));
    }

    [Fact]
    public void ZoomPanAndAutoScale()
    {
        var vm = new WaveformViewModel();
        vm.Load(Sine());
        vm.ZoomX(0.05, 0.1);
        Assert.Equal(0.05 - 0.005, vm.XMin, 12);
        Assert.Equal(0.05 + 0.005, vm.XMax, 12);
        vm.PanX(0.001);
        Assert.Equal(0.046, vm.XMin, 12);
        vm.AutoScaleCommand.Execute(null);
        Assert.Equal(0, vm.XMin);
        Assert.Equal(0.1, vm.XMax, 12);
    }

    [Fact]
    public void ColumnsUseMinMaxDecimation()
    {
        var vm = new WaveformViewModel();
        vm.Load(Sine());
        var cols = vm.Columns(vm.Traces[0], 50);
        Assert.Equal(50, cols.Length);
        Assert.Equal(2, cols.Max(c => c.Max), 3);
        Assert.Equal(-2, cols.Min(c => c.Min), 3);
    }

    [Fact]
    public void CursorReadoutAndMeasurements()
    {
        var vm = new WaveformViewModel();
        vm.Load(Sine());
        vm.CursorA = 0.01;
        vm.CursorB = 0.02;
        var r = vm.Readout!;
        Assert.Equal(0.01, r.DeltaT, 12);
        Assert.Equal(100, r.InverseDeltaT!.Value, 9);
        Assert.Equal(0, r.Measurement.Average, 6);
        Assert.Equal(2 / Math.Sqrt(2), r.Measurement.Rms, 1e-3); // 分段線性內插相對正弦的誤差約 (ωh)²/12
        Assert.Equal(4, r.Measurement.PeakToPeak, 3);
        Assert.Equal(1000, r.Measurement.Frequency!.Value, 1);
        Assert.Contains("RMS", vm.ReadoutText);
        Assert.Contains("1/Δt", vm.ReadoutText);
    }

    [Fact]
    public void SvgExportIsValidXml()
    {
        var vm = new WaveformViewModel();
        vm.Load(Sine());
        vm.AddTrace(SignalId.I("R1"));
        var svg = WaveformSvgExporter.Export(vm);
        Assert.NotNull(System.Xml.Linq.XDocument.Parse(svg).Root);
        Assert.Contains("V(a)", svg);
        Assert.Contains("polyline", svg);
    }

    [Fact]
    public void BodeSelectsVoltageAndUnwrapsPhase()
    {
        var vm = new BodeViewModel();
        var f = Enumerable.Range(0, 50).Select(i => Math.Pow(10, i / 10.0)).ToArray();
        var h = f.Select(x => { var s = new Complex(0, x / 100); return 1 / ((s + 1) * (s + 1) * (s + 1)); }).ToArray();
        vm.Load(new ComplexSignalSet(f, new Dictionary<SignalId, Complex[]> { [SignalId.V("o")] = h }));
        Assert.Equal(SignalId.V("o"), vm.SelectedSignal);
        Assert.True(vm.PhaseDegrees.Min() < -180);
        Assert.True(vm.MagnitudeRange.Min <= vm.MagnitudeDb.Min());
    }

    [Fact]
    public void DiagnosticsShowsSteppingMethod()
    {
        var vm = new DiagnosticsViewModel();
        vm.Load("直流工作點", new SolverStatistics(42, 0, 0, 5, 12, 14, 3, 40, TimeSpan.FromMilliseconds(1.5), TimeSpan.FromMilliseconds(3), ConvergenceMethod.GminStepping));
        Assert.Contains(vm.Rows, r => r.Value == "gmin 步進");
        Assert.Contains(vm.Rows, r => r.LabelZh == "Newton 迭代次數" && r.Value == "42");
    }
}

public class AutoSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cb-test-" + Guid.NewGuid().ToString("N"));

    private sealed class TempPaths(string dir) : IAppPaths
    {
        public string DocumentsDirectory => Path.Combine(dir, "docs");

        public string DataDirectory => Path.Combine(dir, "data");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void TimerSavesOnlyWhenDirtyAndCrashIsRecovered()
    {
        var time = new ManualTimeProvider();
        var paths = new TempPaths(_dir);
        var dirty = false;
        var project = ProjectFile.New() with { Title = "自動儲存測試" };
        var service = new AutoSaveService(time, paths);
        service.Start(() => project, () => dirty);
        Assert.True(File.Exists(service.LockPath));

        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(0, service.SaveCount);

        dirty = true;
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(1, service.SaveCount);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 8, 2, 0, TimeSpan.Zero), service.LastSavedAt); // 第二次計時器觸發的時刻
        Assert.True(File.Exists(service.AutoSavePath));

        // 模擬當機：不呼叫 MarkCleanShutdown，直接建立新的服務
        service.Dispose();
        var next = new AutoSaveService(time, paths);
        var recovery = next.CheckForRecovery();
        Assert.NotNull(recovery);
        Assert.Equal("自動儲存測試", recovery!.Project.Title);
    }

    [Fact]
    public void CleanShutdownRemovesRecovery()
    {
        var time = new ManualTimeProvider();
        var paths = new TempPaths(_dir);
        var service = new AutoSaveService(time, paths);
        service.Start(ProjectFile.New, () => true);
        time.Advance(TimeSpan.FromMinutes(2));
        service.MarkCleanShutdown();
        Assert.Null(new AutoSaveService(time, paths).CheckForRecovery());
        Assert.False(File.Exists(service.LockPath));
    }

    [Fact]
    public void CorruptedAutoSaveIsIgnored()
    {
        var paths = new TempPaths(_dir);
        var service = new AutoSaveService(new ManualTimeProvider(), paths);
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(service.LockPath, "x");
        File.WriteAllText(service.AutoSavePath, "{ broken");
        Assert.Null(service.CheckForRecovery());
    }

    [Fact]
    public async Task MainWindowStartupOffersRecoveryAndLoadsIt()
    {
        var time = new ManualTimeProvider();
        var paths = new TempPaths(_dir);
        var first = AnalysisViewModelTests.RcCircuit();
        var crashed = new AutoSaveService(time, paths);
        crashed.Start(first.Snapshot, () => true);
        crashed.SaveNow();
        crashed.Dispose();

        var dialogs = new NullDialogs { ConfirmResult = true };
        var vm = new MainWindowViewModel(dialogs, dialogs, new Simulator(), new AutoSaveService(time, paths));
        await vm.StartupAsync();
        Assert.Equal(3, vm.Editor.Document.Components.Count());
        Assert.True(vm.IsDirty);
        vm.Shutdown();
    }
}

public class MainWindowFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cb-file-" + Guid.NewGuid().ToString("N"));

    public MainWindowFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task SaveThenOpenRestoresDocumentAndClearsDirty()
    {
        var dialogs = new NullDialogs();
        var vm = AnalysisViewModelTests.RcCircuit(dialogs);
        Assert.True(vm.IsDirty);
        var path = Path.Combine(_dir, "rc.cbench");
        dialogs.NextPaths.Enqueue(path);
        await vm.SaveAsync();
        Assert.False(vm.IsDirty);
        Assert.EndsWith("rc.cbench", vm.Title);

        var other = new MainWindowViewModel(dialogs, dialogs, new Simulator());
        Assert.True(await other.OpenPathAsync(path));
        Assert.True(vm.Editor.Document.ContentEquals(other.Editor.Document));
        Assert.False(other.IsDirty);
    }

    [Fact]
    public async Task OpeningCorruptedOrNewerFileShowsMessage()
    {
        var dialogs = new NullDialogs();
        var vm = new MainWindowViewModel(dialogs, dialogs, new Simulator());
        var bad = Path.Combine(_dir, "bad.cbench");
        await File.WriteAllTextAsync(bad, "{\"schemaVersion\": 42}");
        Assert.False(await vm.OpenPathAsync(bad));
        Assert.Contains("較新版本", dialogs.Errors.Single());
    }

    [Fact]
    public async Task NetlistImportAndExport()
    {
        var dialogs = new NullDialogs();
        var vm = new MainWindowViewModel(dialogs, dialogs, new Simulator());
        var cir = Path.Combine(_dir, "div.cir");
        await File.WriteAllTextAsync(cir, "V1 in 0 10\nR1 in out 1k\nR2 out 0 1k\n.op\n");
        dialogs.NextPaths.Enqueue(cir);
        await vm.ImportNetlistAsync();
        Assert.Equal(3, vm.Editor.Document.Components.Count());
        Assert.Equal(AnalysisKind.OperatingPoint, vm.Analysis.Settings.Kind);
        await vm.Analysis.RunAsync();
        Assert.Contains(vm.Analysis.OperatingPointRows, r => r.LabelZh == "V(out)" && r.Value == "5V");

        var exported = Path.Combine(_dir, "out.cir");
        dialogs.NextPaths.Enqueue(exported);
        await vm.ExportNetlistAsync();
        var back = Core.IO.SpiceNetlistReader.Parse(await File.ReadAllTextAsync(exported));
        Assert.True(back.Success);
        Assert.True(back.Deck!.Circuit.StructurallyEquals(vm.Editor.Extraction.Circuit));
    }

    [Fact]
    public async Task NetlistSyntaxErrorsAreShownWithLineNumbers()
    {
        var dialogs = new NullDialogs();
        var vm = new MainWindowViewModel(dialogs, dialogs, new Simulator());
        var cir = Path.Combine(_dir, "bad.cir");
        await File.WriteAllTextAsync(cir, "R1 a 0 1k\nQ1 a b c\n");
        dialogs.NextPaths.Enqueue(cir);
        await vm.ImportNetlistAsync();
        Assert.Contains("第 2 行", dialogs.Errors.Single());
    }

    [Fact]
    public async Task CsvAndSvgExportsWriteFiles()
    {
        var dialogs = new NullDialogs();
        var vm = AnalysisViewModelTests.RcCircuit(dialogs);
        vm.Analysis.Settings = vm.Analysis.Settings with { Kind = AnalysisKind.Transient, TranStop = "2m" };
        await vm.Analysis.RunAsync();
        var csv = Path.Combine(_dir, "w.csv");
        var svg = Path.Combine(_dir, "w.svg");
        var sch = Path.Combine(_dir, "s.svg");
        dialogs.NextPaths.Enqueue(csv);
        await vm.ExportCsvAsync();
        dialogs.NextPaths.Enqueue(svg);
        await vm.ExportWaveformSvgAsync();
        dialogs.NextPaths.Enqueue(sch);
        await vm.ExportSchematicSvgAsync();
        var bytes = await File.ReadAllBytesAsync(csv);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.NotNull(System.Xml.Linq.XDocument.Load(svg).Root);
        Assert.NotNull(System.Xml.Linq.XDocument.Load(sch).Root);
    }
}
