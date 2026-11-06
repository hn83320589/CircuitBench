using CircuitBench.Schematic.Geometry;

namespace CircuitBench.Schematic.Model;

public abstract record SymbolPrimitive;

public sealed record LinePrimitive(WorldPoint A, WorldPoint B) : SymbolPrimitive;

public sealed record PolylinePrimitive(IReadOnlyList<WorldPoint> Points, bool Closed = false) : SymbolPrimitive;

public sealed record CirclePrimitive(WorldPoint Center, double Radius, bool Filled = false) : SymbolPrimitive;

/// <summary>
/// 元件符號的幾何（本地座標、格距為單位）。畫布繪製與 SVG 匯出共用，確保兩者一致，
/// 且接腳端點與 <see cref="ComponentCatalog"/> 的接腳位置相同。
/// </summary>
public static class SymbolGeometry
{
    private static WorldPoint P(double x, double y) => new(x, y);

    private static LinePrimitive L(double x1, double y1, double x2, double y2) => new(P(x1, y1), P(x2, y2));

    public static IReadOnlyList<SymbolPrimitive> For(ComponentKind kind) => kind switch
    {
        ComponentKind.Resistor => Resistor(),
        ComponentKind.Capacitor => [L(-2, 0, -0.25, 0), L(-0.25, -0.8, -0.25, 0.8), L(0.25, -0.8, 0.25, 0.8), L(0.25, 0, 2, 0)],
        ComponentKind.Inductor => Inductor(),
        ComponentKind.VoltageSource =>
        [
            L(0, -2, 0, -1), new CirclePrimitive(P(0, 0), 1), L(0, 1, 0, 2),
            L(-0.25, -0.5, 0.25, -0.5), L(0, -0.75, 0, -0.25), L(-0.25, 0.5, 0.25, 0.5),
        ],
        ComponentKind.CurrentSource =>
        [
            L(0, -2, 0, -1), new CirclePrimitive(P(0, 0), 1), L(0, 1, 0, 2),
            L(0, -0.6, 0, 0.6), new PolylinePrimitive([P(-0.3, 0.25), P(0, 0.6), P(0.3, 0.25)]),
        ],
        ComponentKind.Diode =>
        [
            L(-2, 0, -0.6, 0), new PolylinePrimitive([P(-0.6, -0.6), P(0.6, 0), P(-0.6, 0.6)], Closed: true),
            L(0.6, -0.6, 0.6, 0.6), L(0.6, 0, 2, 0),
        ],
        ComponentKind.Vcvs => ControlledSource(isCurrent: false),
        ComponentKind.Vccs => ControlledSource(isCurrent: true),
        ComponentKind.Switch =>
        [
            L(-2, 0, -0.8, 0), new CirclePrimitive(P(-0.8, 0), 0.12), L(-0.8, 0, 0.7, -0.7),
            new CirclePrimitive(P(0.8, 0), 0.12), L(0.8, 0, 2, 0),
        ],
        _ => throw new NotSupportedException(kind.ToString()),
    };

    public static IReadOnlyList<SymbolPrimitive> Ground() =>
        [L(0, 0, 0, 0.4), L(-0.8, 0.4, 0.8, 0.4), L(-0.5, 0.7, 0.5, 0.7), L(-0.2, 1.0, 0.2, 1.0)];

    private static IReadOnlyList<SymbolPrimitive> Resistor()
    {
        var points = new List<WorldPoint> { P(-1, 0) };
        for (var i = 0; i < 6; i++)
        {
            points.Add(P(-1 + (i + 0.5) / 3, i % 2 == 0 ? -0.4 : 0.4));
        }

        points.Add(P(1, 0));
        return [L(-2, 0, -1, 0), new PolylinePrimitive(points), L(1, 0, 2, 0)];
    }

    private static IReadOnlyList<SymbolPrimitive> Inductor()
    {
        var points = new List<WorldPoint>();
        for (var hump = 0; hump < 4; hump++)
        {
            var cx = -0.75 + hump * 0.5;
            for (var k = 0; k <= 8; k++)
            {
                var a = Math.PI - k * Math.PI / 8;
                points.Add(P(cx + 0.25 * Math.Cos(a), -0.25 * Math.Sin(a)));
            }
        }

        return [L(-2, 0, -1, 0), new PolylinePrimitive(points), L(1, 0, 2, 0)];
    }

    private static IReadOnlyList<SymbolPrimitive> ControlledSource(bool isCurrent)
    {
        var list = new List<SymbolPrimitive>
        {
            new PolylinePrimitive([P(0.8, -0.7), P(1.5, 0), P(0.8, 0.7), P(0.1, 0)], Closed: true),
            L(2, -1, 0.8, -1), L(0.8, -1, 0.8, -0.7),
            L(2, 1, 0.8, 1), L(0.8, 1, 0.8, 0.7),
            L(-2, -1, -1.2, -1), new CirclePrimitive(P(-1.2, -1), 0.12),
            L(-2, 1, -1.2, 1), new CirclePrimitive(P(-1.2, 1), 0.12),
            L(-0.9, -0.75, -0.9, -0.45), L(-1.05, -0.6, -0.75, -0.6), L(-1.05, 0.6, -0.75, 0.6),
        };
        if (isCurrent)
        {
            list.Add(L(0.8, -0.4, 0.8, 0.4));
            list.Add(new PolylinePrimitive([P(0.6, 0.15), P(0.8, 0.4), P(1.0, 0.15)]));
        }
        else
        {
            list.Add(L(0.65, -0.3, 0.95, -0.3));
            list.Add(L(0.8, -0.45, 0.8, -0.15));
            list.Add(L(0.65, 0.3, 0.95, 0.3));
        }

        return list;
    }
}
