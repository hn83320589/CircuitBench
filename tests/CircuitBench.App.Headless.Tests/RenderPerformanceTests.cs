using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CircuitBench.App.Controls;
using CircuitBench.App.ViewModels;
using CircuitBench.App.Views;
using CircuitBench.Core.Analysis;
using Xunit.Abstractions;

namespace CircuitBench.App.Headless.Tests;

/// <summary>
/// 驗收標準 22：100 萬點波形縮放與平移的「完整重繪」≤ 16 ms。
/// 與 Performance.Tests 只量抽樣不同，這裡用 Skia 實際把 WaveformCanvas 畫進點陣圖
/// （RenderTargetBitmap.Render：量測含 DrawingContext 錄製、幾何建構與 Skia 光柵化），
/// 另外也量整個視窗的 CaptureRenderedFrame 作為上限參考。
/// </summary>
[Trait("Category", "Performance")]
public class RenderPerformanceTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void MillionPointWaveformFullRedrawUnder16Ms()
    {
        const int n = 1_000_000;
        var x = new double[n];
        var a = new double[n];
        var b = new double[n];
        var rng = new Random(1);
        for (var i = 0; i < n; i++)
        {
            x[i] = i * 1e-6 + rng.NextDouble() * 1e-7;
            a[i] = Math.Sin(x[i] * 1000) + (rng.NextDouble() < 1e-5 ? 10 : 0);
            b[i] = 1e-3 * Math.Cos(x[i] * 377);
        }

        var dialogs = new ViewModels.Services.NullDialogs();
        var vm = new MainWindowViewModel(dialogs, dialogs, new Simulator());
        var window = new MainWindow { DataContext = vm, Width = 1920, Height = 1080 };
        window.Show();
        vm.Waveform.Load(new RealSignalSet(x, new Dictionary<SignalId, double[]> { [SignalId.V("a")] = a, [SignalId.I("b")] = b }, "時間 (s)"),
            [SignalId.V("a"), SignalId.I("b")]);
        vm.CurrentPage = Page.Waveform;
        Dispatcher.UIThread.RunJobs();
        var canvas = window.GetVisualDescendants().OfType<WaveformCanvas>().First();
        var size = new PixelSize((int)canvas.Bounds.Width, (int)canvas.Bounds.Height);
        Assert.True(size.Width > 1000, $"畫布寬度 {size.Width}");
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(canvas); // 暖機

        const int redraws = 100;
        double worst = 0, total = 0, worstWindow = 0;
        var samples = new List<(int K, double Ms, int Gen0)>();
        for (var k = 0; k < redraws; k++)
        {
            if (k % 2 == 0)
            {
                vm.Waveform.ZoomX(vm.Waveform.XMin + (vm.Waveform.XMax - vm.Waveform.XMin) * rng.NextDouble(), rng.NextDouble() < 0.5 ? 0.7 : 1.4);
            }
            else
            {
                vm.Waveform.PanX((vm.Waveform.XMax - vm.Waveform.XMin) * (rng.NextDouble() - 0.5) * 0.3);
            }

            if (k % 25 == 0)
            {
                vm.Waveform.AutoScale();
            }

            var gc0 = GC.CollectionCount(0);
            var start = TimeProvider.System.GetTimestamp();
            bitmap.Render(canvas);
            var ms = TimeProvider.System.GetElapsedTime(start).TotalMilliseconds;
            samples.Add((k, ms, GC.CollectionCount(0) - gc0));
            worst = Math.Max(worst, ms);
            total += ms;
        }

        // 整窗擷取每次配置約 8 MB 的點陣圖，會觸發 GC 干擾上面的量測，因此另外量
        for (var k = 0; k < 10; k++)
        {
            vm.Waveform.PanX((vm.Waveform.XMax - vm.Waveform.XMin) * 0.05);
            var ws = TimeProvider.System.GetTimestamp();
            Assert.NotNull(window.CaptureRenderedFrame());
            worstWindow = Math.Max(worstWindow, TimeProvider.System.GetElapsedTime(ws).TotalMilliseconds);
        }

        output.WriteLine($"WaveformCanvas {size.Width}×{size.Height}、100 萬點×2 條曲線，Skia 完整重繪 {redraws} 次：平均 {total / redraws:N2} ms，最差 {worst:N2} ms");
        output.WriteLine($"整個視窗 CaptureRenderedFrame（含版面配置與讀回點陣圖）最差 {worstWindow:N2} ms");
        foreach (var sm in samples.OrderByDescending(v => v.Ms).Take(6))
        {
            output.WriteLine($"  第 {sm.K} 次：{sm.Ms:N2} ms（期間 GC {sm.Gen0} 次）");
        }

        Assert.True(worst <= 16, $"最差完整重繪 {worst:N2} ms");
        window.Close();
    }
}
