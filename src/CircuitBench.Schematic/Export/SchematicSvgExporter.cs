using System.Globalization;
using System.Security;
using System.Text;
using CircuitBench.Schematic.Connectivity;
using CircuitBench.Schematic.Editing;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;

namespace CircuitBench.Schematic.Export;

/// <summary>電路圖匯出為 SVG（與畫布使用相同的符號幾何）。</summary>
public static class SchematicSvgExporter
{
    private const double Scale = 20;
    private const double Margin = 2;

    public static string Export(SchematicDocument doc, ExtractionResult? extraction = null)
    {
        var bounds = doc.Elements.Count == 0
            ? new WorldRect(0, 0, 10, 10)
            : doc.Elements.Select(HitTester.Bounds).Aggregate((a, b) => a.Union(b)).Inflate(Margin);
        var width = bounds.Width * Scale;
        var height = bounds.Height * Scale;
        var sb = new StringBuilder();
        sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{F(width)}" height="{F(height)}" viewBox="0 0 {F(width)} {F(height)}">""");
        sb.AppendLine("""<rect width="100%" height="100%" fill="white"/>""");
        sb.AppendLine("""<g stroke="#1a1a1a" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round">""");

        double X(double x) => (x - bounds.Left) * Scale;
        double Y(double y) => (y - bounds.Top) * Scale;

        void Primitive(SymbolPrimitive p, GridPoint origin, Orientation o)
        {
            WorldPoint T(WorldPoint w)
            {
                var r = o.Apply(w);
                return new WorldPoint(origin.X + r.X, origin.Y + r.Y);
            }

            switch (p)
            {
                case LinePrimitive l:
                {
                    var a = T(l.A);
                    var b = T(l.B);
                    sb.AppendLine($"""<line x1="{F(X(a.X))}" y1="{F(Y(a.Y))}" x2="{F(X(b.X))}" y2="{F(Y(b.Y))}"/>""");
                    break;
                }

                case PolylinePrimitive pl:
                {
                    var pts = string.Join(" ", pl.Points.Select(T).Select(w => $"{F(X(w.X))},{F(Y(w.Y))}"));
                    sb.AppendLine(pl.Closed ? $"""<polygon points="{pts}"/>""" : $"""<polyline points="{pts}"/>""");
                    break;
                }

                case CirclePrimitive c:
                {
                    var ctr = T(c.Center);
                    sb.AppendLine($"""<circle cx="{F(X(ctr.X))}" cy="{F(Y(ctr.Y))}" r="{F(c.Radius * Scale)}"{(c.Filled ? " fill=\"#1a1a1a\"" : string.Empty)}/>""");
                    break;
                }
            }
        }

        foreach (var w in doc.Wires)
        {
            sb.AppendLine($"""<line x1="{F(X(w.A.X))}" y1="{F(Y(w.A.Y))}" x2="{F(X(w.B.X))}" y2="{F(Y(w.B.Y))}" stroke="#1f5fbf"/>""");
        }

        foreach (var c in doc.Components)
        {
            foreach (var p in SymbolGeometry.For(c.Kind))
            {
                Primitive(p, c.Position, c.Orientation);
            }
        }

        foreach (var g in doc.Elements.OfType<GroundSymbol>())
        {
            foreach (var p in SymbolGeometry.Ground())
            {
                Primitive(p, g.Position, Orientation.Default);
            }
        }

        sb.AppendLine("</g>");

        // 接點
        foreach (var j in extraction?.Junctions ?? [])
        {
            sb.AppendLine($"""<circle cx="{F(X(j.X))}" cy="{F(Y(j.Y))}" r="4" fill="#1f5fbf"/>""");
        }

        // 文字：元件名稱與主要參數、網路標籤
        sb.AppendLine("""<g font-family="Microsoft JhengHei, sans-serif" font-size="12" fill="#1a1a1a">""");
        foreach (var c in doc.Components)
        {
            var b = HitTester.Bounds(c);
            var value = c.Kind switch
            {
                ComponentKind.VoltageSource or ComponentKind.CurrentSource => ComponentFactory.SourceText(c),
                ComponentKind.Switch => $"t={c.Parameter("times")}",
                ComponentKind.Diode => string.Empty,
                _ => c.Parameter(c.Kind is ComponentKind.Vcvs or ComponentKind.Vccs ? "gain" : "value"),
            };
            sb.AppendLine($"""<text x="{F(X(b.Right) + 4)}" y="{F(Y(b.Top) + 10)}">{Escape(c.Name)}</text>""");
            if (value.Length > 0)
            {
                sb.AppendLine($"""<text x="{F(X(b.Right) + 4)}" y="{F(Y(b.Top) + 24)}" fill="#555">{Escape(value)}</text>""");
            }
        }

        foreach (var l in doc.Elements.OfType<NetLabel>())
        {
            sb.AppendLine($"""<text x="{F(X(l.Position.X) + 2)}" y="{F(Y(l.Position.Y) - 4)}" fill="#a33">{Escape(l.NetName)}</text>""");
        }

        sb.AppendLine("</g>");

        // 檢查結果標示
        if (extraction is not null)
        {
            foreach (var issue in extraction.Issues)
            {
                foreach (var name in issue.ComponentNames)
                {
                    if (extraction.ComponentIds.TryGetValue(name, out var id) && doc.Find(id) is { } e)
                    {
                        var b = HitTester.Bounds(e).Inflate(0.5);
                        sb.AppendLine($"""<rect x="{F(X(b.Left))}" y="{F(Y(b.Top))}" width="{F(b.Width * Scale)}" height="{F(b.Height * Scale)}" fill="none" stroke="#d00" stroke-dasharray="4 3"/>""");
                    }
                }
            }
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string s) => SecurityElement.Escape(s) ?? string.Empty;
}
