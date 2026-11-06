using System.Collections.Immutable;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.IO;
using CircuitBench.Core.Model;
using CircuitBench.TestKit;
using CircuitBench.TestKit.Property;

namespace CircuitBench.Core.Tests.IO;

public class SpiceNetlistTests
{
    private static SpiceDeck ParseOk(string text)
    {
        var result = SpiceNetlistReader.Parse(text);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return result.Deck!;
    }

    [Fact]
    public void ParsesAllElementTypesAndDirectives()
    {
        var deck = ParseOk("""
            * 範例
            .title 測試電路
            V1 in 0 DC 5 AC 1 30 SIN(0 1 1k 1m 10 45)
            I1 0 n PULSE(0 1m 1u 1n 1n 5u 10u)
            V2 p 0 PWL(0 0 1m 5 2m 0)
            R1 in out 4.7k
            C1 out 0 100nF
            L1 out n 10mH
            D1 n 0 DMOD
            .model DMOD D(Is=2.5n N=1.75)
            E1 e 0 out 0 -10
            G1 g 0 out 0 1m
            Rload g 0 1meg
            Re e 0 1k
            Rp p 0 1
            S1 n 0 times=(1m 2m) initial=off
            .op
            .tran 1u 10m 0 2u
            .ac dec 20 1 1meg
            .dc V1 0 10 0.5
            .end
            R99 ignored 0 1
            """);
        var c = deck.Circuit;
        Assert.Equal("測試電路", deck.Title);
        Assert.Equal(13, c.Components.Count);
        var v1 = Assert.IsType<VoltageSource>(c.Find("V1"));
        Assert.Equal(5, v1.Spec.DcValue);
        Assert.Equal(new AcSpec(1, 30), v1.Spec.Ac);
        Assert.Equal(new SineWaveform(0, 1, 1e3, 1e-3, 10, 45), v1.Spec.Waveform);
        Assert.Equal(new PulseWaveform(0, 1e-3, 1e-6, 1e-9, 1e-9, 5e-6, 10e-6), ((CurrentSource)c.Find("I1")!).Spec.Waveform);
        Assert.Equal(new PwlWaveform([(0.0, 0.0), (1e-3, 5.0), (2e-3, 0.0)]), ((VoltageSource)c.Find("V2")!).Spec.Waveform);
        Assert.Equal(4.7e3, ((Resistor)c.Find("R1")!).Resistance, 9);
        Assert.Equal(new DiodeModel("DMOD", 2.5e-9, 1.75), ((Diode)c.Find("D1")!).Model);
        Assert.Equal(-10, ((Vcvs)c.Find("E1")!).Gain);
        Assert.Equal(1e6, ((Resistor)c.Find("Rload")!).Resistance);
        var s = (TimedSwitch)c.Find("S1")!;
        Assert.Equal([1e-3, 2e-3], s.ToggleTimes.ToArray());
        Assert.False(s.InitiallyOn);
        Assert.Null(c.Find("R99"));
        Assert.Equal(4, deck.Analyses.Count);
        Assert.Equal(new TranCommand(1e-6, 10e-3, 0, 2e-6), deck.Analyses[1]);
        Assert.Equal(new AcCommand(AcSweepType.Decade, 20, 1, 1e6), deck.Analyses[2]);
        Assert.Equal(new DcCommand("V1", 0, 10, 0.5), deck.Analyses[3]);
    }

    [Fact]
    public void IsCaseInsensitiveAndSupportsContinuationLines()
    {
        var deck = ParseOk("""
            v1 IN 0
            + dc 3
            r1 in 0 1K
            .TRAN 1U
            + 1M
            """);
        Assert.Equal(3, ((VoltageSource)deck.Circuit.Find("V1")!).Spec.DcValue);
        Assert.Equal(new TranCommand(1e-6, 1e-3), deck.Analyses[0]);
    }

    [Fact]
    public void BareValueIsDcValue()
    {
        var deck = ParseOk("V1 a 0 12\nR1 a 0 1");
        Assert.Equal(SourceSpec.FromDc(12), ((VoltageSource)deck.Circuit.Find("V1")!).Spec);
    }

    [Theory]
    [InlineData("R1 a 0 1\nR2 a 0\n", 2, "欄位不足")]
    [InlineData("R1 a 0 1\n\nX1 a 0 1\n", 3, "無法辨識的元件")]
    [InlineData("R1 a 0 abc\n", 1, "不是有效的數值")]
    [InlineData("R1 a 0 1\nR1 b 0 2\n", 2, "重複")]
    [InlineData("D1 a 0 NOPE\nR1 a 0 1\n", 1, "沒有以 .model 定義")]
    [InlineData("V1 a 0 PULSE(0 1 0 1n\n", 1, "右括號")]
    [InlineData("V1 a 0 PWL(0 0 0 1)\n", 1, "嚴格遞增")]
    [InlineData("S1 a 0 times=(1m) initial=maybe\n", 1, "on 或 off")]
    [InlineData(".foo 1 2\n", 1, "不支援的指令")]
    [InlineData("+ 1 2\n", 1, "接續行")]
    [InlineData(".ac oct 10 1 1k\n", 1, "DEC 或 LIN")]
    [InlineData("* c\nV1 a 0 DC 1\n+ AC\n", 2, "缺少振幅")]
    public void SyntaxErrorsReportLineNumberAndReason(string text, int line, string reason)
    {
        var result = SpiceNetlistReader.Parse(text);
        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal(line, error.LineNumber);
        Assert.Contains(reason, error.MessageZh);
        Assert.StartsWith($"第 {line} 行", error.ToString());
    }

    [Fact]
    public void MultipleErrorsAreAllReported()
    {
        var result = SpiceNetlistReader.Parse("R1 a 0\nC1 a 0 x\nR2 a 0 1\n");
        Assert.Equal([1, 2], result.Errors.Select(e => e.LineNumber).ToArray());
    }

    [Fact]
    public void ExportThenImportGivesSameCircuitForHandWrittenDeck()
    {
        var c = Circuit.Create([
            new VoltageSource("V1", "in", "0", new SourceSpec(1.5, new SineWaveform(0, 1, 50, 0, 0, 90), new AcSpec(1, -45))),
            new CurrentSource("I1", "0", "x", new SourceSpec(null, new PulseWaveform(0, 1e-3, 1e-6, 1e-9, 2e-9, double.PositiveInfinity, 0), null)),
            new VoltageSource("V2", "p", "0", new SourceSpec(null, new PwlWaveform([(0.0, 0.0), (1e-3, 1.0 / 3)]), null)),
            new Resistor("R1", "in", "out", 1.0 / 7),
            new Capacitor("C1", "out", "0", 1e-9),
            new Inductor("L1", "out", "x", 2.2e-3),
            new Diode("D1", "x", "0", new DiodeModel("DX", 3e-15, 1.1)),
            new Diode("D2", "p", "x", new DiodeModel("DX", 1e-12, 2)),
            new Vcvs("E1", "e", "0", "out", "0", 100),
            new Vccs("G1", "g", "0", "out", "x", -1e-3),
            new TimedSwitch("S1", "x", "p", ImmutableArray.Create(1e-4, 3e-4), true, 0.01, 1e9),
            new Resistor("R2", "e", "g", 1),
            new Resistor("R3", "g", "0", 1),
        ]);
        var deck = new SpiceDeck(c, [new OpCommand(), new TranCommand(1e-6, 1e-3, 0, 1e-6), new AcCommand(AcSweepType.Linear, 11, 10, 20), new DcCommand("V1", -1, 1, 0.1)], "往返");
        var text = SpiceNetlistWriter.Write(deck);
        var back = ParseOk(text);
        Assert.True(c.StructurallyEquals(back.Circuit), text);
        Assert.Equal(deck.Analyses, back.Analyses);
        Assert.Equal("往返", back.Title);
    }

    [Fact]
    public void Property_RandomCircuitsRoundTrip()
    {
        PropertyTest.ForAll(150, (rng, _) =>
        {
            var c = RandomCircuits.Linear(rng, includeReactive: true, includeAc: rng.Next(2) == 0);
            var text = SpiceNetlistWriter.Write(new SpiceDeck(c, []));
            var back = ParseOk(text);
            Assert.True(c.StructurallyEquals(back.Circuit), text);
        });
    }
}
