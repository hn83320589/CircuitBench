using System.Collections.Immutable;
using CircuitBench.Core.Model;
using CircuitBench.Core.Validation;

namespace CircuitBench.Core.Tests.Validation;

public class CircuitValidatorTests
{
    private static IReadOnlyList<CircuitIssue> Check(params Component[] parts) => CircuitValidator.Validate(Circuit.Create(parts));

    [Fact]
    public void ValidCircuitHasNoIssues()
    {
        Assert.Empty(Check(VoltageSource.Dc("V1", "a", "0", 1), new Resistor("R1", "a", "0", 1)));
    }

    [Fact]
    public void MissingGroundIsReported()
    {
        var issues = Check(VoltageSource.Dc("V1", "a", "b", 1), new Resistor("R1", "a", "b", 1));
        var issue = Assert.Single(issues, i => i.Code == IssueCode.NoGround);
        Assert.Contains("接地", issue.MessageZh);
        Assert.Contains("V1", issue.ComponentNames);
        Assert.DoesNotContain(issues, i => i.Code == IssueCode.FloatingNode);
    }

    [Fact]
    public void NodeConnectedOnlyThroughCapacitorIsFloating()
    {
        var issues = Check(
            VoltageSource.Dc("V1", "a", "0", 1),
            new Resistor("R1", "a", "0", 1),
            new Capacitor("C1", "a", "b", 1e-6),
            new Capacitor("C2", "b", "0", 1e-6));
        var issue = Assert.Single(issues);
        Assert.Equal(IssueCode.FloatingNode, issue.Code);
        Assert.Equal([new NodeName("b")], issue.Nodes.ToArray());
        Assert.Contains("C1", issue.ComponentNames);
        Assert.Contains("節點 b", issue.MessageZh);
        Assert.Contains("浮接", issue.MessageZh);
    }

    [Fact]
    public void NodeConnectedOnlyThroughCurrentSourcesIsFloating()
    {
        var issues = Check(
            new Resistor("R1", "a", "0", 1),
            CurrentSource.Dc("I1", "a", "b", 1e-3),
            new Vccs("G1", "b", "0", "a", "0", 1e-3),
            new Resistor("R2", "b", "c", 1),
            new Resistor("R3", "c", "b", 1));
        var issue = Assert.Single(issues, i => i.Code == IssueCode.FloatingNode);
        Assert.Equal(2, issue.Nodes.Length);
        Assert.Contains(new NodeName("b"), issue.Nodes);
        Assert.Contains(new NodeName("c"), issue.Nodes);
    }

    [Fact]
    public void VcvsOutputIsDcPathButControlPinsAreNot()
    {
        // E1 輸出 b-0 提供直流路徑；控制端 c 只被感測，c 為浮接
        var issues = Check(
            VoltageSource.Dc("V1", "a", "0", 1),
            new Resistor("R1", "a", "0", 1),
            new Vcvs("E1", "b", "0", "c", "0", 2),
            new Resistor("R2", "b", "0", 1),
            new Capacitor("C1", "c", "a", 1e-6));
        var issue = Assert.Single(issues, i => i.Code == IssueCode.FloatingNode);
        Assert.Equal([new NodeName("c")], issue.Nodes.ToArray());
    }

    [Fact]
    public void ParallelVoltageSourcesFormLoop()
    {
        var issues = Check(
            VoltageSource.Dc("V1", "a", "0", 1),
            VoltageSource.Dc("V2", "a", "0", 2),
            new Resistor("R1", "a", "0", 1));
        var issue = Assert.Single(issues, i => i.Code == IssueCode.VoltageSourceInductorLoop);
        Assert.Equal(["V1", "V2"], issue.ComponentNames.Order().ToArray());
        Assert.Contains("迴路", issue.MessageZh);
    }

    [Fact]
    public void VoltageSourceInductorAndVcvsLoopListsAllMembers()
    {
        var issues = Check(
            VoltageSource.Dc("V1", "a", "0", 1),
            new Inductor("L1", "a", "b", 1e-3),
            new Vcvs("E1", "b", "c", "a", "0", 1),
            new Inductor("L2", "c", "0", 1e-3),
            new Resistor("R1", "b", "0", 1));
        var issue = Assert.Single(issues, i => i.Code == IssueCode.VoltageSourceInductorLoop);
        Assert.Equal(["E1", "L1", "L2", "V1"], issue.ComponentNames.Order().ToArray());
    }

    [Fact]
    public void InductorAcrossVoltageSourceIsLoop()
    {
        var issues = Check(VoltageSource.Dc("V1", "a", "0", 1), new Inductor("L1", "a", "0", 1e-3));
        Assert.Contains(issues, i => i.Code == IssueCode.VoltageSourceInductorLoop && i.ComponentNames.Contains("L1"));
    }

    [Fact]
    public void DanglingPinIsReportedWithComponentAndPin()
    {
        var issues = Check(VoltageSource.Dc("V1", "a", "0", 1), new Resistor("R1", "a", "0", 1), new Resistor("R2", "a", "x", 1));
        var issue = Assert.Single(issues, i => i.Code == IssueCode.UnconnectedPin);
        Assert.Equal(["R2"], issue.ComponentNames.ToArray());
        Assert.Equal("接腳2", issue.PinName);
        Assert.Contains("R2", issue.MessageZh);
    }

    [Fact]
    public void ExtractionIssuesArePassedThroughAndReplaceNetlistDanglingCheck()
    {
        var extraction = new CircuitIssue(IssueCode.UnconnectedPin, "R9 的接腳1 未連接", ["R9"], [], "接腳1");
        var issues = CircuitValidator.Validate(
            Circuit.Create([VoltageSource.Dc("V1", "a", "0", 1), new Resistor("R1", "a", "0", 1), new Resistor("R2", "a", "x", 1)]),
            [extraction]);
        Assert.Single(issues, i => i.Code == IssueCode.UnconnectedPin);
        Assert.Contains(extraction, issues);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    public void NonPositiveResistanceIsInvalid(double value)
    {
        var issues = Check(VoltageSource.Dc("V1", "a", "0", 1), new Resistor("R1", "a", "0", value));
        var issue = Assert.Single(issues, i => i.Code == IssueCode.InvalidParameter);
        Assert.Equal(["R1"], issue.ComponentNames.ToArray());
        Assert.Contains("大於 0", issue.MessageZh);
    }

    [Fact]
    public void InvalidCapacitorInductorAndDiodeParameters()
    {
        var issues = Check(
            VoltageSource.Dc("V1", "a", "0", 1),
            new Resistor("R1", "a", "0", 1),
            new Capacitor("C1", "a", "0", 0),
            new Inductor("L1", "a", "b", -1),
            new Resistor("R2", "b", "0", 1),
            new Diode("D1", "a", "0", new DiodeModel("D", -1, 0)));
        var invalid = issues.Where(i => i.Code == IssueCode.InvalidParameter).ToList();
        Assert.Contains(invalid, i => i.ComponentNames.Contains("C1"));
        Assert.Contains(invalid, i => i.ComponentNames.Contains("L1"));
        Assert.Equal(2, invalid.Count(i => i.ComponentNames.Contains("D1")));
    }

    [Fact]
    public void InvalidWaveformsAndSwitchTimes()
    {
        var issues = Check(
            new VoltageSource("V1", "a", "0", new SourceSpec(null, new PwlWaveform([(0.0, 0.0), (0.0, 1.0)]), null)),
            new Resistor("R1", "a", "0", 1),
            new VoltageSource("V2", "b", "0", new SourceSpec(null, new PulseWaveform(0, 1, 0, -1, 0, 1, 2), null)),
            new Resistor("R2", "b", "0", 1),
            new TimedSwitch("S1", "a", "b", ImmutableArray.Create(2.0, 1.0), false));
        Assert.Contains(issues, i => i.Code == IssueCode.InvalidParameter && i.ComponentNames.Contains("V1") && i.MessageZh.Contains("PWL"));
        Assert.Contains(issues, i => i.Code == IssueCode.InvalidParameter && i.ComponentNames.Contains("V2") && i.MessageZh.Contains("PULSE"));
        Assert.Contains(issues, i => i.Code == IssueCode.InvalidParameter && i.ComponentNames.Contains("S1") && i.MessageZh.Contains("遞增"));
    }

    [Fact]
    public void TooManyNodesIsReportedOnlyWhenEnforced()
    {
        var parts = new List<Component> { VoltageSource.Dc("V1", "n0", "0", 1) };
        for (var i = 1; i <= 5001; i++)
        {
            parts.Add(new Resistor($"R{i}", $"n{i - 1}", $"n{i}", 1));
        }

        parts.Add(new Resistor("Rend", "n5001", "0", 1));
        var circuit = Circuit.Create(parts);
        Assert.Contains(CircuitValidator.Validate(circuit), i => i.Code == IssueCode.TooManyNodes);
        Assert.DoesNotContain(CircuitValidator.Validate(circuit, enforceNodeLimit: false), i => i.Code == IssueCode.TooManyNodes);
    }
}
