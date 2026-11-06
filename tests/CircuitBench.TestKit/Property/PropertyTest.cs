using Xunit.Sdk;

namespace CircuitBench.TestKit.Property;

/// <summary>
/// 最簡單的性質測試 harness：以固定的基底種子產生 N 組輸入，
/// 任何一組失敗時，例外訊息會帶出可直接重現的種子。
/// </summary>
public static class PropertyTest
{
    public const int DefaultBaseSeed = 20261106;

    /// <summary>以環境變數 CIRCUITBENCH_SEED 指定單一種子可重現失敗案例。</summary>
    public static void ForAll(int iterations, Action<Random, int> property, int baseSeed = DefaultBaseSeed)
    {
        var overrideSeed = Environment.GetEnvironmentVariable("CIRCUITBENCH_SEED");
        if (int.TryParse(overrideSeed, out var single))
        {
            Run(single, 0, property);
            return;
        }

        for (var i = 0; i < iterations; i++)
        {
            var seed = unchecked(baseSeed + i * 7919);
            Run(seed, i, property);
        }
    }

    private static void Run(int seed, int iteration, Action<Random, int> property)
    {
        try
        {
            property(new Random(seed), seed);
        }
        catch (Exception ex) when (ex is not PropertyFailedException)
        {
            throw new PropertyFailedException(
                $"性質測試失敗：iteration={iteration}, seed={seed}（設定環境變數 CIRCUITBENCH_SEED={seed} 可重現）\n{ex.Message}", ex);
        }
    }
}

public sealed class PropertyFailedException(string message, Exception inner) : XunitException(message, inner);
