using System.Text;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.IO;
using CircuitBench.Core.Model;
using CircuitBench.Core.Validation;
using CircuitBench.Schematic.Connectivity;
using CircuitBench.Schematic.Persistence;

namespace CircuitBench.App.ViewModels.Tests.Samples;

/// <summary>
/// 範例電路的回歸測試：samples/ 下的專案檔與網表必須與 <see cref="SampleCatalog"/> 一致，
/// 且兩者執行分析後都符合預期結果（解析解或手算值）。
/// 設定環境變數 CIRCUITBENCH_WRITE_SAMPLES=1 會重新產生 samples/ 下的檔案。
/// </summary>
public class SampleTests
{
    public static TheoryData<string> Ids => [.. SampleCatalog.All.Select(s => s.Id)];

    private static string SamplesDirectory
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CircuitBench.sln")))
            {
                dir = dir.Parent;
            }

            return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("找不到 CircuitBench.sln"), "samples");
        }
    }

    private static ProjectFile ToProject(Sample s) => new(s.Schematic, s.Analysis, new ViewSettings(), s.TitleZh);

    private static string ToNetlist(Sample s)
    {
        var circuit = ConnectivityExtractor.Extract(s.Schematic).Circuit;
        return SpiceNetlistWriter.Write(new SpiceDeck(circuit, [MainWindowViewModel.ToCommand(s.Analysis)], s.TitleZh));
    }

    private static SampleRun Run(Circuit circuit, AnalysisSettings settings)
    {
        var sim = new Simulator();
        var options = SimulationOptions.Default;
        var command = MainWindowViewModel.ToCommand(settings);
        return command switch
        {
            OpCommand => new SampleRun { Op = sim.OperatingPoint(circuit, options) },
            TranCommand t => new SampleRun { Tran = sim.Transient(circuit, t.ToRequest(), options) },
            AcCommand a => new SampleRun { Ac = sim.Ac(circuit, a.ToRequest(), options) },
            _ => throw new NotSupportedException(),
        };
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void ProjectFileMatchesCatalogAndMeetsExpectation(string id)
    {
        var sample = SampleCatalog.All.Single(s => s.Id == id);
        var path = Path.Combine(SamplesDirectory, id + ProjectSerializer.FileExtension);
        var json = ProjectSerializer.Serialize(ToProject(sample));
        if (Environment.GetEnvironmentVariable("CIRCUITBENCH_WRITE_SAMPLES") == "1")
        {
            Directory.CreateDirectory(SamplesDirectory);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        Assert.True(File.Exists(path), $"缺少範例檔 {path}（以 CIRCUITBENCH_WRITE_SAMPLES=1 產生）");
        var loaded = ProjectSerializer.Deserialize(File.ReadAllText(path));
        Assert.Equal(ProjectLoadStatus.Ok, loaded.Status);
        Assert.True(loaded.Project!.ContentEquals(ToProject(sample)), "範例專案檔與目錄定義不同，請重新產生");

        var extraction = ConnectivityExtractor.Extract(loaded.Project.Schematic);
        var issues = CircuitValidator.Validate(extraction.Circuit, extraction.Issues);
        Assert.True(issues.Count == 0, string.Join("\n", issues.Select(i => i.MessageZh)));
        sample.Verify(Run(extraction.Circuit, loaded.Project.Analysis));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void NetlistMatchesSchematicAndMeetsExpectation(string id)
    {
        var sample = SampleCatalog.All.Single(s => s.Id == id);
        var path = Path.Combine(SamplesDirectory, id + ".cir");
        if (Environment.GetEnvironmentVariable("CIRCUITBENCH_WRITE_SAMPLES") == "1")
        {
            Directory.CreateDirectory(SamplesDirectory);
            File.WriteAllText(path, ToNetlist(sample), new UTF8Encoding(false));
        }

        Assert.True(File.Exists(path), $"缺少範例網表 {path}");
        var parsed = SpiceNetlistReader.Parse(File.ReadAllText(path));
        Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
        var fromSchematic = ConnectivityExtractor.Extract(sample.Schematic).Circuit;
        Assert.True(parsed.Deck!.Circuit.StructurallyEquals(fromSchematic), "網表與圖面萃取結果不同");
        sample.Verify(Run(parsed.Deck.Circuit, sample.Analysis));
    }

    [Fact]
    public void AtLeastTenSamples() => Assert.True(SampleCatalog.All.Count >= 10);
}
