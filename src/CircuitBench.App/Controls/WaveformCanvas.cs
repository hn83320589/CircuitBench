using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CircuitBench.App.ViewModels.Results;

namespace CircuitBench.App.Controls;

/// <summary>
/// 波形繪圖控制項。每次重繪只向 ViewModel 取「每個像素欄的最小值與最大值」，
/// 因此 100 萬點也只畫約 2×寬度 個頂點，且任何縮放都不遺漏尖峰。
/// 滑鼠：滾輪縮放 X（Ctrl+滾輪縮放 Y）、左鍵拖曳平移、Shift+左鍵設游標 A、右鍵設游標 B、雙擊自動縮放。
/// </summary>
public sealed class WaveformCanvas : Control
{
    public static readonly StyledProperty<WaveformViewModel?> WaveformProperty =
        AvaloniaProperty.Register<WaveformCanvas, WaveformViewModel?>(nameof(Waveform));

    private const double LeftMargin = 70;
    private const double RightMargin = 70;
    private const double TopMargin = 12;
    private const double BottomMargin = 36;

    private static readonly IBrush[] Brushes = WaveformSvgExporter.Palette.Select(c => (IBrush)new SolidColorBrush(Color.Parse(c))).ToArray();
    private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.FromRgb(140, 140, 140)), 1);
    private static readonly IPen GridLinePen = new Pen(new SolidColorBrush(Color.FromRgb(232, 232, 236)), 1);
    private static readonly IPen CursorAPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 150, 80)), 1.5, new DashStyle([5, 3], 0));
    private static readonly IPen CursorBPen = new Pen(new SolidColorBrush(Color.FromRgb(200, 0, 120)), 1.5, new DashStyle([5, 3], 0));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
    private static readonly Typeface Font = new("Microsoft JhengHei");

    private Point? _dragStart;
    private double _dragXMin;

    static WaveformCanvas()
    {
        AffectsRender<WaveformCanvas>(WaveformProperty);
    }

    public WaveformCanvas()
    {
        ClipToBounds = true;
    }

    public WaveformViewModel? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    /// <summary>最近一次重繪耗時（毫秒），顯示於畫面角落供效能檢查。</summary>
    public double LastRenderMilliseconds { get; private set; }

    private Rect PlotRect => new(LeftMargin, TopMargin,
        Math.Max(1, Bounds.Width - LeftMargin - RightMargin), Math.Max(1, Bounds.Height - TopMargin - BottomMargin));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WaveformProperty)
        {
            if (change.OldValue is WaveformViewModel old)
            {
                old.Changed -= OnChanged;
            }

            if (change.NewValue is WaveformViewModel vm)
            {
                vm.Changed += OnChanged;
            }
        }
    }

    private void OnChanged(object? sender, EventArgs e) => InvalidateVisual();

    private double XAt(double sx)
    {
        var r = PlotRect;
        return Waveform!.XMin + (sx - r.Left) / r.Width * (Waveform.XMax - Waveform.XMin);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Waveform is not { HasData: true } vm)
        {
            return;
        }

        var factor = e.Delta.Y > 0 ? 0.8 : 1.25;
        var p = e.GetPosition(this);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            foreach (var axis in vm.Axes.Values)
            {
                vm.ZoomY(axis.Unit, (axis.Min + axis.Max) / 2, factor);
            }
        }
        else
        {
            vm.ZoomX(XAt(p.X), factor);
        }

        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Waveform is not { HasData: true } vm)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (e.ClickCount == 2)
        {
            vm.AutoScale();
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            vm.CursorB = XAt(point.Position.X);
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            vm.CursorA = XAt(point.Position.X);
            return;
        }

        _dragStart = point.Position;
        _dragXMin = vm.XMin;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start || Waveform is not { } vm)
        {
            return;
        }

        var dx = (e.GetPosition(this).X - start.X) / PlotRect.Width * (vm.XMax - vm.XMin);
        vm.PanX(_dragXMin - dx - vm.XMin);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragStart = null;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext context)
    {
        var start = TimeProvider.System.GetTimestamp();
        context.FillRectangle(Avalonia.Media.Brushes.White, new Rect(Bounds.Size));
        var r = PlotRect;
        if (Waveform is not { HasData: true } vm)
        {
            DrawText(context, "尚無波形：請在「電路圖」頁執行直流掃描或暫態分析。", new Point(r.Left + 10, r.Top + 10), LabelBrush, 14);
            return;
        }

        // X 軸格線與刻度
        foreach (var t in vm.XTicks())
        {
            var x = r.Left + (t.Value - vm.XMin) / (vm.XMax - vm.XMin) * r.Width;
            context.DrawLine(GridLinePen, new Point(x, r.Top), new Point(x, r.Bottom));
            DrawText(context, t.Label, new Point(x - 20, r.Bottom + 4), LabelBrush, 11);
        }

        DrawText(context, vm.XLabel, new Point(r.Left + r.Width / 2 - 30, r.Bottom + 18), LabelBrush, 11);

        // Y 軸：第一軸在左、第二軸在右
        var axisIndex = 0;
        foreach (var axis in vm.Axes.Values)
        {
            var onLeft = axisIndex == 0;
            foreach (var t in axis.Ticks(6))
            {
                var y = r.Bottom - (t.Value - axis.Min) / (axis.Max - axis.Min) * r.Height;
                if (onLeft)
                {
                    context.DrawLine(GridLinePen, new Point(r.Left, y), new Point(r.Right, y));
                }

                DrawText(context, t.Label, new Point(onLeft ? 4 : r.Right + 6, y - 7), LabelBrush, 11);
            }

            axisIndex++;
        }

        context.DrawRectangle(null, FramePen, r);

        // 曲線：每個像素欄畫出 min–max 垂直線，並連接相鄰欄
        using (context.PushClip(r))
        {
            var width = (int)Math.Max(1, Math.Round(r.Width));
            foreach (var trace in vm.Traces)
            {
                var axis = vm.Axes[trace.Unit];
                var span = axis.Max - axis.Min;
                double Sy(double v) => r.Bottom - (v - axis.Min) / span * r.Height;
                var cols = vm.Columns(trace, width);
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    var started = false;
                    var lastY = 0.0;
                    for (var i = 0; i < cols.Length; i++)
                    {
                        if (!cols[i].HasData)
                        {
                            continue;
                        }

                        // 每欄畫一條 min–max 垂直線；先連到離上一點較近的一端，避免邊緣處出現假尖刺
                        var x = r.Left + i + 0.5;
                        var yMax = Sy(cols[i].Max);
                        var yMin = Sy(cols[i].Min);
                        var (first, second) = Math.Abs(yMax - lastY) <= Math.Abs(yMin - lastY) ? (yMax, yMin) : (yMin, yMax);
                        if (!started)
                        {
                            g.BeginFigure(new Point(x, first), false);
                            started = true;
                        }
                        else
                        {
                            g.LineTo(new Point(x, first));
                        }

                        g.LineTo(new Point(x, second));
                        lastY = second;
                    }

                    if (started)
                    {
                        g.EndFigure(false);
                    }
                }

                context.DrawGeometry(null, new Pen(Brushes[trace.ColorIndex % Brushes.Length], 1.5), geometry);
            }

            DrawCursor(context, vm, vm.CursorA, CursorAPen, "A");
            DrawCursor(context, vm, vm.CursorB, CursorBPen, "B");
        }

        for (var i = 0; i < vm.Traces.Count; i++)
        {
            DrawText(context, vm.Traces[i].Label, new Point(r.Left + 8, r.Top + 6 + i * 16), Brushes[vm.Traces[i].ColorIndex % Brushes.Length], 12);
        }

        LastRenderMilliseconds = TimeProvider.System.GetElapsedTime(start).TotalMilliseconds;
        DrawText(context, $"重繪 {LastRenderMilliseconds:F1} ms", new Point(r.Right - 90, r.Top + 4), LabelBrush, 10);
    }

    private void DrawCursor(DrawingContext context, WaveformViewModel vm, double xValue, IPen pen, string name)
    {
        var r = PlotRect;
        var x = r.Left + (xValue - vm.XMin) / (vm.XMax - vm.XMin) * r.Width;
        if (x < r.Left || x > r.Right)
        {
            return;
        }

        context.DrawLine(pen, new Point(x, r.Top), new Point(x, r.Bottom));
        DrawText(context, name, new Point(x + 3, r.Bottom - 18), pen.Brush!, 12);
    }

    private static void DrawText(DrawingContext context, string text, Point at, IBrush brush, double size)
    {
        context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, brush), at);
    }
}
