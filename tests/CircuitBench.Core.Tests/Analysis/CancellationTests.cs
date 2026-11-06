using CircuitBench.Core.Analysis;
using CircuitBench.Core.Model;
using CircuitBench.TestKit.Property;

namespace CircuitBench.Core.Tests.Analysis;

/// <summary>驗收標準 15：任何時間點取消都不留下半套結果；進度單調遞增。</summary>
public class CancellationTests
{
    private readonly Simulator _sim = new();

    private static Circuit RcLadder(int n)
    {
        var parts = new List<Component>
        {
            new VoltageSource("V1", "n0", "0", new SourceSpec(null, new PulseWaveform(0, 1, 0, 1e-6, 1e-6, 1e-3, 2e-3), null)),
        };
        for (var i = 1; i <= n; i++)
        {
            parts.Add(new Resistor($"R{i}", $"n{i - 1}", $"n{i}", 100));
            parts.Add(new Capacitor($"C{i}", $"n{i}", "0", 1e-8));
        }

        return Circuit.Create(parts);
    }

    private static Circuit Divider() => Circuit.Create([
        new VoltageSource("V1", "a", "0", new SourceSpec(1, new DcWaveform(1), new AcSpec(1, 0))),
        new Resistor("R1", "a", "b", 1),
        new Capacitor("C1", "b", "0", 1e-6),
    ]);

    public static TheoryData<string> Analyses => ["tran", "dc", "ac"];

    private Task Run(string kind, Circuit c, IProgress<AnalysisProgress> p, CancellationToken ct) => kind switch
    {
        "tran" => _sim.TransientAsync(c, new TransientRequest(5e-3, 1e-6), SimulationOptions.Default, p, ct),
        "dc" => _sim.DcSweepAsync(c, new DcSweepRequest("V1", 0, 10, 0.0005), SimulationOptions.Default, p, ct),
        _ => _sim.AcAsync(c, new AcRequest(AcSweepType.Decade, 1, 1e9, 2000), SimulationOptions.Default, p, ct),
    };

    [Theory]
    [MemberData(nameof(Analyses))]
    public async Task ProgressIsMonotonicAndEndsAtOne(string kind)
    {
        var fractions = new List<double>();
        var progress = new SynchronousProgress(p => fractions.Add(p.Fraction));
        await Run(kind, kind == "tran" ? RcLadder(5) : Divider(), progress, CancellationToken.None);
        Assert.True(fractions.Count >= 2);
        for (var i = 1; i < fractions.Count; i++)
        {
            Assert.True(fractions[i] > fractions[i - 1], $"進度在 {i} 下降：{fractions[i - 1]} → {fractions[i]}");
        }

        Assert.Equal(1, fractions[^1]);
    }

    [Fact]
    public void Property_CancelAtRandomProgressNeverReturnsResult()
    {
        PropertyTest.ForAll(12, (rng, _) =>
        {
            var kind = new[] { "tran", "dc", "ac" }[rng.Next(3)];
            var cancelAt = rng.NextDouble() * 0.95;
            using var cts = new CancellationTokenSource();
            var progress = new SynchronousProgress(p =>
            {
                if (p.Fraction >= cancelAt)
                {
                    cts.Cancel();
                }
            });

            var task = Run(kind, kind == "tran" ? RcLadder(5) : Divider(), progress, cts.Token);
            var ex = Assert.ThrowsAny<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.NotNull(ex);
            Assert.True(task.IsCanceled || task.IsFaulted);
        });
    }

    [Fact]
    public async Task AlreadyCanceledTokenThrowsImmediately()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sim.OperatingPointAsync(Divider(), SimulationOptions.Default, null, cts.Token));
    }
}
