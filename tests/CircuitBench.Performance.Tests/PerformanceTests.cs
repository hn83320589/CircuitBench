using CircuitBench.App.ViewModels.Results;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.Model;
using Xunit.Abstractions;

namespace CircuitBench.Performance.Tests;

/// <summary>
/// 驗收標準 22 的效能測試。以 [Trait("Category","Performance")] 標記：
/// CI 以獨立步驟執行（不擋綠燈），正式數據在本機以 Release 組態量測後寫進 README。
/// </summary>
[Trait("Category", "Performance")]
public class PerformanceTests(ITestOutputHelper output)
{
    private readonly Simulator _sim = new();

    private TimeSpan Measure(string name, Action action)
    {
        action(); // 暖機（JIT）
        var start = TimeProvider.System.GetTimestamp();
        action();
        var elapsed = TimeProvider.System.GetElapsedTime(start);
        output.WriteLine($"{name}: {elapsed.TotalMilliseconds:N1} ms");
        return elapsed;
    }

    public static Circuit RcLadder(int nodes)
    {
        var parts = new List<Component>
        {
            new VoltageSource("V1", "n0", "0", new SourceSpec(null, new PulseWaveform(0, 1, 0, 1e-6, 1e-6, 5e-4, 1e-3), new AcSpec(1, 0))),
        };
        for (var i = 1; i < nodes; i++)
        {
            parts.Add(new Resistor($"R{i}", $"n{i - 1}", $"n{i}", 10));
            parts.Add(new Capacitor($"C{i}", $"n{i}", "0", 1e-9));
        }

        return Circuit.Create(parts);
    }

    [Fact]
    public void RcLadder2000Nodes10000StepsUnder10Seconds()
    {
        var circuit = RcLadder(2000);
        const double stop = 1e-3;
        var request = new TransientRequest(stop, stop / 10_000) { SavedSignals = [SignalId.V("n1999"), SignalId.V("n1")] };
        TransientResult? result = null;
        var elapsed = Measure("RC 梯形 2000 節點暫態", () => result = _sim.Transient(circuit, request, SimulationOptions.Default));
        output.WriteLine($"  時間點 {result!.Data.X.Length:N0}，接受 {result.Statistics.AcceptedSteps:N0}、拒絕 {result.Statistics.RejectedSteps:N0}，矩陣 {result.Statistics.MatrixSize}，重用分解 {result.Statistics.Refactorizations:N0} 次");
        Assert.True(result.Statistics.AcceptedSteps >= 10_000, $"只有 {result.Statistics.AcceptedSteps} 步");
        Assert.True(elapsed.TotalSeconds <= 10, $"耗時 {elapsed.TotalSeconds:N2} s");
    }

    [Fact]
    public void ResistorGrid100x100OperatingPointUnder1Second()
    {
        const int side = 100;
        string N(int r, int c) => r == 0 && c == 0 ? "0" : $"g{r}_{c}";
        var parts = new List<Component> { VoltageSource.Dc("V1", N(side - 1, side - 1), "0", 1) };
        for (var r = 0; r < side; r++)
        {
            for (var c = 0; c < side; c++)
            {
                if (c + 1 < side) parts.Add(new Resistor($"Rh{r}_{c}", N(r, c), N(r, c + 1), 1));
                if (r + 1 < side) parts.Add(new Resistor($"Rv{r}_{c}", N(r, c), N(r + 1, c), 1));
            }
        }

        var circuit = Circuit.Create(parts);
        OperatingPointResult? op = null;
        var elapsed = Measure("100×100 電阻網格直流工作點", () => op = _sim.OperatingPoint(circuit, SimulationOptions.Default));
        output.WriteLine($"  未知數 {op!.Statistics.MatrixSize:N0}，非零 {op.Statistics.NonZeros:N0}，填入後 {op.Statistics.FillNonZeros:N0}");
        Assert.Equal(1.0, op[SignalId.V(N(side - 1, side - 1))], 9);
        Assert.True(elapsed.TotalSeconds <= 1, $"耗時 {elapsed.TotalSeconds:N3} s");
    }

    [Fact]
    public void Ac2000Nodes500FrequenciesUnder3Seconds()
    {
        var circuit = RcLadder(2000);
        AcResult? result = null;
        var elapsed = Measure("2000 節點交流分析 500 點", () => result = _sim.Ac(circuit, new AcRequest(AcSweepType.Linear, 1e3, 1e8, 500), SimulationOptions.Default));
        Assert.Equal(500, result!.Data.Frequency.Length);
        Assert.True(elapsed.TotalSeconds <= 3, $"耗時 {elapsed.TotalSeconds:N2} s");
    }

    [Fact]
    public void WaveformMillionPointsZoomAndPanUnder16MsPerRedraw()
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
            b[i] = Math.Cos(x[i] * 377);
        }

        var data = new RealSignalSet(x, new Dictionary<SignalId, double[]> { [SignalId.V("a")] = a, [SignalId.V("b")] = b }, "時間 (s)");
        var vm = new WaveformViewModel();
        vm.Load(data, [SignalId.V("a"), SignalId.V("b")]);
        const int width = 1920;
        const int redraws = 200;

        // 一次「重繪」= 每條曲線依目前範圍抽樣成每像素欄的 min/max（繪圖控制項只畫這些點）
        void Redraw()
        {
            foreach (var t in vm.Traces)
            {
                _ = vm.Columns(t, width);
            }

            _ = vm.XTicks();
        }

        Redraw();
        var worst = 0.0;
        var total = 0.0;
        for (var k = 0; k < redraws; k++)
        {
            if (k % 2 == 0)
            {
                vm.ZoomX(vm.XMin + (vm.XMax - vm.XMin) * rng.NextDouble(), rng.NextDouble() < 0.5 ? 0.7 : 1.4);
            }
            else
            {
                vm.PanX((vm.XMax - vm.XMin) * (rng.NextDouble() - 0.5) * 0.3);
            }

            if (k % 50 == 0)
            {
                vm.AutoScale();
            }

            var start = TimeProvider.System.GetTimestamp();
            Redraw();
            var ms = TimeProvider.System.GetElapsedTime(start).TotalMilliseconds;
            worst = Math.Max(worst, ms);
            total += ms;
        }

        output.WriteLine($"100 萬點×2 條曲線、寬 {width} px：平均 {total / redraws:N3} ms，最差 {worst:N3} ms");
        Assert.True(worst <= 16, $"最差重繪 {worst:N2} ms");
    }
}
