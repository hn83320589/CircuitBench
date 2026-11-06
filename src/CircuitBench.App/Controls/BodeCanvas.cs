using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CircuitBench.App.ViewModels.Results;
using CircuitBench.Core.Bode;

namespace CircuitBench.App.Controls;

/// <summary>波德圖：上半部振幅 (dB)、下半部相位（度，已展開），共用對數頻率軸。</summary>
public sealed class BodeCanvas : Control
{
    public static readonly StyledProperty<BodeViewModel?> BodeProperty =
        AvaloniaProperty.Register<BodeCanvas, BodeViewModel?>(nameof(Bode));

    private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.FromRgb(140, 140, 140)), 1);
    private static readonly IPen MajorPen = new Pen(new SolidColorBrush(Color.FromRgb(215, 215, 222)), 1);
    private static readonly IPen MinorPen = new Pen(new SolidColorBrush(Color.FromRgb(240, 240, 244)), 1);
    private static readonly IPen MagPen = new Pen(new SolidColorBrush(Color.FromRgb(31, 119, 180)), 1.8);
    private static readonly IPen PhasePen = new Pen(new SolidColorBrush(Color.FromRgb(214, 39, 40)), 1.8);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
    private static readonly Typeface Font = new("Microsoft JhengHei");

    static BodeCanvas()
    {
        AffectsRender<BodeCanvas>(BodeProperty);
    }

    public BodeViewModel? Bode
    {
        get => GetValue(BodeProperty);
        set => SetValue(BodeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BodeProperty)
        {
            if (change.OldValue is BodeViewModel old) old.Changed -= OnChanged;
            if (change.NewValue is BodeViewModel vm) vm.Changed += OnChanged;
        }
    }

    private void OnChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (Bode is not { HasData: true } vm || vm.Frequencies.Length < 2)
        {
            Text(context, "尚無交流分析結果：請在「電路圖」頁執行交流分析。", new Point(20, 20), 14);
            return;
        }

        const double left = 70, right = 20, top = 16, gap = 36, bottom = 36;
        var h = (Bounds.Height - top - gap - bottom) / 2;
        var w = Math.Max(1, Bounds.Width - left - right);
        var magRect = new Rect(left, top, w, Math.Max(1, h));
        var phRect = new Rect(left, top + h + gap, w, Math.Max(1, h));
        var f = vm.Frequencies;
        var lf0 = Math.Log10(f[0]);
        var lf1 = Math.Log10(f[^1]);
        double Sx(double freq) => left + (Math.Log10(freq) - lf0) / (lf1 - lf0) * w;

        DrawPanel(context, magRect, vm.FrequencyTicks, Sx, vm.MagnitudeRange, 10, "dB", vm.MagnitudeDb, f, MagPen, "振幅 (dB)");
        DrawPanel(context, phRect, vm.FrequencyTicks, Sx, vm.PhaseRange, 45, "°", vm.PhaseDegrees, f, PhasePen, "相位 (度)");
        foreach (var t in vm.FrequencyTicks.Where(t => t.IsMajor))
        {
            Text(context, t.Label, new Point(Math.Min(Sx(t.Value) - 18, Bounds.Width - 50), phRect.Bottom + 4), 11);
        }

        Text(context, "頻率（對數）", new Point(left + w / 2 - 30, phRect.Bottom + 18), 11);
    }

    private static void DrawPanel(DrawingContext context, Rect r, IReadOnlyList<Tick> fTicks, Func<double, double> sx,
        (double Min, double Max) range, double step, string unit, double[] values, double[] f, IPen pen, string title)
    {
        foreach (var t in fTicks)
        {
            var x = sx(t.Value);
            context.DrawLine(t.IsMajor ? MajorPen : MinorPen, new Point(x, r.Top), new Point(x, r.Bottom));
        }

        double Sy(double v) => r.Bottom - (v - range.Min) / (range.Max - range.Min) * r.Height;
        var stride = Math.Max(step, Math.Ceiling((range.Max - range.Min) / step / 8) * step);
        for (var v = range.Min; v <= range.Max + 1e-9; v += stride)
        {
            context.DrawLine(MajorPen, new Point(r.Left, Sy(v)), new Point(r.Right, Sy(v)));
            Text(context, v.ToString("0.#", CultureInfo.InvariantCulture) + unit, new Point(4, Sy(v) - 7), 11);
        }

        context.DrawRectangle(null, FramePen, r);
        Text(context, title, new Point(r.Left + 6, r.Top + 4), 12);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var started = false;
            for (var i = 0; i < values.Length; i++)
            {
                if (!double.IsFinite(values[i]))
                {
                    continue;
                }

                var p = new Point(sx(f[i]), Sy(values[i]));
                if (!started)
                {
                    g.BeginFigure(p, false);
                    started = true;
                }
                else
                {
                    g.LineTo(p);
                }
            }

            if (started)
            {
                g.EndFigure(false);
            }
        }

        using (context.PushClip(r))
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private static void Text(DrawingContext context, string text, Point at, double size) =>
        context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, size, LabelBrush), at);
}
