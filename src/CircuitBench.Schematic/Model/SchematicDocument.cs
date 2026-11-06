using System.Collections.Immutable;
using CircuitBench.Schematic.Geometry;

namespace CircuitBench.Schematic.Model;

/// <summary>
/// 不可變的圖面文件。所有編輯操作都回傳新文件，因此復原/重做只需保存快照
/// （ImmutableList 共用結構，100 層以上的成本很低），且復原後圖面與網表必然回到原狀。
/// 元素保持插入順序（繪製與點選以後加入者在上）。
/// </summary>
public sealed record SchematicDocument(ImmutableList<SchematicElement> Elements, int NextId)
{
    public static SchematicDocument Empty { get; } = new(ImmutableList<SchematicElement>.Empty, 1);

    public SchematicElement? Find(ElementId id) => Elements.FirstOrDefault(e => e.Id == id);

    public IEnumerable<ComponentSymbol> Components => Elements.OfType<ComponentSymbol>();

    public IEnumerable<Wire> Wires => Elements.OfType<Wire>();

    /// <summary>加入元素並配發新的識別碼。</summary>
    public SchematicDocument Add(SchematicElement element, out ElementId id)
    {
        id = new ElementId(NextId);
        return new SchematicDocument(Elements.Add(element.WithId(id)), NextId + 1);
    }

    public SchematicDocument Add(SchematicElement element) => Add(element, out _);

    public SchematicDocument AddRange(IEnumerable<SchematicElement> elements, out IReadOnlyList<ElementId> ids)
    {
        var doc = this;
        var list = new List<ElementId>();
        foreach (var e in elements)
        {
            doc = doc.Add(e, out var id);
            list.Add(id);
        }

        ids = list;
        return doc;
    }

    public SchematicDocument Remove(IEnumerable<ElementId> ids)
    {
        var set = ids.ToHashSet();
        return this with { Elements = Elements.RemoveAll(e => set.Contains(e.Id)) };
    }

    public SchematicDocument Replace(SchematicElement element)
    {
        var index = Elements.FindIndex(e => e.Id == element.Id);
        if (index < 0)
        {
            throw new ArgumentException($"找不到元素 {element.Id}。");
        }

        return this with { Elements = Elements.SetItem(index, element) };
    }

    public SchematicDocument Move(IEnumerable<ElementId> ids, int dx, int dy) =>
        Transform(ids, e => e.Translate(dx, dy));

    /// <summary>以選取範圍的中心格點順時針旋轉 90°。</summary>
    public SchematicDocument Rotate(IEnumerable<ElementId> ids)
    {
        var set = ids.ToHashSet();
        var center = SelectionCenter(set);
        return Transform(set, e => e.RotateAround(center));
    }

    public SchematicDocument Mirror(IEnumerable<ElementId> ids)
    {
        var set = ids.ToHashSet();
        var center = SelectionCenter(set);
        return Transform(set, e => e.MirrorAround(center.X));
    }

    /// <summary>複製一組元素（新的識別碼、位移，元件名稱自動改成不重複）。</summary>
    public SchematicDocument Duplicate(IEnumerable<SchematicElement> source, int dx, int dy, out IReadOnlyList<ElementId> newIds)
    {
        var doc = this;
        var ids = new List<ElementId>();
        foreach (var e in source)
        {
            var moved = e.Translate(dx, dy);
            if (moved is ComponentSymbol c)
            {
                moved = c with { Name = doc.NextComponentName(c.Kind) };
            }

            doc = doc.Add(moved, out var id);
            ids.Add(id);
        }

        newIds = ids;
        return doc;
    }

    /// <summary>此類型尚未使用的最小名稱，例如 R3。</summary>
    public string NextComponentName(ComponentKind kind)
    {
        var prefix = ComponentCatalog.Get(kind).Prefix;
        var used = Components.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++)
        {
            var name = $"{prefix}{i}";
            if (!used.Contains(name))
            {
                return name;
            }
        }
    }

    /// <summary>內容相等（元素順序與內容都相同）。</summary>
    public bool ContentEquals(SchematicDocument other) =>
        NextId == other.NextId && Elements.SequenceEqual(other.Elements);

    private GridPoint SelectionCenter(HashSet<ElementId> ids)
    {
        var points = Elements.Where(e => ids.Contains(e.Id)).SelectMany(e => e switch
        {
            ComponentSymbol c => [c.Position],
            _ => e.Terminals,
        }).ToList();
        if (points.Count == 0)
        {
            return new GridPoint(0, 0);
        }

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        return new GridPoint((minX + maxX) / 2, (minY + maxY) / 2);
    }

    private SchematicDocument Transform(IEnumerable<ElementId> ids, Func<SchematicElement, SchematicElement> f)
    {
        var set = ids as HashSet<ElementId> ?? ids.ToHashSet();
        return this with { Elements = Elements.Select(e => set.Contains(e.Id) ? f(e) : e).ToImmutableList() };
    }
}
