using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CircuitBench.Core.Validation;
using CircuitBench.Schematic.Connectivity;
using CircuitBench.Schematic.Editing;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;

namespace CircuitBench.App.ViewModels.Editor;

public enum EditorTool
{
    Select,
    Wire,
    Place,
    Label,
    Ground,
    Pan,
}

[Flags]
public enum PointerModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
}

/// <summary>檢查結果在圖上的標示：外框（世界座標）與訊息。</summary>
public sealed record IssueMarker(WorldRect Bounds, string MessageZh, IssueCode Code, ElementId? Element);

/// <summary>
/// 電路圖編輯器。所有圖面變更都經過 <see cref="UndoHistory{T}"/>，一個使用者動作 = 一個復原步驟。
/// 指標事件以世界座標傳入（由 View 透過 <see cref="Viewport"/> 轉換）。
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly List<SchematicElement> _clipboard = [];
    private WorldPoint? _pressPoint;
    private bool _dragging;
    private ExtractionResult? _extraction;
    private IReadOnlyList<CircuitIssue> _issues = [];

    public EditorViewModel()
    {
        History = new UndoHistory<SchematicDocument>(SchematicDocument.Empty);
        History.Changed += (_, _) => OnDocumentChanged();
    }

    public UndoHistory<SchematicDocument> History { get; }

    public SchematicDocument Document => History.Current;

    public Viewport Viewport { get; } = new();

    public event EventHandler? DocumentChanged;

    /// <summary>重繪需求（拖曳預覽、選取改變等不影響文件的狀態）。</summary>
    public event EventHandler? VisualChanged;

    [ObservableProperty]
    private EditorTool _activeTool = EditorTool.Select;

    [ObservableProperty]
    private ComponentKind _placeKind = ComponentKind.Resistor;

    /// <summary>標籤工具下一次放置的名稱。</summary>
    [ObservableProperty]
    private string _labelName = "out";

    [ObservableProperty]
    private string _statusText = "選取工具：點選元件，拖曳移動；空白處拖曳可框選。";

    public ImmutableHashSet<ElementId> Selection { get; private set; } = [];

    /// <summary>拖曳中的位移預覽（格點）。</summary>
    public GridPoint DragOffset { get; private set; }

    /// <summary>框選矩形預覽。</summary>
    public WorldRect? SelectionBox { get; private set; }

    /// <summary>畫導線時的預覽線段（L 形：先水平再垂直）。</summary>
    public IReadOnlyList<(GridPoint A, GridPoint B)> WirePreview { get; private set; } = [];

    /// <summary>游標所在的格點（放置預覽）。</summary>
    public GridPoint? HoverPoint { get; private set; }

    public ExtractionResult Extraction => _extraction ??= ConnectivityExtractor.Extract(Document);

    public IReadOnlyList<CircuitIssue> Issues => _issues;

    public IReadOnlyList<IssueMarker> IssueMarkers { get; private set; } = [];

    public ComponentSymbol? SelectedComponent =>
        Selection.Count == 1 ? Document.Find(Selection.First()) as ComponentSymbol : null;

    public NetLabel? SelectedLabel =>
        Selection.Count == 1 ? Document.Find(Selection.First()) as NetLabel : null;

    partial void OnActiveToolChanged(EditorTool value)
    {
        CancelGesture();
        StatusText = value switch
        {
            EditorTool.Select => "選取工具：點選元件，拖曳移動；空白處拖曳可框選。",
            EditorTool.Wire => "導線工具：按下起點、拖曳到終點後放開（自動轉成水平與垂直兩段）。",
            EditorTool.Place => $"放置{ComponentCatalog.Get(PlaceKind).DisplayNameZh}：在格點上按一下。按 R 旋轉。",
            EditorTool.Label => $"網路標籤：按一下放置「{LabelName}」。同名標籤視為相連。",
            EditorTool.Ground => "接地：按一下放置接地符號（節點 0）。",
            _ => "平移：拖曳移動畫面。",
        };
    }

    public void SelectTool(EditorTool tool, ComponentKind? kind = null)
    {
        if (kind is { } k)
        {
            PlaceKind = k;
        }

        ActiveTool = tool;
        OnActiveToolChanged(tool);
    }

    // ---------- 指標事件 ----------

    public void PointerPressed(WorldPoint p, PointerModifiers modifiers = PointerModifiers.None)
    {
        _pressPoint = p;
        _dragging = false;
        var g = p.Snap();
        switch (ActiveTool)
        {
            case EditorTool.Select:
            {
                var hit = HitTester.HitTest(Document, p);
                if (hit is null)
                {
                    if (!modifiers.HasFlag(PointerModifiers.Shift))
                    {
                        SetSelection([]);
                    }

                    SelectionBox = WorldRect.FromPoints(p, p);
                }
                else if (modifiers.HasFlag(PointerModifiers.Shift))
                {
                    SetSelection(Selection.Contains(hit.Id) ? Selection.Remove(hit.Id) : Selection.Add(hit.Id));
                }
                else if (!Selection.Contains(hit.Id))
                {
                    SetSelection([hit.Id]);
                }

                break;
            }

            case EditorTool.Wire:
                WirePreview = [];
                break;
            case EditorTool.Place:
            {
                var kind = PlaceKind;
                var symbol = new ComponentSymbol(default, kind, Document.NextComponentName(kind), g, _placeOrientation, ComponentCatalog.Get(kind).Defaults);
                var doc = Document.Add(symbol, out var id);
                History.Apply(doc);
                SetSelection([id]);
                StatusText = $"已放置 {symbol.Name}。";
                break;
            }

            case EditorTool.Label:
            {
                var name = string.IsNullOrWhiteSpace(LabelName) ? "out" : LabelName.Trim();
                History.Apply(Document.Add(new NetLabel(default, g, name), out var id));
                SetSelection([id]);
                break;
            }

            case EditorTool.Ground:
                History.Apply(Document.Add(new GroundSymbol(default, g), out var gid));
                SetSelection([gid]);
                break;
        }

        RaiseVisual();
    }

    private Orientation _placeOrientation = Orientation.Default;

    public void PointerMoved(WorldPoint p, PointerModifiers modifiers = PointerModifiers.None)
    {
        HoverPoint = p.Snap();
        if (_pressPoint is not { } start)
        {
            RaiseVisual();
            return;
        }

        if (p.DistanceTo(start) > 0.3)
        {
            _dragging = true;
        }

        switch (ActiveTool)
        {
            case EditorTool.Select when SelectionBox is not null:
                SelectionBox = WorldRect.FromPoints(start, p);
                break;
            case EditorTool.Select when _dragging && Selection.Count > 0:
                DragOffset = p.Snap() - start.Snap();
                break;
            case EditorTool.Wire:
                WirePreview = LShape(start.Snap(), p.Snap());
                break;
            case EditorTool.Pan:
            {
                var (sx0, sy0) = Viewport.ToScreen(start);
                var (sx1, sy1) = Viewport.ToScreen(p);
                Viewport.PanBy(sx1 - sx0, sy1 - sy0);
                break;
            }
        }

        RaiseVisual();
    }

    public void PointerReleased(WorldPoint p, PointerModifiers modifiers = PointerModifiers.None)
    {
        if (_pressPoint is not { } start)
        {
            return;
        }

        switch (ActiveTool)
        {
            case EditorTool.Select when SelectionBox is { } box:
            {
                var boxed = HitTester.BoxSelect(Document, WorldRect.FromPoints(start, p));
                if (_dragging)
                {
                    SetSelection(modifiers.HasFlag(PointerModifiers.Shift) ? Selection.Union(boxed) : boxed.ToImmutableHashSet());
                }

                _ = box;
                break;
            }

            case EditorTool.Select when _dragging && Selection.Count > 0:
            {
                var offset = p.Snap() - start.Snap();
                if (offset != default)
                {
                    History.Apply(Document.Move(Selection, offset.X, offset.Y));
                    StatusText = $"已移動 {Selection.Count} 個元素。";
                }

                break;
            }

            case EditorTool.Wire:
            {
                var segments = LShape(start.Snap(), p.Snap());
                if (segments.Count > 0)
                {
                    var doc = Document.AddRange(segments.Select(s => (SchematicElement)new Wire(default, s.A, s.B)), out var ids);
                    History.Apply(doc);
                    SetSelection([.. ids]);
                }

                break;
            }
        }

        CancelGesture();
        RaiseVisual();
    }

    private void CancelGesture()
    {
        _pressPoint = null;
        _dragging = false;
        DragOffset = default;
        SelectionBox = null;
        WirePreview = [];
    }

    private static List<(GridPoint A, GridPoint B)> LShape(GridPoint a, GridPoint b)
    {
        var list = new List<(GridPoint, GridPoint)>();
        var corner = new GridPoint(b.X, a.Y);
        if (a != corner)
        {
            list.Add((a, corner));
        }

        if (corner != b)
        {
            list.Add((corner, b));
        }

        return list;
    }

    // ---------- 指令 ----------

    [RelayCommand]
    private void ChooseTool(EditorTool tool) => SelectTool(tool);

    [RelayCommand]
    private void Place(ComponentKind kind) => SelectTool(EditorTool.Place, kind);

    [RelayCommand]
    private void Rotate()
    {
        if (ActiveTool == EditorTool.Place && Selection.Count == 0)
        {
            _placeOrientation = _placeOrientation.RotateClockwise();
            return;
        }

        if (Selection.Count > 0)
        {
            History.Apply(Document.Rotate(Selection));
        }
    }

    [RelayCommand]
    private void Mirror()
    {
        if (Selection.Count > 0)
        {
            History.Apply(Document.Mirror(Selection));
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selection.Count > 0)
        {
            var count = Selection.Count;
            History.Apply(Document.Remove(Selection));
            SetSelection([]);
            StatusText = $"已刪除 {count} 個元素。";
        }
    }

    [RelayCommand]
    private void Copy()
    {
        _clipboard.Clear();
        _clipboard.AddRange(Document.Elements.Where(e => Selection.Contains(e.Id)));
        StatusText = $"已複製 {_clipboard.Count} 個元素。";
    }

    [RelayCommand]
    private void Paste()
    {
        if (_clipboard.Count == 0)
        {
            return;
        }

        var doc = Document.Duplicate(_clipboard, 2, 2, out var ids);
        History.Apply(doc);
        SetSelection([.. ids]);

        // 下次貼上再往下錯開
        var moved = _clipboard.Select(e => e.Translate(2, 2)).ToList();
        _clipboard.Clear();
        _clipboard.AddRange(moved);
    }

    [RelayCommand]
    private void Undo()
    {
        if (History.Undo())
        {
            PruneSelection();
        }
    }

    [RelayCommand]
    private void Redo()
    {
        if (History.Redo())
        {
            PruneSelection();
        }
    }

    [RelayCommand]
    private void SelectAll() => SetSelection([.. Document.Elements.Select(e => e.Id)]);

    [RelayCommand]
    private void Escape()
    {
        CancelGesture();
        SelectTool(EditorTool.Select);
        SetSelection([]);
    }

    // ---------- 屬性編輯 ----------

    /// <summary>修改元件名稱；名稱重複或空白時拒絕並回傳中文原因。</summary>
    public string? RenameComponent(ElementId id, string newName)
    {
        if (Document.Find(id) is not ComponentSymbol c)
        {
            return "找不到元件。";
        }

        newName = newName.Trim();
        if (newName.Length == 0)
        {
            return "名稱不可空白。";
        }

        if (char.ToUpperInvariant(newName[0]) != ComponentCatalog.Get(c.Kind).Prefix[0])
        {
            return $"{ComponentCatalog.Get(c.Kind).DisplayNameZh}的名稱必須以「{ComponentCatalog.Get(c.Kind).Prefix}」開頭（與網表一致）。";
        }

        if (Document.Components.Any(o => o.Id != id && string.Equals(o.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            return $"名稱「{newName}」已經被使用。";
        }

        if (newName != c.Name)
        {
            History.Apply(Document.Replace(c with { Name = newName }));
        }

        return null;
    }

    public void SetParameter(ElementId id, string key, string value)
    {
        if (Document.Find(id) is ComponentSymbol c && c.Parameter(key) != value)
        {
            History.Apply(Document.Replace(c.WithParameter(key, value)));
        }
    }

    /// <summary>修改網路標籤名稱（同名即相連）。</summary>
    public string? RenameLabel(ElementId id, string newName)
    {
        if (Document.Find(id) is not NetLabel l)
        {
            return "找不到網路標籤。";
        }

        newName = newName.Trim();
        if (newName.Length == 0)
        {
            return "標籤名稱不可空白。";
        }

        if (newName.StartsWith(ConnectivityExtractor.AutoNetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"標籤名稱不可以「{ConnectivityExtractor.AutoNetPrefix}」開頭（保留給自動命名）。";
        }

        if (newName.Any(char.IsWhiteSpace) || newName.IndexOfAny(['(', ')', '=', ',']) >= 0)
        {
            return "標籤名稱不可包含空白、括號、逗號或等號。";
        }

        if (newName != l.NetName)
        {
            History.Apply(Document.Replace(l with { NetName = newName }));
        }

        return null;
    }

    /// <summary>載入新文件（開檔、匯入）：清除歷史。</summary>
    public void LoadDocument(SchematicDocument doc)
    {
        History.Reset(doc);
        SetSelection([]);
    }

    // ---------- 電路檢查 ----------

    /// <summary>執行電路檢查，更新問題清單與圖上的標示。回傳是否沒有問題。</summary>
    public bool RunCheck()
    {
        var extraction = Extraction;
        _issues = CircuitValidator.Validate(extraction.Circuit, extraction.Issues);
        IssueMarkers = BuildMarkers(extraction, _issues);
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(IssueMarkers));
        StatusText = _issues.Count == 0 ? "電路檢查通過。" : $"電路檢查發現 {_issues.Count} 個問題，已在圖上以紅框標示。";
        RaiseVisual();
        return _issues.Count == 0;
    }

    private IReadOnlyList<IssueMarker> BuildMarkers(ExtractionResult extraction, IReadOnlyList<CircuitIssue> issues)
    {
        var markers = new List<IssueMarker>();
        foreach (var issue in issues)
        {
            foreach (var name in issue.ComponentNames)
            {
                if (extraction.ComponentIds.TryGetValue(name, out var id) && Document.Find(id) is { } e)
                {
                    var bounds = HitTester.Bounds(e).Inflate(0.4);
                    if (issue.Code == IssueCode.UnconnectedPin && e is ComponentSymbol c &&
                        c.PinPositions.FirstOrDefault(p => ConnectivityExtractor.PinLabel(c.Kind, p.Name) == issue.PinName) is { Name: not null } pin)
                    {
                        bounds = new WorldRect(pin.Position.X - 0.5, pin.Position.Y - 0.5, pin.Position.X + 0.5, pin.Position.Y + 0.5);
                    }

                    markers.Add(new IssueMarker(bounds, issue.MessageZh, issue.Code, id));
                }
            }

            if (issue.Code is IssueCode.FloatingNode)
            {
                foreach (var node in issue.Nodes)
                {
                    if (extraction.NetPoints.TryGetValue(node, out var points))
                    {
                        foreach (var pt in points)
                        {
                            markers.Add(new IssueMarker(new WorldRect(pt.X - 0.5, pt.Y - 0.5, pt.X + 0.5, pt.Y + 0.5), issue.MessageZh, issue.Code, null));
                        }
                    }
                }
            }
        }

        return markers;
    }

    // ---------- 內部 ----------

    private void SetSelection(ImmutableHashSet<ElementId> selection)
    {
        Selection = selection;
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(SelectedComponent));
        OnPropertyChanged(nameof(SelectedLabel));
        RaiseVisual();
    }

    private void PruneSelection() =>
        SetSelection(Selection.Where(id => Document.Find(id) is not null).ToImmutableHashSet());

    private void OnDocumentChanged()
    {
        _extraction = null;
        if (IssueMarkers.Count > 0 || _issues.Count > 0)
        {
            // 圖面改變後重新檢查，標示才不會停在舊位置
            RunCheck();
        }

        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(SelectedComponent));
        OnPropertyChanged(nameof(SelectedLabel));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        RaiseVisual();
    }

    private void RaiseVisual() => VisualChanged?.Invoke(this, EventArgs.Empty);
}
