using CircuitBench.Core.Model;
using CircuitBench.Core.Validation;
using CircuitBench.Schematic.Connectivity;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;
using CircuitBench.TestKit;

namespace CircuitBench.Schematic.Tests;

/// <summary>驗收標準 2：連線萃取。</summary>
public class ConnectivityTests
{
    private static NodeName Net(ExtractionResult r, ElementId c, string pin) => r.PinNets[new PinRef(c, pin)];

    [Fact]
    public void CrossingWiresWithoutJunctionAreNotConnected()
    {
        var b = new SchematicBuilder();
        // R1 水平在 y=0，R2 垂直在 x=10；兩條導線在 (5,5) 交叉
        var r1 = b.Component(ComponentKind.Resistor, "R1", -2, 5);  // 接腳 (-4,5) (0,5)
        var r2 = b.Component(ComponentKind.Resistor, "R2", 5, -2, Rotation.R90); // 接腳 (5,-4) (5,0)
        b.Wire(0, 5, 10, 5);
        b.Wire(5, 0, 5, 10);
        var r = ConnectivityExtractor.Extract(b.Doc);
        Assert.NotEqual(Net(r, r1, "2"), Net(r, r2, "2"));
        Assert.DoesNotContain(new GridPoint(5, 5), r.Junctions);
    }

    [Fact]
    public void WireEndOnAnotherWiresMiddleCreatesJunction()
    {
        var b = new SchematicBuilder();
        var r1 = b.Component(ComponentKind.Resistor, "R1", -2, 0); // 右接腳 (0,0)
        var r2 = b.Component(ComponentKind.Resistor, "R2", 5, 7, Rotation.R90); // 上接腳 (5,5)
        b.Wire(0, 0, 10, 0);   // 主幹
        b.Wire(5, 5, 5, 0);    // T 字：端點 (5,0) 落在主幹中段
        var r = ConnectivityExtractor.Extract(b.Doc);
        Assert.Equal(Net(r, r1, "2"), Net(r, r2, "1"));
        Assert.Contains(new GridPoint(5, 0), r.Junctions);
    }

    [Fact]
    public void PinOnWireMiddleConnectsAndCreatesJunction()
    {
        var b = new SchematicBuilder();
        var r1 = b.Component(ComponentKind.Resistor, "R1", -2, 0);
        var r2 = b.Component(ComponentKind.Resistor, "R2", 5, 2, Rotation.R90); // 上接腳 (5,0)
        b.Wire(0, 0, 10, 0);
        var r = ConnectivityExtractor.Extract(b.Doc);
        Assert.Equal(Net(r, r1, "2"), Net(r, r2, "1"));
        Assert.Contains(new GridPoint(5, 0), r.Junctions);
    }

    [Fact]
    public void PinAndWireEndAtSamePointAreConnected()
    {
        var b = new SchematicBuilder();
        var r1 = b.Component(ComponentKind.Resistor, "R1", 0, 0);   // (−2,0) (2,0)
        var r2 = b.Component(ComponentKind.Resistor, "R2", 10, 0);  // (8,0) (12,0)
        b.Wire(2, 0, 8, 0);
        var r = ConnectivityExtractor.Extract(b.Doc);
        Assert.Equal(Net(r, r1, "2"), Net(r, r2, "1"));
        Assert.NotEqual(Net(r, r1, "1"), Net(r, r1, "2"));
    }

    [Fact]
    public void PinsTouchingDirectlyAreConnected()
    {
        var b = new SchematicBuilder();
        var r1 = b.Component(ComponentKind.Resistor, "R1", 0, 0);
        var r2 = b.Component(ComponentKind.Resistor, "R2", 4, 0);
        var r = ConnectivityExtractor.Extract(b.Doc);
        Assert.Equal(Net(r, r1, "2"), Net(r, r2, "1"));
    }

    [Fact]
    public void SameNameLabelsMergeCaseInsensitively()
    {
        var b = new SchematicBuilder();
        var r1 = b.Component(ComponentKind.Resistor, "R1", 0, 0);
        var r2 = b.Component(ComponentKind.Resistor, "R2", 20, 20);
        b.Label(2, 0, "Vout");
        b.Label(18, 20, "VOUT");
        var r = ConnectivityExtractor.Extract(b.Doc);
        Assert.Equal(Net(r, r1, "2"), Net(r, r2, "1"));
        Assert.Equal("Vout", Net(r, r1, "2").Value, ignoreCase: true);
    }

    [Fact]
    public void GroundIsNodeZeroAndAllGroundsMerge()
    {
        var b = new SchematicBuilder();
        var v = b.Component(ComponentKind.VoltageSource, "V1", 0, 0); // + (0,-2), − (0,2)
        var r = b.Component(ComponentKind.Resistor, "R1", 10, 0);
        b.Ground(0, 2);
        b.Ground(12, 0);
        b.Wire(0, -2, 8, -2);
        b.Wire(8, -2, 8, 0);
        var res = ConnectivityExtractor.Extract(b.Doc);
        Assert.Equal(NodeName.Ground, Net(res, v, "-"));
        Assert.Equal(NodeName.Ground, Net(res, r, "2"));
        Assert.Equal(Net(res, v, "+"), Net(res, r, "1"));
        Assert.Empty(res.Issues);
        Assert.Equal(2, res.Circuit.Components.Count);
    }

    [Fact]
    public void LabelNamedZeroIsGround()
    {
        var b = new SchematicBuilder();
        var r1 = b.Component(ComponentKind.Resistor, "R1", 0, 0);
        b.Label(2, 0, "0");
        var r = ConnectivityExtractor.Extract(b.Doc);
        Assert.Equal(NodeName.Ground, Net(r, r1, "2"));
    }

    [Fact]
    public void UnconnectedPinIsReportedWithComponentAndPin()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 0);
        b.Ground(0, 2);
        var r1 = b.Component(ComponentKind.Resistor, "R1", 2, -2); // 左接腳 (0,-2) 接到 V1+，右接腳懸空
        var r = ConnectivityExtractor.Extract(b.Doc);
        var issue = Assert.Single(r.Issues);
        Assert.Equal(IssueCode.UnconnectedPin, issue.Code);
        Assert.Equal(["R1"], issue.ComponentNames.ToArray());
        Assert.Equal("接腳2", issue.PinName);
        Assert.Equal(r1, r.ComponentIds["R1"]);
    }

    [Fact]
    public void PinOnDanglingWireIsStillUnconnected()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.Resistor, "R1", 0, 0);
        b.Wire(2, 0, 6, 0);
        b.Ground(-2, 0);
        var r = ConnectivityExtractor.Extract(b.Doc);
        Assert.Contains(r.Issues, i => i.Code == IssueCode.UnconnectedPin && i.PinName == "接腳2");
    }

    [Fact]
    public void AutomaticNetNamesUsePrefixAndAreUnique()
    {
        var b = new SchematicBuilder();
        var r1 = b.Component(ComponentKind.Resistor, "R1", 0, 0);
        var r2 = b.Component(ComponentKind.Resistor, "R2", 4, 0);
        var r = ConnectivityExtractor.Extract(b.Doc);
        var nets = new[] { Net(r, r1, "1"), Net(r, r1, "2"), Net(r, r2, "2") };
        Assert.All(nets, n => Assert.StartsWith("N$", n.Value));
        Assert.Equal(3, nets.Distinct().Count());
    }

    [Fact]
    public void InvalidParameterIsReportedAndComponentSkipped()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.Resistor, "R1", 0, 0, parameters: ("value", "abc"));
        b.Ground(-2, 0);
        b.Ground(2, 0);
        var r = ConnectivityExtractor.Extract(b.Doc);
        var issue = Assert.Single(r.Issues);
        Assert.Equal(IssueCode.InvalidParameter, issue.Code);
        Assert.Contains("R1", issue.MessageZh);
        Assert.Empty(r.Circuit.Components);
    }

    [Fact]
    public void DuplicateNamesAreReported()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.Resistor, "R1", 0, 0);
        b.Component(ComponentKind.Resistor, "r1", 4, 0);
        var r = ConnectivityExtractor.Extract(b.Doc);
        Assert.Contains(r.Issues, i => i.Code == IssueCode.DuplicateName);
    }

    [Fact]
    public void SourceParametersBecomeSourceSpec()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 0, parameters: [("dc", "1"), ("ac", "2 45"), ("waveform", "SIN(0 1 1k)")]);
        b.Ground(0, 2);
        b.Label(0, -2, "in");
        var v = Assert.IsType<VoltageSource>(ConnectivityExtractor.Extract(b.Doc).Circuit.Find("V1"));
        Assert.Equal(1, v.Spec.DcValue);
        Assert.Equal(new AcSpec(2, 45), v.Spec.Ac);
        Assert.Equal(new SineWaveform(0, 1, 1000), v.Spec.Waveform);
    }

    [Fact]
    public void VcvsPinsMapToNodesInCoreOrder()
    {
        var b = new SchematicBuilder();
        var e = b.Component(ComponentKind.Vcvs, "E1", 0, 0);
        b.Label(2, -1, "op");
        b.Label(2, 1, "on");
        b.Label(-2, -1, "cp");
        b.Label(-2, 1, "cn");
        var vcvs = Assert.IsType<Vcvs>(ConnectivityExtractor.Extract(b.Doc).Circuit.Find("E1"));
        Assert.Equal(["op", "on", "cp", "cn"], vcvs.Nodes.Select(n => n.Value).ToArray());
    }
}

/// <summary>驗收標準 2：旋轉與鏡射後的接腳位置。</summary>
public class OrientationTests
{
    [Theory]
    [InlineData(Rotation.R0, false, -2, 0, 2, 0)]
    [InlineData(Rotation.R90, false, 0, -2, 0, 2)]
    [InlineData(Rotation.R180, false, 2, 0, -2, 0)]
    [InlineData(Rotation.R270, false, 0, 2, 0, -2)]
    [InlineData(Rotation.R0, true, 2, 0, -2, 0)]
    [InlineData(Rotation.R90, true, 0, 2, 0, -2)]
    public void TwoTerminalPinPositions(Rotation rot, bool mirrored, int x1, int y1, int x2, int y2)
    {
        var c = new ComponentSymbol(new ElementId(1), ComponentKind.Resistor, "R1", new GridPoint(10, 20), new Orientation(rot, mirrored), SchematicBuilder.Params(ComponentKind.Resistor));
        var pins = c.PinPositions;
        Assert.Equal(new GridPoint(10 + x1, 20 + y1), pins[0].Position);
        Assert.Equal(new GridPoint(10 + x2, 20 + y2), pins[1].Position);
    }

    [Fact]
    public void ControlledSourcePinsAfterRotationAndMirror()
    {
        var c = new ComponentSymbol(new ElementId(1), ComponentKind.Vccs, "G1", new GridPoint(0, 0), new Orientation(Rotation.R90, true), SchematicBuilder.Params(ComponentKind.Vccs));
        // out+ 本地 (2,−1)：鏡射 → (−2,−1)；順時針 90° (x,y)→(−y,x) → (1,−2)
        Assert.Equal(new GridPoint(1, -2), c.PinPositions.Single(p => p.Name == "out+").Position);
    }

    [Fact]
    public void FourRotationsReturnToOriginal()
    {
        var o = Orientation.Default;
        for (var i = 0; i < 4; i++) o = o.RotateClockwise();
        Assert.Equal(Orientation.Default, o);
        Assert.Equal(Orientation.Default, Orientation.Default.MirrorHorizontally().MirrorHorizontally());
    }

    [Fact]
    public void DocumentRotateKeepsConnectivityOfRotatedGroup()
    {
        var b = new SchematicBuilder();
        var r1 = b.Component(ComponentKind.Resistor, "R1", 0, 0);
        var r2 = b.Component(ComponentKind.Resistor, "R2", 8, 0);
        var w = b.Wire(2, 0, 6, 0);
        var rotated = b.Doc.Rotate([r1, r2, w]);
        var r = ConnectivityExtractor.Extract(rotated);
        Assert.Equal(r.PinNets[new PinRef(r1, "2")], r.PinNets[new PinRef(r2, "1")]);
        var moved = (ComponentSymbol)rotated.Find(r1)!;
        Assert.Equal(Rotation.R90, moved.Orientation.Rotation);
    }

    [Fact]
    public void MirrorMovesPinPositionsToMatchWorldMirror()
    {
        var b = new SchematicBuilder();
        var d = b.Component(ComponentKind.Diode, "D1", 3, 0);
        var before = ((ComponentSymbol)b.Doc.Find(d)!).PinPositions;
        var after = ((ComponentSymbol)b.Doc.Mirror([d]).Find(d)!).PinPositions;
        // 以元件本身位置 x=3 為軸鏡射：陽極由左側換到右側
        Assert.Equal(new GridPoint(2 * 3 - before[0].Position.X, 0), after[0].Position);
        Assert.Equal(new GridPoint(2 * 3 - before[1].Position.X, 0), after[1].Position);
    }
}
