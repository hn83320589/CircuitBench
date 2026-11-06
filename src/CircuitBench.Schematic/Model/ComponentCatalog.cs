using System.Collections.Immutable;
using CircuitBench.Schematic.Geometry;

namespace CircuitBench.Schematic.Model;

public sealed record PinDefinition(string Name, GridPoint Offset);

public sealed record ParameterDefinition(string Key, string LabelZh, string Hint);

/// <summary>元件類型定義：接腳位置（本地座標，未旋轉）、預設參數、名稱前綴與本體外框。</summary>
public sealed record ComponentDefinition(
    ComponentKind Kind,
    string Prefix,
    string DisplayNameZh,
    IReadOnlyList<PinDefinition> Pins,
    IReadOnlyList<ParameterDefinition> ParameterDefinitions,
    ImmutableSortedDictionary<string, string> Defaults,
    WorldRect LocalBounds);

/// <summary>
/// 元件目錄。接腳順序與 Core 元件的 Nodes 順序一致：
/// 兩端元件 [接腳1, 接腳2]、電源 [正端, 負端]、二極體 [陽極, 陰極]、受控源 [輸出正, 輸出負, 控制正, 控制負]。
/// </summary>
public static class ComponentCatalog
{
    private static readonly GridPoint Left = new(-2, 0);
    private static readonly GridPoint Right = new(2, 0);
    private static readonly GridPoint Top = new(0, -2);
    private static readonly GridPoint Bottom = new(0, 2);

    private static readonly WorldRect Horizontal = new(-2, -0.6, 2, 0.6);
    private static readonly WorldRect Vertical = new(-0.9, -2, 0.9, 2);

    private static readonly Dictionary<ComponentKind, ComponentDefinition> Definitions = new()
    {
        [ComponentKind.Resistor] = new(ComponentKind.Resistor, "R", "電阻", [new("1", Left), new("2", Right)],
            [new("value", "電阻值 (Ω)", "例如 1k、4.7kohm")], Defaults(("value", "1k")), Horizontal),
        [ComponentKind.Capacitor] = new(ComponentKind.Capacitor, "C", "電容", [new("1", Left), new("2", Right)],
            [new("value", "電容值 (F)", "例如 100n、10uF")], Defaults(("value", "1u")), Horizontal with { Top = -0.9, Bottom = 0.9 }),
        [ComponentKind.Inductor] = new(ComponentKind.Inductor, "L", "電感", [new("1", Left), new("2", Right)],
            [new("value", "電感值 (H)", "例如 10m、1uH")], Defaults(("value", "1m")), Horizontal),
        [ComponentKind.VoltageSource] = new(ComponentKind.VoltageSource, "V", "電壓源", [new("+", Top), new("-", Bottom)],
            SourceParameters("V"), Defaults(("dc", "5"), ("ac", ""), ("waveform", "")), Vertical),
        [ComponentKind.CurrentSource] = new(ComponentKind.CurrentSource, "I", "電流源", [new("+", Top), new("-", Bottom)],
            SourceParameters("A"), Defaults(("dc", "1m"), ("ac", ""), ("waveform", "")), Vertical),
        [ComponentKind.Diode] = new(ComponentKind.Diode, "D", "二極體", [new("A", Left), new("K", Right)],
            [new("is", "飽和電流 Is (A)", "預設 1e-14"), new("n", "理想因子 n", "預設 1")],
            Defaults(("is", "1e-14"), ("n", "1")), Horizontal with { Top = -0.8, Bottom = 0.8 }),
        [ComponentKind.Vcvs] = new(ComponentKind.Vcvs, "E", "電壓控制電壓源",
            [new("out+", new GridPoint(2, -1)), new("out-", new GridPoint(2, 1)), new("ctrl+", new GridPoint(-2, -1)), new("ctrl-", new GridPoint(-2, 1))],
            [new("gain", "電壓增益", "無單位，例如 -10")], Defaults(("gain", "10")), new WorldRect(-2, -1.5, 2, 1.5)),
        [ComponentKind.Vccs] = new(ComponentKind.Vccs, "G", "電壓控制電流源",
            [new("out+", new GridPoint(2, -1)), new("out-", new GridPoint(2, 1)), new("ctrl+", new GridPoint(-2, -1)), new("ctrl-", new GridPoint(-2, 1))],
            [new("gain", "轉導 (S)", "例如 1m")], Defaults(("gain", "1m")), new WorldRect(-2, -1.5, 2, 1.5)),
        [ComponentKind.Switch] = new(ComponentKind.Switch, "S", "開關", [new("1", Left), new("2", Right)],
            [
                new("times", "切換時間 (s)", "以空白分隔，例如 1m 2m"),
                new("initial", "初始狀態", "on 或 off"),
                new("ron", "導通電阻 Ron (Ω)", "預設 1m"),
                new("roff", "截止電阻 Roff (Ω)", "預設 1e12"),
            ],
            Defaults(("times", "1m"), ("initial", "off"), ("ron", "1m"), ("roff", "1e12")), Horizontal with { Top = -0.8, Bottom = 0.4 }),
    };

    public static IEnumerable<ComponentDefinition> All => Definitions.Values;

    public static ComponentDefinition Get(ComponentKind kind) => Definitions[kind];

    private static ImmutableSortedDictionary<string, string> Defaults(params (string Key, string Value)[] items) =>
        items.ToImmutableSortedDictionary(i => i.Key, i => i.Value);

    private static IReadOnlyList<ParameterDefinition> SourceParameters(string unit) =>
    [
        new("dc", $"直流值 ({unit})", "用於工作點與直流掃描"),
        new("ac", "交流振幅與相位", "例如 1 0（振幅 相位°），空白代表無"),
        new("waveform", "暫態波形", "SIN(VO VA FREQ …)、PULSE(V1 V2 TD TR TF PW PER)、PWL(t1 v1 …)，空白代表沿用直流值"),
    ];
}
