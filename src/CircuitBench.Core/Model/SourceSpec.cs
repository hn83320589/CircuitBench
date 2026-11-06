namespace CircuitBench.Core.Model;

/// <summary>交流分析用的振幅與相位（度），與直流、暫態互相獨立。</summary>
public sealed record AcSpec(double Magnitude, double PhaseDegrees);

/// <summary>
/// 獨立電源規格（同 SPICE）：
/// <list type="bullet">
/// <item><see cref="DcValue"/>：.op 與 .dc 使用的值；未指定時取暫態波形在 t=0 的值。</item>
/// <item><see cref="Waveform"/>：暫態函數（SIN/PULSE/PWL）；未指定時暫態沿用直流值。</item>
/// <item><see cref="Ac"/>：交流分析的振幅與相位。</item>
/// </list>
/// </summary>
public sealed record SourceSpec(double? DcValue, Waveform? Waveform, AcSpec? Ac)
{
    public static SourceSpec FromDc(double value) => new(value, null, null);

    /// <summary>暫態分析實際使用的波形。</summary>
    public Waveform Transient => Waveform ?? new DcWaveform(DcValue ?? 0);

    public double EffectiveDcValue => DcValue ?? Waveform?.Evaluate(0) ?? 0;
}
