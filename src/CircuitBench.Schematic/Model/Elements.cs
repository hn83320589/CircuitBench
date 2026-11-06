using System.Collections.Immutable;
using CircuitBench.Schematic.Geometry;

namespace CircuitBench.Schematic.Model;

public readonly record struct ElementId(int Value)
{
    public override string ToString() => $"#{Value}";
}

/// <summary>圖面元素（不可變）。</summary>
public abstract record SchematicElement(ElementId Id)
{
    /// <summary>此元素在格點上的「端子」：與其他端子同點即相連。</summary>
    public abstract IEnumerable<GridPoint> Terminals { get; }

    public abstract SchematicElement Translate(int dx, int dy);

    /// <summary>繞某個格點順時針旋轉 90°。</summary>
    public abstract SchematicElement RotateAround(GridPoint center);

    /// <summary>以通過某個 x 座標的垂直線左右鏡射。</summary>
    public abstract SchematicElement MirrorAround(int centerX);

    public SchematicElement WithId(ElementId id) => this with { Id = id };

    protected static GridPoint RotatePoint(GridPoint p, GridPoint c) => c + new Orientation(Rotation.R90, false).Apply(p - c);

    protected static GridPoint MirrorPoint(GridPoint p, int cx) => new(2 * cx - p.X, p.Y);
}

public enum ComponentKind
{
    Resistor,
    Capacitor,
    Inductor,
    VoltageSource,
    CurrentSource,
    Diode,
    Vcvs,
    Vccs,
    Switch,
}

/// <summary>元件符號。參數保留使用者輸入的原字串（例如 "4.7k"），萃取網表時才解析。</summary>
public sealed record ComponentSymbol(
    ElementId Id,
    ComponentKind Kind,
    string Name,
    GridPoint Position,
    Orientation Orientation,
    ImmutableSortedDictionary<string, string> Parameters) : SchematicElement(Id)
{
    public override IEnumerable<GridPoint> Terminals => PinPositions.Select(p => p.Position);

    /// <summary>每個接腳的名稱與絕對格點位置（已套用旋轉與鏡射）。</summary>
    public IReadOnlyList<(string Name, GridPoint Position)> PinPositions =>
        ComponentCatalog.Get(Kind).Pins.Select(p => (p.Name, Position + Orientation.Apply(p.Offset))).ToList();

    public string Parameter(string key) =>
        Parameters.TryGetValue(key, out var v) ? v : ComponentCatalog.Get(Kind).Defaults.GetValueOrDefault(key, string.Empty);

    public ComponentSymbol WithParameter(string key, string value) => this with { Parameters = Parameters.SetItem(key, value) };

    public override SchematicElement Translate(int dx, int dy) => this with { Position = Position + new GridPoint(dx, dy) };

    public override SchematicElement RotateAround(GridPoint center) =>
        this with { Position = RotatePoint(Position, center), Orientation = Orientation.RotateClockwise() };

    public override SchematicElement MirrorAround(int centerX) =>
        this with { Position = MirrorPoint(Position, centerX), Orientation = Orientation.MirrorHorizontally() };

    public bool Equals(ComponentSymbol? other) =>
        other is not null && Id == other.Id && Kind == other.Kind && Name == other.Name && Position == other.Position &&
        Orientation == other.Orientation && Parameters.SequenceEqual(other.Parameters);

    public override int GetHashCode() => HashCode.Combine(Id, Kind, Name, Position, Orientation, Parameters.Count);
}

/// <summary>導線：只允許水平或垂直，兩端點不同。</summary>
public sealed record Wire(ElementId Id, GridPoint A, GridPoint B) : SchematicElement(Id)
{
    public override IEnumerable<GridPoint> Terminals => [A, B];

    public bool IsValid => A != B && (A.X == B.X || A.Y == B.Y);

    public override SchematicElement Translate(int dx, int dy) => this with { A = A + new GridPoint(dx, dy), B = B + new GridPoint(dx, dy) };

    public override SchematicElement RotateAround(GridPoint center) => this with { A = RotatePoint(A, center), B = RotatePoint(B, center) };

    public override SchematicElement MirrorAround(int centerX) => this with { A = MirrorPoint(A, centerX), B = MirrorPoint(B, centerX) };
}

/// <summary>網路標籤：同名標籤視為同一節點。</summary>
public sealed record NetLabel(ElementId Id, GridPoint Position, string NetName) : SchematicElement(Id)
{
    public override IEnumerable<GridPoint> Terminals => [Position];

    public override SchematicElement Translate(int dx, int dy) => this with { Position = Position + new GridPoint(dx, dy) };

    public override SchematicElement RotateAround(GridPoint center) => this with { Position = RotatePoint(Position, center) };

    public override SchematicElement MirrorAround(int centerX) => this with { Position = MirrorPoint(Position, centerX) };
}

/// <summary>接地符號：固定為節點 0。</summary>
public sealed record GroundSymbol(ElementId Id, GridPoint Position) : SchematicElement(Id)
{
    public override IEnumerable<GridPoint> Terminals => [Position];

    public override SchematicElement Translate(int dx, int dy) => this with { Position = Position + new GridPoint(dx, dy) };

    public override SchematicElement RotateAround(GridPoint center) => this with { Position = RotatePoint(Position, center) };

    public override SchematicElement MirrorAround(int centerX) => this with { Position = MirrorPoint(Position, centerX) };
}
