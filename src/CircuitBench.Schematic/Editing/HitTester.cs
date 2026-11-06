using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;

namespace CircuitBench.Schematic.Editing;

public static class HitTester
{
    /// <summary>元素在世界座標的外框。</summary>
    public static WorldRect Bounds(SchematicElement e) => e switch
    {
        ComponentSymbol c => TransformRect(ComponentCatalog.Get(c.Kind).LocalBounds, c.Position, c.Orientation),
        Wire w => WorldRect.FromPoints(w.A.ToWorld(), w.B.ToWorld()),
        NetLabel l => new WorldRect(l.Position.X - 0.2, l.Position.Y - 1.0, l.Position.X + 0.6 * Math.Max(1, l.NetName.Length) + 0.4, l.Position.Y + 0.2),
        GroundSymbol g => new WorldRect(g.Position.X - 0.8, g.Position.Y, g.Position.X + 0.8, g.Position.Y + 1.2),
        _ => throw new NotSupportedException(e.GetType().Name),
    };

    /// <summary>點選：回傳最上層（最後加入）且在容許距離內的元素。</summary>
    public static SchematicElement? HitTest(SchematicDocument doc, WorldPoint p, double tolerance = 0.3)
    {
        for (var i = doc.Elements.Count - 1; i >= 0; i--)
        {
            var e = doc.Elements[i];
            var hit = e switch
            {
                Wire w => GridMath.DistanceToSegment(p, w.A.ToWorld(), w.B.ToWorld()) <= tolerance,
                _ => Bounds(e).Inflate(tolerance).Contains(p),
            };
            if (hit)
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>框選：完全落在矩形內的元素。</summary>
    public static IReadOnlySet<ElementId> BoxSelect(SchematicDocument doc, WorldRect rect) =>
        doc.Elements.Where(e => rect.Contains(Bounds(e))).Select(e => e.Id).ToHashSet();

    private static WorldRect TransformRect(WorldRect local, GridPoint origin, Orientation o)
    {
        var corners = new[]
        {
            o.Apply(new WorldPoint(local.Left, local.Top)),
            o.Apply(new WorldPoint(local.Right, local.Top)),
            o.Apply(new WorldPoint(local.Left, local.Bottom)),
            o.Apply(new WorldPoint(local.Right, local.Bottom)),
        };
        return new WorldRect(
            origin.X + corners.Min(c => c.X), origin.Y + corners.Min(c => c.Y),
            origin.X + corners.Max(c => c.X), origin.Y + corners.Max(c => c.Y));
    }
}
