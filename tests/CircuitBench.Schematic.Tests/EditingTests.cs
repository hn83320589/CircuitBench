using CircuitBench.Core.IO;
using CircuitBench.Core.Model;
using CircuitBench.Schematic.Connectivity;
using CircuitBench.Schematic.Editing;
using CircuitBench.Schematic.Export;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;
using CircuitBench.TestKit;
using CircuitBench.TestKit.Property;

namespace CircuitBench.Schematic.Tests;

public class UndoHistoryTests
{
    /// <summary>隨機的圖面編輯操作。</summary>
    private static SchematicDocument RandomEdit(Random rng, SchematicDocument doc)
    {
        var ids = doc.Elements.Select(e => e.Id).ToList();
        IEnumerable<ElementId> Some() => ids.Where(_ => rng.Next(3) == 0).DefaultIfEmpty(ids.Count > 0 ? ids[0] : default);
        GridPoint P() => new(rng.Next(-20, 21), rng.Next(-20, 21));
        switch (rng.Next(ids.Count == 0 ? 3 : 9))
        {
            case 0:
            {
                var kind = (ComponentKind)rng.Next(9);
                return doc.Add(new ComponentSymbol(default, kind, doc.NextComponentName(kind), P(),
                    new Orientation((Rotation)rng.Next(4), rng.Next(2) == 0), ComponentCatalog.Get(kind).Defaults));
            }

            case 1:
            {
                var a = P();
                var b = rng.Next(2) == 0 ? a with { X = a.X + rng.Next(1, 10) } : a with { Y = a.Y + rng.Next(1, 10) };
                return doc.Add(new Wire(default, a, b));
            }

            case 2:
                return rng.Next(2) == 0 ? doc.Add(new NetLabel(default, P(), $"n{rng.Next(5)}")) : doc.Add(new GroundSymbol(default, P()));
            case 3:
                return doc.Remove(Some());
            case 4:
                return doc.Move(Some(), rng.Next(-3, 4), rng.Next(-3, 4));
            case 5:
                return doc.Rotate(Some());
            case 6:
                return doc.Mirror(Some());
            case 7:
                return doc.Duplicate(Some().Select(doc.Find).OfType<SchematicElement>().ToList(), 2, 2, out _);
            default:
            {
                var c = doc.Components.FirstOrDefault();
                return c is null ? doc : doc.Replace(c.WithParameter(c.Kind == ComponentKind.Resistor ? "value" : "gain", $"{rng.Next(1, 100)}k"));
            }
        }
    }

    private static string NetlistOf(SchematicDocument d) =>
        SpiceNetlistWriter.Write(new SpiceDeck(ConnectivityExtractor.Extract(d).Circuit, []));

    [Fact]
    public void Property_EveryUndoRestoresDocumentAndNetlist()
    {
        PropertyTest.ForAll(10, (rng, _) =>
        {
            var history = new UndoHistory<SchematicDocument>(SchematicDocument.Empty, capacity: 150);
            var snapshots = new List<SchematicDocument> { history.Current };
            var netlists = new List<string> { NetlistOf(history.Current) };
            for (var step = 0; step < 120; step++)
            {
                history.Apply(RandomEdit(rng, history.Current));
                snapshots.Add(history.Current);
                netlists.Add(NetlistOf(history.Current));
            }

            // 逐步復原 120 層：每一步都回到前一個狀態（圖面與網表）
            for (var k = snapshots.Count - 2; k >= 0; k--)
            {
                Assert.True(history.Undo());
                Assert.True(history.Current.ContentEquals(snapshots[k]), $"復原到第 {k} 步時圖面不同");
                Assert.Equal(netlists[k], NetlistOf(history.Current));
            }

            Assert.False(history.CanUndo);

            // 全部重做回來
            for (var k = 1; k < snapshots.Count; k++)
            {
                Assert.True(history.Redo());
                Assert.True(history.Current.ContentEquals(snapshots[k]));
            }
        });
    }

    [Fact]
    public void AtLeastHundredLevelsAndOldestDroppedBeyondCapacity()
    {
        var h = new UndoHistory<int>(0, capacity: 100);
        for (var i = 1; i <= 130; i++) h.Apply(i);
        Assert.Equal(100, h.UndoCount);
        var count = 0;
        while (h.Undo()) count++;
        Assert.Equal(100, count);
        Assert.Equal(30, h.Current);
    }

    [Fact]
    public void NewEditClearsRedo()
    {
        var h = new UndoHistory<int>(0);
        h.Apply(1);
        h.Apply(2);
        h.Undo();
        Assert.True(h.CanRedo);
        h.Apply(5);
        Assert.False(h.CanRedo);
        Assert.Equal(5, h.Current);
    }

    [Fact]
    public void DefaultCapacityIsAtLeastOneHundred()
    {
        Assert.True(new UndoHistory<int>(0).Capacity >= 100);
    }
}

public class DocumentEditingTests
{
    [Fact]
    public void DuplicateAssignsNewIdsAndUniqueNames()
    {
        var b = new SchematicBuilder();
        var r1 = b.Component(ComponentKind.Resistor, "R1", 0, 0);
        var doc = b.Doc.Duplicate([b.Doc.Find(r1)!], 5, 0, out var ids);
        var copy = (ComponentSymbol)doc.Find(ids[0])!;
        Assert.NotEqual(r1, copy.Id);
        Assert.Equal("R2", copy.Name);
        Assert.Equal(new GridPoint(5, 0), copy.Position);
    }

    [Fact]
    public void NextComponentNameFillsGaps()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.Capacitor, "C1", 0, 0);
        b.Component(ComponentKind.Capacitor, "C3", 4, 0);
        Assert.Equal("C2", b.Doc.NextComponentName(ComponentKind.Capacitor));
        Assert.Equal("V1", b.Doc.NextComponentName(ComponentKind.VoltageSource));
    }

    [Fact]
    public void RemoveAndMove()
    {
        var b = new SchematicBuilder();
        var w = b.Wire(0, 0, 4, 0);
        var g = b.Ground(9, 9);
        var moved = b.Doc.Move([w], 1, 2);
        Assert.Equal(new Wire(w, new GridPoint(1, 2), new GridPoint(5, 2)), moved.Find(w));
        Assert.Null(moved.Remove([g]).Find(g));
    }
}

public class HitTesterTests
{
    [Fact]
    public void ClickOnComponentBodyAndWire()
    {
        var b = new SchematicBuilder();
        var r = b.Component(ComponentKind.Resistor, "R1", 0, 0);
        var w = b.Wire(0, 5, 10, 5);
        Assert.Equal(r, HitTester.HitTest(b.Doc, new WorldPoint(0.3, 0.2))!.Id);
        Assert.Equal(w, HitTester.HitTest(b.Doc, new WorldPoint(6, 5.1))!.Id);
        Assert.Null(HitTester.HitTest(b.Doc, new WorldPoint(20, 20)));
    }

    [Fact]
    public void RotatedComponentBoundsFollowOrientation()
    {
        var b = new SchematicBuilder();
        var r = b.Component(ComponentKind.Resistor, "R1", 0, 0, Rotation.R90);
        Assert.Equal(r, HitTester.HitTest(b.Doc, new WorldPoint(0, 1.8))!.Id);
        Assert.Null(HitTester.HitTest(b.Doc, new WorldPoint(1.8, 0), 0.1));
    }

    [Fact]
    public void TopmostElementWins()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.Resistor, "R1", 0, 0);
        var top = b.Component(ComponentKind.Capacitor, "C1", 0, 0);
        Assert.Equal(top, HitTester.HitTest(b.Doc, new WorldPoint(0, 0))!.Id);
    }

    [Fact]
    public void BoxSelectRequiresFullyInside()
    {
        var b = new SchematicBuilder();
        var r = b.Component(ComponentKind.Resistor, "R1", 0, 0);
        var w = b.Wire(10, 0, 20, 0);
        var selected = HitTester.BoxSelect(b.Doc, new WorldRect(-3, -3, 15, 3));
        Assert.Contains(r, selected);
        Assert.DoesNotContain(w, selected);
    }
}

public class AutoLayoutTests
{
    [Fact]
    public void ImportedCircuitRoundTripsThroughSchematic()
    {
        var circuit = Circuit.Create([
            new VoltageSource("V1", "in", "0", new SourceSpec(1, new PulseWaveform(0, 5, 0, 1e-6, 1e-6, 1e-3, 2e-3), new AcSpec(1, 0))),
            new Resistor("R1", "in", "out", 1e3),
            new Capacitor("C1", "out", "0", 1e-6),
            new Diode("D1", "out", "x", new DiodeModel("D", 2e-14, 1.5)),
            new Vcvs("E1", "y", "0", "out", "0", -10),
            new Resistor("R2", "y", "x", 10),
            new TimedSwitch("S1", "x", "0", [1e-3], false),
        ]);
        var doc = AutoLayout.FromCircuit(circuit);
        var extracted = ConnectivityExtractor.Extract(doc);
        Assert.Empty(extracted.Issues);
        Assert.True(circuit.StructurallyEquals(extracted.Circuit));
    }

    [Fact]
    public void Property_RandomCircuitsSurviveAutoLayout()
    {
        PropertyTest.ForAll(50, (rng, _) =>
        {
            var circuit = RandomCircuits.Linear(rng, includeReactive: true, includeAc: true);
            var extracted = ConnectivityExtractor.Extract(AutoLayout.FromCircuit(circuit));
            Assert.True(circuit.StructurallyEquals(extracted.Circuit));
        });
    }
}

public class SvgExportTests
{
    [Fact]
    public void SvgContainsSymbolsLabelsJunctionsAndIssueMarkers()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.Resistor, "R1", 0, 0);
        b.Component(ComponentKind.Resistor, "R2", 5, 2, Rotation.R90);
        b.Wire(2, 0, 10, 0);
        b.Label(10, 0, "out<&>");
        b.Ground(-2, 0);
        var extraction = ConnectivityExtractor.Extract(b.Doc);
        var svg = SchematicSvgExporter.Export(b.Doc, extraction);
        Assert.StartsWith("<svg", svg);
        Assert.Contains("R1", svg);
        Assert.Contains("out&lt;&amp;&gt;", svg);
        Assert.Contains("polyline", svg);
        Assert.Contains("stroke=\"#d00\"", svg); // R2 下方接腳未連接
        Assert.True(System.Xml.Linq.XDocument.Parse(svg).Root is not null);
    }

    [Fact]
    public void EmptyDocumentExportsValidSvg()
    {
        Assert.NotNull(System.Xml.Linq.XDocument.Parse(SchematicSvgExporter.Export(SchematicDocument.Empty)).Root);
    }
}
