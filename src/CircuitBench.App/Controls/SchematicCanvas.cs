using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CircuitBench.App.ViewModels.Editor;
using CircuitBench.Schematic.Connectivity;
using CircuitBench.Schematic.Editing;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;

namespace CircuitBench.App.Controls;

/// <summary>
/// 電路圖畫布（自訂繪製）。只負責把螢幕事件轉成世界座標交給 <see cref="EditorViewModel"/>，
/// 以及依 ViewModel 狀態繪圖；不含任何編輯邏輯。
/// </summary>
public sealed class SchematicCanvas : Control
{
    public static readonly StyledProperty<EditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<SchematicCanvas, EditorViewModel?>(nameof(Editor));

    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(225, 228, 235)), 1);
    private static readonly IPen SymbolPen = new Pen(new SolidColorBrush(Color.FromRgb(26, 26, 26)), 2, lineCap: PenLineCap.Round);
    private static readonly IPen SelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(230, 120, 0)), 2.5, lineCap: PenLineCap.Round);
    private static readonly IPen WirePen = new Pen(new SolidColorBrush(Color.FromRgb(31, 95, 191)), 2, lineCap: PenLineCap.Round);
    private static readonly IPen PreviewPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 31, 95, 191)), 2, new DashStyle([4, 3], 0));
    private static readonly IPen IssuePen = new Pen(Brushes.Red, 2, new DashStyle([4, 3], 0));
    private static readonly IPen BoxPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 120, 220)), 1, new DashStyle([3, 3], 0));
    private static readonly IBrush JunctionBrush = new SolidColorBrush(Color.FromRgb(31, 95, 191));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(170, 40, 40));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40));
    private static readonly IBrush BoxFill = new SolidColorBrush(Color.FromArgb(30, 30, 120, 220));
    private static readonly Typeface Font = new("Microsoft JhengHei");

    private bool _middlePanning;
    private Point _lastPan;

    static SchematicCanvas()
    {
        AffectsRender<SchematicCanvas>(EditorProperty);
        FocusableProperty.OverrideDefaultValue<SchematicCanvas>(true);
    }

    public SchematicCanvas()
    {
        ClipToBounds = true;
    }

    public EditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            if (change.OldValue is EditorViewModel old)
            {
                old.VisualChanged -= OnVisualChanged;
                old.Viewport.Changed -= OnVisualChanged;
            }

            if (change.NewValue is EditorViewModel vm)
            {
                vm.VisualChanged += OnVisualChanged;
                vm.Viewport.Changed += OnVisualChanged;
            }
        }
    }

    private void OnVisualChanged(object? sender, EventArgs e) => InvalidateVisual();

    private WorldPoint ToWorld(Point p) => Editor!.Viewport.ToWorld(p.X, p.Y);

    private static PointerModifiers Mods(KeyModifiers k) =>
        (k.HasFlag(KeyModifiers.Shift) ? PointerModifiers.Shift : PointerModifiers.None) |
        (k.HasFlag(KeyModifiers.Control) ? PointerModifiers.Control : PointerModifiers.None);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Editor is null)
        {
            return;
        }

        Focus();
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
        {
            _middlePanning = true;
            _lastPan = point.Position;
            return;
        }

        Editor.PointerPressed(ToWorld(point.Position), Mods(e.KeyModifiers));
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Editor is null)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (_middlePanning)
        {
            Editor.Viewport.PanBy(p.X - _lastPan.X, p.Y - _lastPan.Y);
            _lastPan = p;
            return;
        }

        Editor.PointerMoved(ToWorld(p), Mods(e.KeyModifiers));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Editor is null)
        {
            return;
        }

        if (_middlePanning)
        {
            _middlePanning = false;
            return;
        }

        Editor.PointerReleased(ToWorld(e.GetPosition(this)), Mods(e.KeyModifiers));
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Editor is null)
        {
            return;
        }

        var p = e.GetPosition(this);
        Editor.Viewport.ZoomAt(p.X, p.Y, e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (Editor is not { } vm)
        {
            return;
        }

        var vp = vm.Viewport;
        DrawGrid(context, vp);

        var doc = vm.Document;
        var selected = vm.Selection;
        var drag = vm.DragOffset;

        foreach (var w in doc.Wires)
        {
            var isSel = selected.Contains(w.Id);
            var off = isSel ? drag : default;
            context.DrawLine(isSel ? SelectedPen : WirePen, P(vp, w.A + off), P(vp, w.B + off));
        }

        foreach (var e in doc.Elements)
        {
            var isSel = selected.Contains(e.Id);
            var off = isSel ? drag : default;
            var pen = isSel ? SelectedPen : SymbolPen;
            switch (e)
            {
                case ComponentSymbol c:
                    DrawPrimitives(context, vp, SymbolGeometry.For(c.Kind), c.Position + off, c.Orientation, pen);
                    DrawComponentText(context, vp, c, off);
                    break;
                case GroundSymbol g:
                    DrawPrimitives(context, vp, SymbolGeometry.Ground(), g.Position + off, Orientation.Default, pen);
                    break;
                case NetLabel l:
                {
                    var pos = P(vp, l.Position + off);
                    context.DrawEllipse(LabelBrush, null, pos, 2.5, 2.5);
                    DrawText(context, l.NetName, new Point(pos.X + 3, pos.Y - vp.Zoom * 0.9), LabelBrush, Math.Max(10, vp.Zoom * 0.6), isSel);
                    break;
                }
            }
        }

        foreach (var j in vm.Extraction.Junctions)
        {
            context.DrawEllipse(JunctionBrush, null, P(vp, j), Math.Max(3, vp.Zoom * 0.18), Math.Max(3, vp.Zoom * 0.18));
        }

        foreach (var (a, b) in vm.WirePreview)
        {
            context.DrawLine(PreviewPen, P(vp, a), P(vp, b));
        }

        if (vm.ActiveTool == EditorTool.Place && vm.HoverPoint is { } hover)
        {
            DrawPrimitives(context, vp, SymbolGeometry.For(vm.PlaceKind), hover, Orientation.Default, PreviewPen);
        }

        foreach (var m in vm.IssueMarkers)
        {
            var (x1, y1) = vp.ToScreen(new WorldPoint(m.Bounds.Left, m.Bounds.Top));
            var (x2, y2) = vp.ToScreen(new WorldPoint(m.Bounds.Right, m.Bounds.Bottom));
            context.DrawRectangle(null, IssuePen, new Rect(new Point(x1, y1), new Point(x2, y2)));
        }

        if (vm.SelectionBox is { } box)
        {
            var (x1, y1) = vp.ToScreen(new WorldPoint(box.Left, box.Top));
            var (x2, y2) = vp.ToScreen(new WorldPoint(box.Right, box.Bottom));
            context.DrawRectangle(BoxFill, BoxPen, new Rect(new Point(x1, y1), new Point(x2, y2)));
        }
    }

    private void DrawGrid(DrawingContext context, Viewport vp)
    {
        if (vp.Zoom < 6)
        {
            return;
        }

        var tl = vp.ToWorld(0, 0);
        var br = vp.ToWorld(Bounds.Width, Bounds.Height);
        for (var x = Math.Floor(tl.X); x <= br.X; x++)
        {
            for (var y = Math.Floor(tl.Y); y <= br.Y; y++)
            {
                var (sx, sy) = vp.ToScreen(new WorldPoint(x, y));
                context.DrawLine(GridPen, new Point(sx - 0.5, sy), new Point(sx + 0.5, sy));
            }
        }
    }

    private static Point P(Viewport vp, GridPoint g)
    {
        var (x, y) = vp.ToScreen(g.ToWorld());
        return new Point(x, y);
    }

    private static void DrawPrimitives(DrawingContext context, Viewport vp, IReadOnlyList<SymbolPrimitive> primitives, GridPoint origin, Orientation o, IPen pen)
    {
        Point T(WorldPoint w)
        {
            var r = o.Apply(w);
            var (x, y) = vp.ToScreen(new WorldPoint(origin.X + r.X, origin.Y + r.Y));
            return new Point(x, y);
        }

        foreach (var p in primitives)
        {
            switch (p)
            {
                case LinePrimitive l:
                    context.DrawLine(pen, T(l.A), T(l.B));
                    break;
                case PolylinePrimitive pl:
                {
                    var geometry = new StreamGeometry();
                    using (var g = geometry.Open())
                    {
                        g.BeginFigure(T(pl.Points[0]), false);
                        for (var i = 1; i < pl.Points.Count; i++)
                        {
                            g.LineTo(T(pl.Points[i]));
                        }

                        g.EndFigure(pl.Closed);
                    }

                    context.DrawGeometry(null, pen, geometry);
                    break;
                }

                case CirclePrimitive c:
                    context.DrawEllipse(c.Filled ? pen.Brush : null, pen, T(c.Center), c.Radius * vp.Zoom, c.Radius * vp.Zoom);
                    break;
            }
        }
    }

    private static void DrawComponentText(DrawingContext context, Viewport vp, ComponentSymbol c, GridPoint off)
    {
        var bounds = HitTester.Bounds(c);
        var size = Math.Max(10, vp.Zoom * 0.55);
        var value = c.Kind switch
        {
            ComponentKind.VoltageSource or ComponentKind.CurrentSource => ComponentFactory.SourceText(c),
            ComponentKind.Diode => string.Empty,
            ComponentKind.Switch => $"t={c.Parameter("times")} {c.Parameter("initial")}",
            ComponentKind.Vcvs or ComponentKind.Vccs => c.Parameter("gain"),
            _ => c.Parameter("value"),
        };
        var name = Format(c.Name, size, bold: true);
        var val = Format(value, size * 0.9, bold: false);
        if (bounds.Width > bounds.Height)
        {
            // 水平元件：名稱與數值置中放在本體上方
            var (cx, top) = vp.ToScreen(new WorldPoint((bounds.Left + bounds.Right) / 2 + off.X, bounds.Top + off.Y));
            var lines = value.Length > 0 ? name.Height + val.Height : name.Height;
            var y = top - lines - 2;
            context.DrawText(name, new Point(cx - name.Width / 2, y));
            context.DrawText(val, new Point(cx - val.Width / 2, y + name.Height));
            return;
        }

        // 垂直元件：電源的文字放左側（通常右側接電路），其餘放右側
        var isSource = c.Kind is ComponentKind.VoltageSource or ComponentKind.CurrentSource;
        var (lx, ty) = vp.ToScreen(new WorldPoint(bounds.Left + off.X - 0.2, bounds.Top + off.Y + 0.6));
        var (rx, _) = vp.ToScreen(new WorldPoint(bounds.Right + off.X + 0.2, 0));
        context.DrawText(name, new Point(isSource ? lx - name.Width : rx, ty));
        context.DrawText(val, new Point(isSource ? lx - val.Width : rx, ty + name.Height));
    }

    private static FormattedText Format(string text, double size, bool bold) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Font.FontFamily, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal), size, TextBrush);

    private static void DrawText(DrawingContext context, string text, Point at, IBrush brush, double size, bool bold)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Font.FontFamily, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal), size, brush);
        context.DrawText(ft, at);
    }
}
