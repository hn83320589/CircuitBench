using CircuitBench.Core.Analysis;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;
using CircuitBench.Schematic.Persistence;
using CircuitBench.TestKit;

namespace CircuitBench.App.ViewModels.Tests.Samples;

/// <summary>範例電路定義：圖面、分析設定與自動驗證的預期結果（解析解或手算值）。</summary>
public sealed record Sample(string Id, string TitleZh, SchematicDocument Schematic, AnalysisSettings Analysis, Action<SampleRun> Verify);

/// <summary>執行一個範例後可供驗證的結果。</summary>
public sealed class SampleRun
{
    public OperatingPointResult? Op { get; init; }

    public TransientResult? Tran { get; init; }

    public AcResult? Ac { get; init; }

    public double[] T => Tran!.Data.X;

    public double[] V(string node) => Tran!.Data[SignalId.V(node)];

    public double[] I(string name) => Tran!.Data[SignalId.I(name)];

    public double At(string signal, double t)
    {
        var id = signal.StartsWith("I(") ? SignalId.I(signal[2..^1]) : SignalId.V(signal[2..^1]);
        return Core.Measurement.Interpolation.At(Tran!.Data.X, Tran.Data[id], t);
    }
}

public static class SampleCatalog
{
    private static AnalysisSettings Op => new() { Kind = AnalysisKind.OperatingPoint };

    private static AnalysisSettings Tran(string stop, string maxStep = "") => new() { Kind = AnalysisKind.Transient, TranStop = stop, TranMaxStep = maxStep };

    private static AnalysisSettings Ac(string start, string stop, string perDecade) => new() { Kind = AnalysisKind.Ac, AcDecade = true, AcStart = start, AcStop = stop, AcPoints = perDecade };

    private static void Near(double expected, double actual, double tolerance, string what) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}：預期 {expected:G6}，實際 {actual:G6}（容許 {tolerance:G3}）");

    public static IReadOnlyList<Sample> All { get; } =
    [
        VoltageDivider(), RcLowPass(), RlcSeriesResonance(), HalfWaveRectifier(), FullWaveBridge(), BridgeWithFilter(),
        InvertingAmplifier(), SwitchedInductor(), LcOscillator(), RcStepResponse(), DiodeForwardBias(), VccsTransconductance(),
    ];

    private static Sample VoltageDivider()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 4, parameters: ("dc", "10"));
        b.Component(ComponentKind.Resistor, "R1", 2, 2, parameters: ("value", "1k"));
        b.Component(ComponentKind.Resistor, "R2", 4, 4, Rotation.R90, parameters: ("value", "1k"));
        b.Label(0, 2, "in");
        b.Label(4, 2, "out");
        b.Ground(0, 6);
        b.Ground(4, 6);
        return new("01-voltage-divider", "分壓器", b.Doc, Op, r =>
        {
            Near(5, r.Op![SignalId.V("out")], 1e-9, "V(out) = 10·R2/(R1+R2)");
            Near(-5e-3, r.Op[SignalId.I("V1")], 1e-12, "I(V1)（由正端流入為正）");
        });
    }

    private static Sample RcLowPass()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 4, parameters: [("dc", "0"), ("ac", "1 0")]);
        b.Component(ComponentKind.Resistor, "R1", 2, 2, parameters: ("value", "1k"));
        b.Component(ComponentKind.Capacitor, "C1", 4, 4, Rotation.R90, parameters: ("value", "100n"));
        b.Label(0, 2, "in");
        b.Label(4, 2, "out");
        b.Ground(0, 6);
        b.Ground(4, 6);
        return new("02-rc-lowpass", "RC 低通濾波器", b.Doc, Ac("10", "1meg", "100"), r =>
        {
            var fc = 1 / (2 * Math.PI * 1e3 * 100e-9);
            var f = r.Ac!.Data.Frequency;
            var db = Core.Bode.BodeMath.MagnitudeDb(r.Ac.Data[SignalId.V("out")]);
            var i = Array.FindIndex(db, d => d < -3.0103);
            var frac = (-3.0103 - db[i - 1]) / (db[i] - db[i - 1]);
            var f3 = Math.Pow(10, Math.Log10(f[i - 1]) + frac * Math.Log10(f[i] / f[i - 1]));
            Near(fc, f3, fc * 1e-3, "−3 dB 頻率 1/(2πRC)");
        });
    }

    private static Sample RlcSeriesResonance()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 4, parameters: [("dc", "0"), ("ac", "1 0")]);
        b.Component(ComponentKind.Inductor, "L1", 2, 2, parameters: ("value", "10m"));
        b.Component(ComponentKind.Capacitor, "C1", 6, 2, parameters: ("value", "100n"));
        b.Component(ComponentKind.Resistor, "R1", 8, 4, Rotation.R90, parameters: ("value", "10"));
        b.Label(8, 2, "out");
        b.Ground(0, 6);
        b.Ground(8, 6);
        return new("03-rlc-series-resonance", "RLC 串聯共振", b.Doc, Ac("1k", "20k", "400"), r =>
        {
            var f0 = 1 / (2 * Math.PI * Math.Sqrt(10e-3 * 100e-9));
            var f = r.Ac!.Data.Frequency;
            var mag = r.Ac.Data[SignalId.V("out")].Select(h => h.Magnitude).ToArray();
            var peak = Array.IndexOf(mag, mag.Max());
            Near(f0, f[peak], f0 * 3e-3, "共振頻率 1/(2π√LC)");
            Near(1, mag[peak], 0.01, "共振時 V(out)/V(in)（頻率點離散，峰值略低於 1）");
        });
    }

    private static Sample HalfWaveRectifier()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 4, parameters: [("dc", ""), ("waveform", "SIN(0 10 50)")]);
        b.Component(ComponentKind.Diode, "D1", 4, 2);
        b.Wire(0, 2, 2, 2);
        b.Component(ComponentKind.Resistor, "RL", 8, 4, Rotation.R90, parameters: ("value", "1k"));
        b.Wire(6, 2, 8, 2);
        b.Label(0, 2, "in");
        b.Label(8, 2, "out");
        b.Ground(0, 6);
        b.Ground(8, 6);
        return new("04-half-wave-rectifier", "二極體半波整流", b.Doc, Tran("40m", "20u"), r =>
        {
            var vout = r.V("out");
            // 峰值約 10 V 減去峰值電流時的二極體壓降（≈ n·Vt·ln(I/Is) ≈ 0.71 V）
            Near(9.29, vout.Max(), 0.05, "輸出峰值");
            Assert.True(vout.Min() > -1e-3, "負半週輸出應接近 0");
        });
    }

    private static void Bridge(SchematicBuilder b)
    {
        b.Component(ComponentKind.VoltageSource, "V1", 6, 6, parameters: [("dc", ""), ("waveform", "SIN(0 10 50)")]);
        b.Wire(6, 2, 6, 4);
        b.Wire(6, 8, 6, 10);
        b.Component(ComponentKind.Diode, "D1", 8, 2);   // a → p
        b.Component(ComponentKind.Diode, "D2", 8, 10);  // b → p
        b.Component(ComponentKind.Diode, "D3", 4, 4);   // 0 → a
        b.Component(ComponentKind.Diode, "D4", 4, 8);   // 0 → b
        b.Wire(10, 2, 10, 10);
        b.Wire(2, 4, 2, 8);
        b.Ground(2, 8);
        b.Component(ComponentKind.Resistor, "RL", 14, 6, Rotation.R90, parameters: ("value", "1k"));
        b.Wire(10, 2, 14, 2);
        b.Wire(14, 2, 14, 4);
        b.Ground(14, 8);
        b.Label(14, 2, "out");
        b.Label(6, 2, "a");
        b.Label(6, 10, "b");
    }

    private static Sample FullWaveBridge()
    {
        var b = new SchematicBuilder();
        Bridge(b);
        return new("05-full-wave-bridge", "二極體橋式全波整流", b.Doc, Tran("40m", "20u"), r =>
        {
            var vout = r.V("out");
            // 兩顆二極體導通：10 V − 2 × 約 0.71 V
            Near(8.58, vout.Max(), 0.06, "輸出峰值");
            // 全波：正負半週都有輸出，週期內出現兩個峰
            Near(r.At("V(out)", 5e-3), r.At("V(out)", 15e-3), 0.02, "正負半週峰值對稱");
        });
    }

    private static Sample BridgeWithFilter()
    {
        var b = new SchematicBuilder();
        Bridge(b);
        b.Component(ComponentKind.Capacitor, "CF", 18, 6, Rotation.R90, parameters: ("value", "100u"));
        b.Wire(14, 2, 18, 2);
        b.Wire(18, 2, 18, 4);
        b.Ground(18, 8);
        return new("06-bridge-with-filter", "橋式整流加濾波電容", b.Doc, Tran("100m", "100u"), r =>
        {
            // 手算模型：峰值 Vp ≈ 10 − 2·0.71 V 後電容以 τ = RC 放電，直到整流後的正弦 |10·sin ωt| − 2·Vd 追上為止。
            // （粗估公式 Vp/(2fRC) ≈ 0.86 V 假設放電整個半週，是上限。）
            const double vd = 0.71, rc = 1e3 * 100e-6, w = 2 * Math.PI * 50;
            var vc = 10 - 2 * vd;
            var lowest = vc;
            for (var tt = 0.25 / 50; tt < 0.75 / 50; tt += 1e-6)
            {
                // 理想二極體模型：整流電壓高於電容電壓時充電，否則以 RC 放電
                vc = Math.Max(vc * Math.Exp(-1e-6 / rc), Math.Abs(10 * Math.Sin(w * tt)) - 2 * vd);
                lowest = Math.Min(lowest, vc);
            }

            var expectedRipple = 10 - 2 * vd - lowest;
            var t = r.T;
            var v = r.V("out");
            var window = Enumerable.Range(0, t.Length).Where(i => t[i] >= 60e-3).Select(i => v[i]).ToArray();
            var ripple = window.Max() - window.Min();
            Near(expectedRipple, ripple, 0.08, "漣波峰對峰值");
            Near(8.4, window.Average(), 0.4, "平均輸出");
        });
    }

    private static Sample InvertingAmplifier()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "Vin", 0, 4, parameters: [("dc", "0.5"), ("waveform", "SIN(0 0.5 1k)")]);
        b.Component(ComponentKind.Resistor, "R1", 2, 2, parameters: ("value", "1k"));
        b.Component(ComponentKind.Resistor, "R2", 6, 2, parameters: ("value", "10k"));
        b.Component(ComponentKind.Vcvs, "E1", 6, 7, parameters: ("gain", "-100k"));
        b.Wire(4, 2, 4, 6);   // 反相輸入 m → ctrl+
        b.Ground(4, 8);       // ctrl−
        b.Wire(8, 2, 8, 6);   // out → out+
        b.Ground(8, 8);       // out−
        b.Ground(0, 6);
        b.Label(0, 2, "in");
        b.Label(4, 2, "m");
        b.Label(8, 2, "out");
        return new("07-inverting-amplifier", "受控源組成的反相放大器", b.Doc, Op, r =>
        {
            // 有限增益 A：Vout = −Vin·(R2/R1) / (1 + (1 + R2/R1)/A)
            Near(-0.5 * 10 / (1 + 11 / 1e5), r.Op![SignalId.V("out")], 1e-6, "輸出電壓（增益約 −10）");
            Near(0, r.Op[SignalId.V("m")], 1e-3, "虛接地");
        });
    }

    private static Sample SwitchedInductor()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 4, parameters: ("dc", "10"));
        b.Component(ComponentKind.Switch, "S1", 2, 2, parameters: [("times", "1m 6m"), ("initial", "off")]);
        b.Component(ComponentKind.Resistor, "R1", 6, 2, parameters: ("value", "10"));
        b.Component(ComponentKind.Inductor, "L1", 8, 4, Rotation.R90, parameters: ("value", "10m"));
        b.Component(ComponentKind.Diode, "D1", 4, 4, Rotation.R270); // 飛輪二極體：陽極接地、陰極接開關後端
        b.Ground(0, 6);
        b.Ground(4, 6);
        b.Ground(8, 6);
        b.Label(4, 2, "sw");
        b.Label(8, 2, "x");
        return new("08-switched-inductor", "開關切換的電感電路", b.Doc, Tran("10m", "20u"), r =>
        {
            // τ = L/R = 1 ms；開關在 1 ms 閉合：i(t) = (V/R)(1 − e^{−(t−1ms)/τ})
            var vr = 10.0 / (10 + 1e-3);
            Near(0, r.At("I(L1)", 0.5e-3), 1e-6, "閉合前電流");
            Near(vr * (1 - Math.Exp(-1)), r.At("I(L1)", 2e-3), 5e-3, "閉合後 1τ");
            Near(vr * (1 - Math.Exp(-5)), r.At("I(L1)", 6e-3), 5e-3, "閉合後 5τ");
            Assert.True(r.At("I(L1)", 9e-3) < r.At("I(L1)", 6.5e-3), "開關打開後電流經飛輪二極體衰減");
            Assert.Equal(0, WaveformMath.StepwiseAlternations(r.V("sw"), 1e-3));
        });
    }

    private static Sample LcOscillator()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.CurrentSource, "I1", 0, 4, Rotation.R180, parameters: [("dc", ""), ("waveform", "PWL(0 1m 1n 0)")]);
        b.Component(ComponentKind.Inductor, "L1", 4, 4, Rotation.R90, parameters: ("value", "1m"));
        b.Component(ComponentKind.Capacitor, "C1", 8, 4, Rotation.R90, parameters: ("value", "1u"));
        b.Wire(0, 2, 4, 2);
        b.Wire(4, 2, 8, 2);
        b.Wire(0, 6, 4, 6);
        b.Wire(4, 6, 8, 6);
        b.Ground(4, 6);
        b.Label(8, 2, "a");
        var period = 2 * Math.PI * Math.Sqrt(1e-3 * 1e-6);
        return new("09-lc-oscillator", "LC 振盪", b.Doc, Tran("2m", "2u"), r =>
        {
            // f = 1/(2π√LC) ≈ 5033 Hz；振幅 V = I·√(L/C) ≈ 31.6 mV
            var m = Core.Measurement.Measurements.Measure(r.T, r.V("a"), 0, 10 * period);
            Near(1 / period, m.Frequency!.Value, 1 / period * 2e-3, "振盪頻率");
            Near(1e-3 * Math.Sqrt(1e-3 / 1e-6), m.Max, 1e-3, "電壓振幅");
        });
    }

    private static Sample RcStepResponse()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 4, parameters: [("dc", ""), ("waveform", "PULSE(0 5 0.1m 1u 1u 10m)")]);
        b.Component(ComponentKind.Resistor, "R1", 2, 2, parameters: ("value", "1k"));
        b.Component(ComponentKind.Capacitor, "C1", 4, 4, Rotation.R90, parameters: ("value", "1u"));
        b.Label(0, 2, "in");
        b.Label(4, 2, "out");
        b.Ground(0, 6);
        b.Ground(4, 6);
        return new("10-rc-step-response", "RC 步階響應", b.Doc, Tran("6m"), r =>
        {
            // τ = RC = 1 ms：1τ 後達到 63.2%，5τ 後 99.3%
            Near(5 * (1 - Math.Exp(-1)), r.At("V(out)", 0.1e-3 + 1e-3), 5e-3, "1τ 時的電壓");
            Near(5 * (1 - Math.Exp(-5)), r.At("V(out)", 0.1e-3 + 5e-3), 5e-3, "5τ 時的電壓");
        });
    }

    private static Sample DiodeForwardBias()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 4, parameters: ("dc", "5"));
        b.Component(ComponentKind.Resistor, "R1", 2, 2, parameters: ("value", "1k"));
        b.Component(ComponentKind.Diode, "D1", 4, 4, Rotation.R90);
        b.Label(4, 2, "a");
        b.Ground(0, 6);
        b.Ground(4, 6);
        return new("11-diode-forward-bias", "二極體順向偏壓工作點", b.Doc, Op, r =>
        {
            // Lambert W 解析解（忽略 gmin）：I = (nVt/R)·W((Is·R/nVt)·e^((Vs+Is·R)/nVt)) − Is
            var i = LambertW.SeriesDiodeCurrent(5, 1e3, 1e-14, 1);
            Near(i, r.Op![SignalId.I("D1")], i * 1e-4, "二極體電流");
            Near(5 - i * 1e3, r.Op[SignalId.V("a")], 1e-3, "二極體壓降");
        });
    }

    private static Sample VccsTransconductance()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "Vin", 0, 4, parameters: ("dc", "0.5"));
        b.Component(ComponentKind.Vccs, "G1", 6, 5, parameters: ("gain", "10m"));
        b.Wire(0, 2, 4, 2);
        b.Wire(4, 2, 4, 4);   // ctrl+
        b.Ground(4, 6);       // ctrl−
        b.Component(ComponentKind.Resistor, "RL", 10, 4, parameters: ("value", "1k"));
        b.Ground(12, 4);
        b.Ground(8, 6);       // out−
        b.Ground(0, 6);
        b.Label(0, 2, "in");
        b.Label(8, 4, "out");
        return new("12-vccs-transconductance", "電壓控制電流源（轉導放大）", b.Doc, Op, r =>
        {
            // G 由 out+ 經元件流到 out− 的電流 = gm·Vin = 5 mA，流出 out 節點 → Vout = −gm·Vin·RL
            Near(-5, r.Op![SignalId.V("out")], 1e-9, "輸出電壓");
        });
    }
}
