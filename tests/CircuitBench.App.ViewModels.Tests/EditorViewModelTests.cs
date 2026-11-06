using CircuitBench.App.ViewModels.Editor;
using CircuitBench.Core.Validation;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;

namespace CircuitBench.App.ViewModels.Tests;

/// <summary>驗收標準 23：編輯器的工具切換、選取、拖曳、旋轉、標籤命名、檢查結果在圖上的標示。</summary>
public class EditorViewModelTests
{
    private static WorldPoint W(double x, double y) => new(x, y);

    private static void Click(EditorViewModel vm, double x, double y, PointerModifiers m = PointerModifiers.None)
    {
        vm.PointerPressed(W(x, y), m);
        vm.PointerReleased(W(x, y), m);
    }

    private static void Drag(EditorViewModel vm, double x1, double y1, double x2, double y2)
    {
        vm.PointerPressed(W(x1, y1));
        vm.PointerMoved(W((x1 + x2) / 2, (y1 + y2) / 2));
        vm.PointerMoved(W(x2, y2));
        vm.PointerReleased(W(x2, y2));
    }

    [Fact]
    public void ToolSwitchingUpdatesStatusAndPlacesSelectedKind()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.Capacitor);
        Assert.Equal(EditorTool.Place, vm.ActiveTool);
        Assert.Contains("電容", vm.StatusText);
        Click(vm, 3.2, 4.7);
        var c = Assert.Single(vm.Document.Components);
        Assert.Equal(ComponentKind.Capacitor, c.Kind);
        Assert.Equal("C1", c.Name);
        Assert.Equal(new GridPoint(3, 5), c.Position);

        vm.SelectTool(EditorTool.Ground);
        Click(vm, 0, 10);
        Assert.Single(vm.Document.Elements.OfType<GroundSymbol>());

        vm.EscapeCommand.Execute(null);
        Assert.Equal(EditorTool.Select, vm.ActiveTool);
        Assert.Empty(vm.Selection);
    }

    [Fact]
    public void ClickSelectsShiftTogglesAndEmptyClickClears()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.Resistor);
        Click(vm, 0, 0);
        Click(vm, 10, 0);
        vm.SelectTool(EditorTool.Select);
        Click(vm, 0, 0);
        var r1 = Assert.Single(vm.Selection);
        Click(vm, 10, 0, PointerModifiers.Shift);
        Assert.Equal(2, vm.Selection.Count);
        Click(vm, 10, 0, PointerModifiers.Shift);
        Assert.Equal([r1], vm.Selection.ToArray());
        Click(vm, 50, 50);
        Assert.Empty(vm.Selection);
    }

    [Fact]
    public void BoxSelectionSelectsEnclosedElements()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.Resistor);
        Click(vm, 0, 0);
        Click(vm, 10, 0);
        Click(vm, 40, 0);
        vm.SelectTool(EditorTool.Select);
        Drag(vm, -5, -5, 15, 5);
        Assert.Equal(2, vm.Selection.Count);
    }

    [Fact]
    public void DraggingMovesSelectionAsSingleUndoStep()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.Resistor);
        Click(vm, 0, 0);
        vm.SelectTool(EditorTool.Select);
        var undoBefore = vm.History.UndoCount;

        vm.PointerPressed(W(0.2, 0.1));
        vm.PointerMoved(W(2, 1));
        Assert.Equal(new GridPoint(2, 1), vm.DragOffset); // 預覽
        Assert.Equal(new GridPoint(0, 0), vm.Document.Components.Single().Position); // 文件尚未改變
        vm.PointerMoved(W(5.1, 3.2));
        vm.PointerReleased(W(5.1, 3.2));

        Assert.Equal(new GridPoint(5, 3), vm.Document.Components.Single().Position);
        Assert.Equal(undoBefore + 1, vm.History.UndoCount);
        vm.UndoCommand.Execute(null);
        Assert.Equal(new GridPoint(0, 0), vm.Document.Components.Single().Position);
    }

    [Fact]
    public void RotateAndMirrorSelectedComponent()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.Diode);
        Click(vm, 0, 0);
        vm.RotateCommand.Execute(null);
        var d = vm.Document.Components.Single();
        Assert.Equal(Rotation.R90, d.Orientation.Rotation);
        Assert.Equal(new GridPoint(0, -2), d.PinPositions[0].Position);
        vm.MirrorCommand.Execute(null);
        Assert.True(vm.Document.Components.Single().Orientation.Mirrored);
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(Orientation.Default, vm.Document.Components.Single().Orientation);
    }

    [Fact]
    public void RotateInPlaceModeRotatesNextPlacement()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.Resistor);
        vm.EscapeCommand.Execute(null);
        vm.SelectTool(EditorTool.Place, ComponentKind.Resistor);
        vm.RotateCommand.Execute(null);
        Click(vm, 0, 0);
        Assert.Equal(Rotation.R90, vm.Document.Components.Single().Orientation.Rotation);
    }

    [Fact]
    public void WireToolDrawsLShapeInOneStep()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Wire);
        vm.PointerPressed(W(0, 0));
        vm.PointerMoved(W(4, 3));
        Assert.Equal(2, vm.WirePreview.Count);
        vm.PointerReleased(W(4, 3));
        var wires = vm.Document.Wires.ToList();
        Assert.Equal(2, wires.Count);
        Assert.Contains(wires, w => w.A == new GridPoint(0, 0) && w.B == new GridPoint(4, 0));
        Assert.Contains(wires, w => w.A == new GridPoint(4, 0) && w.B == new GridPoint(4, 3));
        Assert.Equal(1, vm.History.UndoCount);
        Assert.Empty(vm.WirePreview);
    }

    [Fact]
    public void LabelNamingValidatesAndConnects()
    {
        var vm = new EditorViewModel { LabelName = "vin" };
        vm.SelectTool(EditorTool.Label);
        Click(vm, 2, 0);
        var label = Assert.IsType<NetLabel>(vm.Document.Find(vm.Selection.Single()));
        Assert.Equal("vin", label.NetName);
        Assert.Null(vm.RenameLabel(label.Id, "out"));
        Assert.Equal("out", ((NetLabel)vm.Document.Find(label.Id)!).NetName);
        Assert.Contains("N$", vm.RenameLabel(label.Id, "N$3"));
        Assert.Contains("空白", vm.RenameLabel(label.Id, "a b"));
        Assert.Contains("不可空白", vm.RenameLabel(label.Id, "  "));
    }

    [Fact]
    public void RenameComponentRejectsDuplicatesAndWrongPrefix()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.Resistor);
        Click(vm, 0, 0);
        Click(vm, 10, 0);
        var ids = vm.Document.Components.Select(c => c.Id).ToList();
        Assert.Contains("已經被使用", vm.RenameComponent(ids[1], "r1"));
        Assert.Contains("R", vm.RenameComponent(ids[1], "X9"));
        Assert.Null(vm.RenameComponent(ids[1], "Rload"));
        Assert.Equal("Rload", vm.Document.Components.Last().Name);
    }

    [Fact]
    public void ParameterEditIsUndoable()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.Resistor);
        Click(vm, 0, 0);
        var id = vm.Document.Components.Single().Id;
        vm.SetParameter(id, "value", "4.7k");
        Assert.Equal("4.7k", vm.SelectedComponent!.Parameter("value"));
        vm.UndoCommand.Execute(null);
        Assert.Equal("1k", vm.Document.Components.Single().Parameter("value"));
    }

    [Fact]
    public void CopyPasteDeleteAndSelectAll()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.Resistor);
        Click(vm, 0, 0);
        vm.CopyCommand.Execute(null);
        vm.PasteCommand.Execute(null);
        vm.PasteCommand.Execute(null);
        Assert.Equal(["R1", "R2", "R3"], vm.Document.Components.Select(c => c.Name).ToArray());
        Assert.Equal(new GridPoint(4, 4), vm.Document.Components.Last().Position);
        vm.SelectAllCommand.Execute(null);
        vm.DeleteCommand.Execute(null);
        Assert.Empty(vm.Document.Elements);
        vm.UndoCommand.Execute(null);
        Assert.Equal(3, vm.Document.Components.Count());
    }

    [Fact]
    public void CheckResultsAreMarkedOnCanvasAndFollowEdits()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.VoltageSource);
        Click(vm, 0, 0);              // V1：+ (0,−2)、− (0,2)，都沒有接
        Assert.False(vm.RunCheck());
        Assert.Contains(vm.Issues, i => i.Code == IssueCode.NoGround);
        Assert.Contains(vm.Issues, i => i.Code == IssueCode.UnconnectedPin);
        var pinMarker = vm.IssueMarkers.First(m => m.Code == IssueCode.UnconnectedPin);
        Assert.True(pinMarker.Bounds.Contains(new WorldPoint(0, -2)) || pinMarker.Bounds.Contains(new WorldPoint(0, 2)));
        Assert.Contains("紅框", vm.StatusText);

        // 補上接地與電阻後，檢查自動更新
        vm.SelectTool(EditorTool.Ground);
        Click(vm, 0, 2);
        vm.SelectTool(EditorTool.Place, ComponentKind.Resistor);
        Click(vm, 2, -2);   // 水平電阻：(0,−2)–(4,−2)
        vm.SelectTool(EditorTool.Ground);
        Click(vm, 4, -2);
        Assert.Empty(vm.Issues);
        Assert.Empty(vm.IssueMarkers);
    }

    [Fact]
    public void FloatingNodeMarkersPointAtNodeLocations()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Place, ComponentKind.Resistor);
        Click(vm, 0, 0);                    // R1 (−2,0)–(2,0)
        vm.SelectTool(EditorTool.Place, ComponentKind.Capacitor);
        Click(vm, 4, 0);                    // C1 (2,0)–(6,0)
        vm.SelectTool(EditorTool.Place, ComponentKind.Capacitor);
        Click(vm, 8, 0);                    // C2 (6,0)–(10,0)
        vm.SelectTool(EditorTool.Ground);
        Click(vm, -2, 0);
        Click(vm, 10, 0);
        Assert.False(vm.RunCheck());
        Assert.Contains(vm.IssueMarkers, m => m.Code == IssueCode.FloatingNode && m.Bounds.Contains(new WorldPoint(6, 0)));
    }

    [Fact]
    public void ViewportZoomKeepsAnchorFixed()
    {
        var vp = new Viewport();
        var before = vp.ToWorld(300, 250);
        vp.ZoomAt(300, 250, 2);
        var after = vp.ToWorld(300, 250);
        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
        vp.PanBy(10, 0);
        Assert.Equal(before.X - 10 / vp.Zoom, vp.ToWorld(300, 250).X, 9);
    }

    [Fact]
    public void PanToolMovesViewportNotDocument()
    {
        var vm = new EditorViewModel();
        vm.SelectTool(EditorTool.Pan);
        var x0 = vm.Viewport.OffsetX;
        vm.PointerPressed(W(0, 0));
        vm.PointerMoved(W(1, 0));
        vm.PointerReleased(W(1, 0));
        Assert.NotEqual(x0, vm.Viewport.OffsetX);
        Assert.Empty(vm.Document.Elements);
    }
}
