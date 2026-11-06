using System.Collections.Immutable;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;

namespace CircuitBench.TestKit;

/// <summary>測試用的圖面建構器。</summary>
public sealed class SchematicBuilder
{
    public SchematicDocument Doc { get; private set; } = SchematicDocument.Empty;

    public ElementId Component(ComponentKind kind, string name, int x, int y, Rotation rotation = Rotation.R0, bool mirrored = false, params (string Key, string Value)[] parameters)
    {
        var defaults = ComponentCatalog.Get(kind).Defaults;
        var p = parameters.Aggregate(defaults, (d, kv) => d.SetItem(kv.Key, kv.Value));
        Doc = Doc.Add(new ComponentSymbol(default, kind, name, new GridPoint(x, y), new Orientation(rotation, mirrored), p), out var id);
        return id;
    }

    public ElementId Wire(int x1, int y1, int x2, int y2)
    {
        Doc = Doc.Add(new Wire(default, new GridPoint(x1, y1), new GridPoint(x2, y2)), out var id);
        return id;
    }

    public ElementId Label(int x, int y, string name)
    {
        Doc = Doc.Add(new NetLabel(default, new GridPoint(x, y), name), out var id);
        return id;
    }

    public ElementId Ground(int x, int y)
    {
        Doc = Doc.Add(new GroundSymbol(default, new GridPoint(x, y)), out var id);
        return id;
    }

    public static ImmutableSortedDictionary<string, string> Params(ComponentKind kind) => ComponentCatalog.Get(kind).Defaults;
}
