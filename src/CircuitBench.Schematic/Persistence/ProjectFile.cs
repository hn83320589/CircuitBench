using System.Collections.Immutable;
using CircuitBench.Schematic.Model;

namespace CircuitBench.Schematic.Persistence;

public enum AnalysisKind
{
    OperatingPoint,
    DcSweep,
    Transient,
    Ac,
}

/// <summary>分析設定。數值欄位保留使用者輸入的字串（工程記號），執行分析時才解析。</summary>
public sealed record AnalysisSettings
{
    public AnalysisKind Kind { get; init; } = AnalysisKind.Transient;

    public string DcSource { get; init; } = "V1";

    public string DcStart { get; init; } = "0";

    public string DcStop { get; init; } = "10";

    public string DcStep { get; init; } = "0.1";

    public string TranStop { get; init; } = "10m";

    /// <summary>空白代表預設（停止時間的 1/50）。</summary>
    public string TranMaxStep { get; init; } = string.Empty;

    public bool AcDecade { get; init; } = true;

    public string AcStart { get; init; } = "10";

    public string AcStop { get; init; } = "1meg";

    public string AcPoints { get; init; } = "20";

    public double RelTol { get; init; } = 1e-3;

    public double VnTol { get; init; } = 1e-6;

    public double AbsTol { get; init; } = 1e-12;

    public double Gmin { get; init; } = 1e-12;

    public int MaxNewtonIterations { get; init; } = 100;
}

/// <summary>畫面設定：縮放、平移與要顯示的波形。</summary>
public sealed record ViewSettings
{
    public double Zoom { get; init; } = 20;

    public double PanX { get; init; }

    public double PanY { get; init; }

    public ImmutableList<string> Probes { get; init; } = [];

    public bool Equals(ViewSettings? other) =>
        other is not null && Zoom.Equals(other.Zoom) && PanX.Equals(other.PanX) && PanY.Equals(other.PanY) && Probes.SequenceEqual(other.Probes);

    public override int GetHashCode() => HashCode.Combine(Zoom, PanX, PanY, Probes.Count);
}

/// <summary>專案檔內容（JSON，含 schemaVersion）。</summary>
public sealed record ProjectFile(SchematicDocument Schematic, AnalysisSettings Analysis, ViewSettings View, string Title = "")
{
    public static ProjectFile New() => new(SchematicDocument.Empty, new AnalysisSettings(), new ViewSettings());

    public bool ContentEquals(ProjectFile other) =>
        Schematic.ContentEquals(other.Schematic) && Analysis == other.Analysis && View == other.View && Title == other.Title;
}

public enum ProjectLoadStatus
{
    Ok,
    TooNew,
    Corrupted,
}

public sealed record ProjectLoadResult(ProjectLoadStatus Status, ProjectFile? Project, string MessageZh)
{
    public static ProjectLoadResult Ok(ProjectFile p) => new(ProjectLoadStatus.Ok, p, string.Empty);

    public static ProjectLoadResult TooNew(int version) =>
        new(ProjectLoadStatus.TooNew, null, $"這個專案檔由較新版本的電路實驗台建立（檔案版本 {version}，目前程式支援到 {ProjectSerializer.CurrentSchemaVersion}）。請更新程式後再開啟。");

    public static ProjectLoadResult Corrupted(string reason) =>
        new(ProjectLoadStatus.Corrupted, null, $"專案檔已損毀或格式不正確，無法開啟：{reason}");
}
