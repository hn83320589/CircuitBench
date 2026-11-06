namespace CircuitBench.Core.Model;

/// <summary>交流分析用的振幅與相位（度），與直流、暫態互相獨立。</summary>
public sealed record AcSpec(double Magnitude, double PhaseDegrees);

/// <summary>
/// 獨立電源規格。<see cref="DcValue"/> 用於 .op 與 .dc；為 null 時取暫態波形在 t=0 的值。
/// 暫態分析一律使用 <see cref="Transient"/>。
/// </summary>
public sealed record SourceSpec(double? DcValue, Waveform Transient, AcSpec? Ac)
{
    public static SourceSpec FromDc(double value) => new(value, new DcWaveform(value), null);

    public double EffectiveDcValue => DcValue ?? Transient.Evaluate(0);
}
