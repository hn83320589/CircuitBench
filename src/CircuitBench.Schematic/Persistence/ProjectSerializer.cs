using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;

namespace CircuitBench.Schematic.Persistence;

/// <summary>
/// 專案檔 JSON 讀寫。版本過新或內容損毀時不丟出例外，而是回傳帶中文訊息的結果，讓 UI 顯示而不崩潰。
/// </summary>
public static class ProjectSerializer
{
    public const int CurrentSchemaVersion = 1;
    public const string FileExtension = ".cbench";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(ProjectFile project)
    {
        var dto = new ProjectDto
        {
            SchemaVersion = CurrentSchemaVersion,
            Title = project.Title,
            NextId = project.Schematic.NextId,
            Elements = project.Schematic.Elements.Select(ToDto).ToList(),
            Analysis = project.Analysis,
            View = new ViewDto { Zoom = project.View.Zoom, PanX = project.View.PanX, PanY = project.View.PanY, Probes = [.. project.View.Probes] },
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static ProjectLoadResult Deserialize(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ProjectLoadResult.Corrupted($"JSON 語法錯誤（第 {ex.LineNumber + 1} 行，第 {ex.BytePositionInLine + 1} 個位置）。");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schemaVersion", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var version))
            {
                return ProjectLoadResult.Corrupted("缺少 schemaVersion 欄位。");
            }

            if (version > CurrentSchemaVersion)
            {
                return ProjectLoadResult.TooNew(version);
            }

            if (version < 1)
            {
                return ProjectLoadResult.Corrupted($"schemaVersion {version} 無效。");
            }
        }

        ProjectDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ProjectDto>(json, Options);
        }
        catch (JsonException ex)
        {
            return ProjectLoadResult.Corrupted($"欄位格式錯誤（{ex.Path}）。");
        }

        if (dto is null)
        {
            return ProjectLoadResult.Corrupted("內容是空的。");
        }

        var elements = ImmutableList.CreateBuilder<SchematicElement>();
        var ids = new HashSet<int>();
        for (var i = 0; i < (dto.Elements?.Count ?? 0); i++)
        {
            var e = dto.Elements![i];
            if (!ids.Add(e.Id))
            {
                return ProjectLoadResult.Corrupted($"第 {i + 1} 個元素的識別碼 {e.Id} 重複。");
            }

            var element = FromDto(e, out var error);
            if (element is null)
            {
                return ProjectLoadResult.Corrupted($"第 {i + 1} 個元素無效：{error}");
            }

            elements.Add(element);
        }

        var nextId = Math.Max(dto.NextId, ids.Count == 0 ? 1 : ids.Max() + 1);
        var view = dto.View ?? new ViewDto();
        return ProjectLoadResult.Ok(new ProjectFile(
            new SchematicDocument(elements.ToImmutable(), nextId),
            dto.Analysis ?? new AnalysisSettings(),
            new ViewSettings { Zoom = view.Zoom, PanX = view.PanX, PanY = view.PanY, Probes = [.. view.Probes ?? []] },
            dto.Title ?? string.Empty));
    }

    private static ElementDto ToDto(SchematicElement e) => e switch
    {
        ComponentSymbol c => new ElementDto
        {
            Type = "component", Id = c.Id.Value, Kind = c.Kind, Name = c.Name, X = c.Position.X, Y = c.Position.Y,
            Rotation = (int)c.Orientation.Rotation * 90, Mirrored = c.Orientation.Mirrored,
            Parameters = new SortedDictionary<string, string>(c.Parameters),
        },
        Wire w => new ElementDto { Type = "wire", Id = w.Id.Value, X = w.A.X, Y = w.A.Y, X2 = w.B.X, Y2 = w.B.Y },
        NetLabel l => new ElementDto { Type = "label", Id = l.Id.Value, X = l.Position.X, Y = l.Position.Y, Name = l.NetName },
        GroundSymbol g => new ElementDto { Type = "ground", Id = g.Id.Value, X = g.Position.X, Y = g.Position.Y },
        _ => throw new NotSupportedException(e.GetType().Name),
    };

    private static SchematicElement? FromDto(ElementDto e, out string error)
    {
        error = string.Empty;
        var id = new ElementId(e.Id);
        var p = new GridPoint(e.X, e.Y);
        switch (e.Type)
        {
            case "component":
                if (e.Kind is null || !Enum.IsDefined(e.Kind.Value))
                {
                    error = "元件缺少 kind 或類型不存在。";
                    return null;
                }

                if (e.Parameters?.Values.Any(v => v is null) == true)
                {
                    error = "元件參數的值不可為 null。";
                    return null;
                }

                if (e.Rotation is not (0 or 90 or 180 or 270))
                {
                    error = $"旋轉角度 {e.Rotation} 無效（只能是 0、90、180、270）。";
                    return null;
                }

                return new ComponentSymbol(id, e.Kind.Value, e.Name ?? string.Empty, p,
                    new Orientation((Rotation)(e.Rotation / 90), e.Mirrored),
                    (e.Parameters ?? []).ToImmutableSortedDictionary());
            case "wire":
                var w = new Wire(id, p, new GridPoint(e.X2 ?? e.X, e.Y2 ?? e.Y));
                if (!w.IsValid)
                {
                    error = "導線必須是水平或垂直，且兩端點不同。";
                    return null;
                }

                return w;
            case "label":
                if (string.IsNullOrWhiteSpace(e.Name))
                {
                    error = "網路標籤沒有名稱。";
                    return null;
                }

                return new NetLabel(id, p, e.Name);
            case "ground":
                return new GroundSymbol(id, p);
            default:
                error = $"不認得的元素類型「{e.Type}」。";
                return null;
        }
    }

    private sealed class ProjectDto
    {
        public int SchemaVersion { get; set; }

        public string? Title { get; set; }

        public int NextId { get; set; }

        public List<ElementDto>? Elements { get; set; }

        public AnalysisSettings? Analysis { get; set; }

        public ViewDto? View { get; set; }
    }

    private sealed class ElementDto
    {
        public string Type { get; set; } = string.Empty;

        public int Id { get; set; }

        public ComponentKind? Kind { get; set; }

        public string? Name { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        public int? X2 { get; set; }

        public int? Y2 { get; set; }

        public int Rotation { get; set; }

        public bool Mirrored { get; set; }

        public SortedDictionary<string, string>? Parameters { get; set; }
    }

    private sealed class ViewDto
    {
        public double Zoom { get; set; } = 20;

        public double PanX { get; set; }

        public double PanY { get; set; }

        public List<string>? Probes { get; set; }
    }
}
