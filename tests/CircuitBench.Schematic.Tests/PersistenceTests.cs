using CircuitBench.Schematic.Editing;
using CircuitBench.Schematic.Geometry;
using CircuitBench.Schematic.Model;
using CircuitBench.Schematic.Persistence;
using CircuitBench.TestKit;
using CircuitBench.TestKit.Property;

namespace CircuitBench.Schematic.Tests;

/// <summary>驗收標準 20：專案檔往返無損；版本過新或損毀時回傳明確訊息且不崩潰。</summary>
public class PersistenceTests
{
    private static ProjectFile Sample()
    {
        var b = new SchematicBuilder();
        b.Component(ComponentKind.VoltageSource, "V1", 0, 0, parameters: [("dc", "5"), ("waveform", "PULSE(0 5 0 1u 1u 1m 2m)")]);
        b.Component(ComponentKind.Resistor, "R1", 2, -2, Rotation.R270, true, ("value", "4.7k"));
        b.Wire(0, -2, 0, -6);
        b.Label(4, -2, "輸出");
        b.Ground(0, 2);
        return new ProjectFile(b.Doc,
            new AnalysisSettings { Kind = AnalysisKind.Ac, AcStart = "1", AcStop = "1meg", RelTol = 1e-4 },
            new ViewSettings { Zoom = 33.5, PanX = -12.25, PanY = 7, Probes = ["V(輸出)", "I(R1)"] },
            "分壓器");
    }

    [Fact]
    public void RoundTripIsLossless()
    {
        var original = Sample();
        var json = ProjectSerializer.Serialize(original);
        var loaded = ProjectSerializer.Deserialize(json);
        Assert.Equal(ProjectLoadStatus.Ok, loaded.Status);
        Assert.True(original.ContentEquals(loaded.Project!));
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("輸出", json); // 中文不跳脫，檔案可讀
    }

    [Fact]
    public void Property_RandomDocumentsRoundTrip()
    {
        PropertyTest.ForAll(30, (rng, _) =>
        {
            var doc = AutoLayout.FromCircuit(RandomCircuits.Linear(rng, includeReactive: true, includeAc: true));
            var project = new ProjectFile(doc, new AnalysisSettings(), new ViewSettings());
            var back = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
            Assert.True(project.ContentEquals(back.Project!));
        });
    }

    [Fact]
    public void NewerSchemaVersionIsRejectedWithClearMessage()
    {
        var json = ProjectSerializer.Serialize(Sample()).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
        var r = ProjectSerializer.Deserialize(json);
        Assert.Equal(ProjectLoadStatus.TooNew, r.Status);
        Assert.Null(r.Project);
        Assert.Contains("較新版本", r.MessageZh);
        Assert.Contains("99", r.MessageZh);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\": \"1\"}")]
    [InlineData("{\"schemaVersion\": 0}")]
    [InlineData("{\"schemaVersion\": 1, \"elements\": [{\"type\": \"blob\", \"id\": 1}]}")]
    [InlineData("{\"schemaVersion\": 1, \"elements\": [{\"type\": \"wire\", \"id\": 1, \"x\": 0, \"y\": 0, \"x2\": 3, \"y2\": 4}]}")]
    [InlineData("{\"schemaVersion\": 1, \"elements\": [{\"type\": \"component\", \"id\": 1, \"kind\": \"warpDrive\"}]}")]
    [InlineData("{\"schemaVersion\": 1, \"elements\": [{\"type\": \"ground\", \"id\": 1}, {\"type\": \"ground\", \"id\": 1}]}")]
    [InlineData("{\"schemaVersion\": 1, \"elements\": [{\"type\": \"component\", \"id\": 1, \"kind\": \"resistor\", \"rotation\": 45}]}")]
    [InlineData("{\"schemaVersion\": 1, \"nextId\": \"x\"}")]
    [InlineData("{\"schemaVersion\": 1, \"elements\": [{\"type\": \"component\", \"id\": 1, \"kind\": 99}]}")]
    [InlineData("{\"schemaVersion\": 1, \"elements\": [{\"type\": \"component\", \"id\": 1, \"kind\": \"resistor\", \"parameters\": {\"value\": null}}]}")]
    [InlineData("{\"schemaVersion\": 1, \"analysis\": {\"kind\": 7}}")]
    public void CorruptedFilesAreReportedNotThrown(string json)
    {
        var r = ProjectSerializer.Deserialize(json);
        Assert.Equal(ProjectLoadStatus.Corrupted, r.Status);
        Assert.Null(r.Project);
        Assert.Contains("損毀", r.MessageZh);
    }

    [Fact]
    public void TruncatedFileIsCorrupted()
    {
        var json = ProjectSerializer.Serialize(Sample());
        var r = ProjectSerializer.Deserialize(json[..(json.Length / 2)]);
        Assert.Equal(ProjectLoadStatus.Corrupted, r.Status);
        Assert.Contains("行", r.MessageZh);
    }

    [Fact]
    public void NextIdIsRepairedWhenTooSmall()
    {
        var json = ProjectSerializer.Serialize(Sample()).Replace("\"nextId\":", "\"nextId\": 1, \"ignored\":");
        var r = ProjectSerializer.Deserialize(json);
        Assert.Equal(ProjectLoadStatus.Ok, r.Status);
        Assert.True(r.Project!.Schematic.NextId > r.Project.Schematic.Elements.Max(e => e.Id.Value));
    }
}
