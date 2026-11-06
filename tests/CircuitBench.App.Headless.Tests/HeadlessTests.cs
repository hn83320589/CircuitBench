using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CircuitBench.App.Controls;
using CircuitBench.App.ViewModels;
using CircuitBench.App.Views;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;
using CircuitBench.Schematic.Persistence;
using CircuitBench.TestKit;

[assembly: AvaloniaTestApplication(typeof(CircuitBench.App.Headless.Tests.TestAppBuilder))]

namespace CircuitBench.App.Headless.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .AfterSetup(b => ((App)b.Instance!).EnableAutoSave = false);
}

/// <summary>驗收標準 23：Avalonia Headless 測試。</summary>
public class HeadlessTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) OpenMainWindow()
    {
        // 對話框改用 NullDialogs（自動確認），避免 Headless 環境開出模態視窗
        var dialogs = new ViewModels.Services.NullDialogs { ConfirmResult = true };
        var vm = new MainWindowViewModel(dialogs, dialogs, new Core.Analysis.Simulator());
        var window = new MainWindow { DataContext = vm, Width = 1360, Height = 860 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static T Find<T>(Window w, string? name = null)
        where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(c => name is null || c.Name == name);

    private static void Click(Window window, Control target, double x, double y)
    {
        var p = target.TranslatePoint(new Point(x, y), window)!.Value;
        window.MouseMove(p);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SaveScreenshot(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CIRCUITBENCH_SCREENSHOTS");
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name));
    }

    [AvaloniaFact]
    public void MainWindowOpensWithChineseTitleAndFourPages()
    {
        var (window, vm) = OpenMainWindow();
        Assert.StartsWith("電路實驗台", window.Title);
        var tabs = Find<TabControl>(window);
        Assert.Equal(4, tabs.ItemCount);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.NotNull(Find<SchematicCanvas>(window).Editor);

        vm.CurrentPage = Page.Diagnostics;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, tabs.SelectedIndex);
        window.Close();
    }

    [AvaloniaFact]
    public void ToolbarAndCanvasClickPlaceComponentAndRunCheck()
    {
        var (window, vm) = OpenMainWindow();
        var canvas = Find<SchematicCanvas>(window, "Canvas");
        var button = Find<Button>(window, "PlaceResistorButton");
        button.Command!.Execute(button.CommandParameter);
        Assert.Equal(ViewModels.Editor.EditorTool.Place, vm.Editor.ActiveTool);

        // 在畫布上點兩下，放兩個電阻
        var vp = vm.Editor.Viewport;
        var (sx, sy) = vp.ToScreen(new WorldPoint(5, 5));
        Click(window, canvas, sx, sy);
        (sx, sy) = vp.ToScreen(new WorldPoint(15, 5));
        Click(window, canvas, sx, sy);
        Assert.Equal(["R1", "R2"], vm.Editor.Document.Components.Select(c => c.Name).ToArray());

        // 檢查電路：沒有接地 → 錯誤並在圖上標示
        Assert.False(vm.Editor.RunCheck());
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(vm.Editor.IssueMarkers);
        Assert.NotNull(window.CaptureRenderedFrame());
        window.Close();
    }

    [AvaloniaFact]
    public async Task RunningTransientSwitchesToWaveformPageAndRendersPlot()
    {
        var (window, vm) = OpenMainWindow();
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 0, parameters: [("dc", "0"), ("ac", "1 0"), ("waveform", "PULSE(0 5 0.2m 1u 1u 1m 2m)")]);
        b.Component(ComponentKind.Resistor, "R1", 2, -2, parameters: ("value", "1k"));
        b.Component(ComponentKind.Capacitor, "C1", 4, 0, Rotation.R90, parameters: ("value", "220n"));
        b.Label(4, -2, "out");
        b.Ground(0, 2);
        b.Ground(4, 2);
        vm.Editor.LoadDocument(b.Doc);
        vm.Editor.Viewport.Set(40, 520, 350);
        vm.Analysis.Settings = vm.Analysis.Settings with { Kind = AnalysisKind.Transient, TranStop = "6m" };
        Dispatcher.UIThread.RunJobs();
        SaveScreenshot(window, "editor.png");

        await vm.Analysis.RunAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Page.Waveform, vm.CurrentPage);
        Assert.Equal(1, Find<TabControl>(window).SelectedIndex);
        vm.Waveform.AddTrace(Core.Analysis.SignalId.V("out"));
        vm.Waveform.AddTrace(Core.Analysis.SignalId.I("C1"));
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var plot = Find<WaveformCanvas>(window, "WaveformPlot");
        Assert.True(plot.Bounds.Width > 100);
        Assert.True(plot.LastRenderMilliseconds >= 0);
        SaveScreenshot(window, "waveform.png");

        vm.Analysis.Settings = vm.Analysis.Settings with { Kind = AnalysisKind.Ac, AcStart = "10", AcStop = "1meg", AcPoints = "50" };
        await vm.Analysis.RunAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, Find<TabControl>(window).SelectedIndex);
        vm.Bode.SelectedSignal = Core.Analysis.SignalId.V("out");
        Dispatcher.UIThread.RunJobs();
        SaveScreenshot(window, "bode.png");

        vm.CurrentPage = Page.Diagnostics;
        Dispatcher.UIThread.RunJobs();
        SaveScreenshot(window, "diagnostics.png");
        window.Close();
    }

    [AvaloniaFact]
    public void KeyboardRotateOnCanvasRotatesSelection()
    {
        var (window, vm) = OpenMainWindow();
        var canvas = Find<SchematicCanvas>(window, "Canvas");
        vm.Editor.SelectTool(ViewModels.Editor.EditorTool.Place, ComponentKind.Diode);
        var (sx, sy) = vm.Editor.Viewport.ToScreen(new WorldPoint(4, 4));
        Click(window, canvas, sx, sy);
        canvas.Focus();
        window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Rotation.R90, vm.Editor.Document.Components.Single().Orientation.Rotation);
        window.Close();
    }

    [AvaloniaFact]
    public void PropertyPanelEditsSelectedComponent()
    {
        var (window, vm) = OpenMainWindow();
        vm.Editor.SelectTool(ViewModels.Editor.EditorTool.Place, ComponentKind.Resistor);
        var canvas = Find<SchematicCanvas>(window, "Canvas");
        var (sx, sy) = vm.Editor.Viewport.ToScreen(new WorldPoint(4, 4));
        Click(window, canvas, sx, sy);
        Assert.Equal("電阻 R1", vm.Properties.Header);
        var valueField = vm.Properties.Fields.Single(f => f.Key == "value");
        valueField.Value = "2.2k";
        Assert.Equal("2.2k", vm.Editor.Document.Components.Single().Parameter("value"));
        var nameField = vm.Properties.Fields.Single(f => f.Key == "name");
        nameField.Value = "X1";
        Assert.NotNull(nameField.Error);
        window.Close();
    }
}
