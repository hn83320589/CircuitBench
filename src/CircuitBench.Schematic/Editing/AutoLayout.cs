using System.Collections.Immutable;
using CircuitBench.Core.Model;
using CircuitBench.Schematic.Connectivity;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;

namespace CircuitBench.Schematic.Editing;

/// <summary>
/// 匯入網表時的自動排列：網表不含座標，因此把元件排成格狀，
/// 每個接腳拉一小段導線接到「網路標籤」（接地則放接地符號）。
/// 萃取出的網表與原電路相同（同名標籤即同節點）。
/// </summary>
public static class AutoLayout
{
    private const int CellWidth = 10;
    private const int CellHeight = 8;

    public static SchematicDocument FromCircuit(Circuit circuit)
    {
        var doc = SchematicDocument.Empty;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(circuit.Components.Count)));
        for (var i = 0; i < circuit.Components.Count; i++)
        {
            var c = circuit.Components[i];
            var kind = ComponentFactory.KindOf(c);
            var position = new GridPoint(5 + i % columns * CellWidth, 5 + i / columns * CellHeight);
            var symbol = new ComponentSymbol(default, kind, c.Name, position, Orientation.Default,
                ComponentFactory.ToParameters(c).ToImmutableSortedDictionary());
            doc = doc.Add(symbol);

            var pins = symbol.PinPositions;
            for (var p = 0; p < pins.Count; p++)
            {
                var pin = pins[p].Position;
                var outward = Outward(ComponentCatalog.Get(kind).Pins[p].Offset);
                var end = pin + outward;
                doc = doc.Add(new Wire(default, pin, end));
                var node = c.Nodes[p];
                doc = node.IsGround
                    ? doc.Add(new GroundSymbol(default, end))
                    : doc.Add(new NetLabel(default, end, node.Value));
            }
        }

        return doc;
    }

    private static GridPoint Outward(GridPoint offset) =>
        Math.Abs(offset.X) >= Math.Abs(offset.Y)
            ? new GridPoint(Math.Sign(offset.X), 0)
            : new GridPoint(0, Math.Sign(offset.Y));
}
