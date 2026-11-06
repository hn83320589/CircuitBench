using System.Globalization;
using System.Security;
using System.Text;

namespace CircuitBench.App.ViewModels.Results;

/// <summary>把目前的波形畫面（範圍、曲線、軸刻度）匯出成 SVG。使用與螢幕相同的最小/最大抽樣。</summary>
public static class WaveformSvgExporter
{
    public static readonly string[] Palette = ["#1f77b4", "#d62728", "#2ca02c", "#9467bd", "#ff7f0e", "#17becf", "#8c564b", "#e377c2"];

    public static string Export(WaveformViewModel vm, int width = 900, int height = 500)
    {
        const int left = 80, right = 80, top = 20, bottom = 50;
        var plotW = width - left - right;
        var plotH = height - top - bottom;
        var sb = new StringBuilder();
        sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">""");
        sb.AppendLine("""<rect width="100%" height="100%" fill="white"/>""");
        sb.AppendLine($"""<rect x="{left}" y="{top}" width="{plotW}" height="{plotH}" fill="none" stroke="#888"/>""");
        sb.AppendLine("""<g font-family="Microsoft JhengHei, sans-serif" font-size="11" fill="#333">""");

        double Sx(double x) => left + (x - vm.XMin) / (vm.XMax - vm.XMin) * plotW;
        foreach (var t in vm.XTicks())
        {
            var x = Sx(t.Value);
            sb.AppendLine($"""<line x1="{F(x)}" y1="{top}" x2="{F(x)}" y2="{top + plotH}" stroke="#eee"/>""");
            sb.AppendLine($"""<text x="{F(x)}" y="{top + plotH + 16}" text-anchor="middle">{Escape(t.Label)}</text>""");
        }

        sb.AppendLine($"""<text x="{left + plotW / 2}" y="{height - 8}" text-anchor="middle">{Escape(vm.XLabel)}</text>""");

        var axisIndex = 0;
        foreach (var axis in vm.Axes.Values)
        {
            var onLeft = axisIndex == 0;
            var ax = onLeft ? left : left + plotW;
            foreach (var t in axis.Ticks(6))
            {
                var y = top + plotH - (t.Value - axis.Min) / (axis.Max - axis.Min) * plotH;
                sb.AppendLine($"""<text x="{F(onLeft ? ax - 6 : ax + 6)}" y="{F(y + 4)}" text-anchor="{(onLeft ? "end" : "start")}">{Escape(t.Label)}</text>""");
            }

            axisIndex++;
        }

        sb.AppendLine("</g>");

        foreach (var trace in vm.Traces)
        {
            var axis = vm.Axes[trace.Unit];
            double Sy(double v) => top + plotH - (v - axis.Min) / (axis.Max - axis.Min) * plotH;
            var cols = vm.Columns(trace, plotW);
            var points = new StringBuilder();
            var lastY = 0.0;
            for (var i = 0; i < cols.Length; i++)
            {
                if (!cols[i].HasData)
                {
                    continue;
                }

                // 先連到離上一點較近的一端（與畫面繪製相同），避免邊緣處出現假尖刺
                var x = left + i + 0.5;
                var yMax = Sy(cols[i].Max);
                var yMin = Sy(cols[i].Min);
                var (first, second) = Math.Abs(yMax - lastY) <= Math.Abs(yMin - lastY) ? (yMax, yMin) : (yMin, yMax);
                points.Append(CultureInfo.InvariantCulture, $"{F(x)},{F(first)} {F(x)},{F(second)} ");
                lastY = second;
            }

            var color = Palette[trace.ColorIndex % Palette.Length];
            sb.AppendLine($"""<polyline points="{points.ToString().Trim()}" fill="none" stroke="{color}" stroke-width="1.5"/>""");
        }

        // 圖例
        for (var i = 0; i < vm.Traces.Count; i++)
        {
            var color = Palette[vm.Traces[i].ColorIndex % Palette.Length];
            sb.AppendLine($"""<text x="{left + 8}" y="{top + 16 + i * 14}" fill="{color}" font-size="12" font-family="sans-serif">{Escape(vm.Traces[i].Label)}</text>""");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string s) => SecurityElement.Escape(s) ?? string.Empty;
}
