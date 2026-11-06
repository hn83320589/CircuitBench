using CircuitBench.Schematic.Geometry;

namespace CircuitBench.App.ViewModels.Editor;

/// <summary>畫布的縮放與平移：螢幕座標 = 世界座標 × Zoom + Offset。</summary>
public sealed class Viewport
{
    public const double MinZoom = 4;
    public const double MaxZoom = 120;

    public double Zoom { get; private set; } = 20;

    public double OffsetX { get; private set; } = 200;

    public double OffsetY { get; private set; } = 200;

    public event EventHandler? Changed;

    public WorldPoint ToWorld(double sx, double sy) => new((sx - OffsetX) / Zoom, (sy - OffsetY) / Zoom);

    public (double X, double Y) ToScreen(WorldPoint w) => (w.X * Zoom + OffsetX, w.Y * Zoom + OffsetY);

    /// <summary>以螢幕上某點為中心縮放（該點下的世界座標保持不動）。</summary>
    public void ZoomAt(double sx, double sy, double factor)
    {
        var anchor = ToWorld(sx, sy);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        OffsetX = sx - anchor.X * Zoom;
        OffsetY = sy - anchor.Y * Zoom;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void PanBy(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Set(double zoom, double offsetX, double offsetY)
    {
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        OffsetX = offsetX;
        OffsetY = offsetY;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>讓世界矩形完整顯示在指定大小的畫面中。</summary>
    public void Fit(WorldRect rect, double width, double height)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || width <= 0 || height <= 0)
        {
            return;
        }

        var zoom = Math.Clamp(Math.Min(width / rect.Width, height / rect.Height) * 0.9, MinZoom, MaxZoom);
        Set(zoom, width / 2 - (rect.Left + rect.Width / 2) * zoom, height / 2 - (rect.Top + rect.Height / 2) * zoom);
    }
}
