namespace CircuitBench.Core.Analysis;

/// <summary>保證回報的進度單調遞增（比上次小的值一律丟棄），並限制回報頻率。</summary>
internal sealed class MonotonicProgress(IProgress<AnalysisProgress>? inner, string stageZh)
{
    private double _last = -1;

    public void Report(double fraction)
    {
        if (inner is null)
        {
            return;
        }

        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction < _last + 0.005 && fraction < 1)
        {
            return;
        }

        if (fraction <= _last)
        {
            return;
        }

        _last = fraction;
        inner.Report(new AnalysisProgress(fraction, stageZh));
    }

    public void Complete() => Report(1);
}
