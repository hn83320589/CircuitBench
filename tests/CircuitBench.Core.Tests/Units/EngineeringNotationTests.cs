using CircuitBench.Core.Units;

namespace CircuitBench.Core.Tests.Units;

public class EngineeringNotationTests
{
    [Theory]
    [InlineData("1f", 1e-15)]
    [InlineData("1p", 1e-12)]
    [InlineData("1n", 1e-9)]
    [InlineData("1u", 1e-6)]
    [InlineData("1µ", 1e-6)]
    [InlineData("1μ", 1e-6)]
    [InlineData("1m", 1e-3)]
    [InlineData("1k", 1e3)]
    [InlineData("1meg", 1e6)]
    [InlineData("1g", 1e9)]
    [InlineData("1t", 1e12)]
    public void EachSuffixHasCorrectScale(string text, double expected)
    {
        Assert.Equal(expected, EngineeringNotation.Parse(text), 1e-12 * Math.Abs(expected));
    }

    [Theory]
    [InlineData("1K", 1e3)]
    [InlineData("1MEG", 1e6)]
    [InlineData("1Meg", 1e6)]
    [InlineData("2.2U", 2.2e-6)]
    [InlineData("3N", 3e-9)]
    [InlineData("1F", 1e-15)]
    [InlineData("1T", 1e12)]
    public void SuffixesAreCaseInsensitive(string text, double expected)
    {
        Assert.Equal(expected, EngineeringNotation.Parse(text), 1e-12 * Math.Abs(expected));
    }

    [Theory]
    [InlineData("1m", 1e-3)]
    [InlineData("1M", 1e-3)]
    [InlineData("1meg", 1e6)]
    [InlineData("1MEG", 1e6)]
    [InlineData("1mohm", 1e-3)]
    [InlineData("1megohm", 1e6)]
    public void MIsAlwaysMilliAndMegIsMega(string text, double expected)
    {
        Assert.Equal(expected, EngineeringNotation.Parse(text), 1e-12 * Math.Abs(expected));
    }

    [Theory]
    [InlineData("10uF", 10e-6)]
    [InlineData("4.7kohm", 4.7e3)]
    [InlineData("100nH", 100e-9)]
    [InlineData("5V", 5)]
    [InlineData("10Hz", 10)]
    [InlineData("1mA", 1e-3)]
    [InlineData("2.5ohm", 2.5)]
    public void TrailingUnitLettersAreIgnored(string text, double expected)
    {
        Assert.Equal(expected, EngineeringNotation.Parse(text), 1e-12 * Math.Abs(expected));
    }

    [Theory]
    [InlineData("1e-6", 1e-6)]
    [InlineData("1E3", 1e3)]
    [InlineData("-2.5e+2", -250)]
    [InlineData("1e3k", 1e6)]
    [InlineData(".5", 0.5)]
    [InlineData("5.", 5)]
    [InlineData("+3", 3)]
    [InlineData("  42  ", 42)]
    [InlineData("0", 0)]
    public void AcceptsScientificNotationAndPlainNumbers(string text, double expected)
    {
        Assert.Equal(expected, EngineeringNotation.Parse(text), 1e-12 * Math.Max(1, Math.Abs(expected)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("k10")]
    [InlineData("1.2.3")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("10k5")]
    [InlineData("1 k")]
    [InlineData("-")]
    [InlineData("1,5")]
    [InlineData("1e99999999999k")]
    [InlineData("1e999999999999999999999")]
    [InlineData("1e400")]
    public void InvalidInputIsRejectedWithChineseMessage(string text)
    {
        Assert.False(EngineeringNotation.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.MessageZh));
        Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", error.MessageZh);
        Assert.Throws<FormatException>(() => EngineeringNotation.Parse(text));
    }

    [Theory]
    [InlineData("1e-99999999999k")]
    [InlineData("1e-999999999999999999999u")]
    public void HugeNegativeExponentUnderflowsToZero(string text)
    {
        Assert.True(EngineeringNotation.TryParse(text, out var value, out _));
        Assert.Equal(0, value);
    }

    [Fact]
    public void ErrorReportsPositionOfOffendingCharacter()
    {
        Assert.False(EngineeringNotation.TryParse("10k5", out _, out var error));
        Assert.Equal(3, error.Position);
    }

    [Fact]
    public void NullIsRejected()
    {
        Assert.False(EngineeringNotation.TryParse(null, out _, out var error));
        Assert.Contains("空", error.MessageZh);
    }

    [Theory]
    [InlineData(4700, "Ω", "4.7kΩ")]
    [InlineData(1e-6, "F", "1µF")]
    [InlineData(2.2e6, null, "2.2meg")]
    [InlineData(0.001, "A", "1mA")]
    [InlineData(0, "V", "0V")]
    [InlineData(-3.3, "V", "-3.3V")]
    public void FormatUsesEngineeringSuffixes(double value, string? unit, string expected)
    {
        Assert.Equal(expected, EngineeringNotation.Format(value, unit));
    }

    [Fact]
    public void FormatThenParseRoundTripsWithinSignificantDigits()
    {
        foreach (var v in new[] { 1.234e-13, 5.678e-7, 9.99e2, 1.5e8, 47e3 })
        {
            var parsed = EngineeringNotation.Parse(EngineeringNotation.Format(v, null, 6));
            Assert.Equal(v, parsed, 1e-5 * Math.Abs(v));
        }
    }
}
