using System.Globalization;
using YourLauncher.Core.Calc;

namespace YourLauncher.Core.Tests.Calc;

public class CalcEvaluatorTests
{
    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("10-4-3", 3)]
    [InlineData("100/4/5", 5)]
    [InlineData("7%3", 1)]
    [InlineData("10 % 4 * 3", 6)]
    [InlineData("2^10", 1024)]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("2^-1", 0.5)]
    [InlineData("(-2)^2", 4)]
    [InlineData("-(3+4)", -7)]
    [InlineData("--3+1", 4)]
    [InlineData("2*-3", -6)]
    [InlineData("1.5+2.25", 3.75)]
    [InlineData(".5*4", 2)]
    [InlineData("3,5*2", 7)]
    [InlineData("1,5+1,5", 3)]
    [InlineData("1,000+1", 2)]
    [InlineData("0x1F+1", 32)]
    [InlineData("0XfF-0b11", 252)]
    [InlineData("0b101*2", 10)]
    [InlineData("sqrt(16)+1", 5)]
    [InlineData("abs(-4.5)*2", 9)]
    [InlineData("round(2.5)+0", 3)]
    [InlineData("round(-2.5)+0", -3)]
    [InlineData("floor(2.9)+ceil(2.1)", 5)]
    [InlineData("SQRT(9)*1", 3)]
    [InlineData("log(1000)+0", 3)]
    [InlineData("ln(e)*1", 1)]
    [InlineData("sin(0)+cos(0)", 1)]
    [InlineData("  2  +  2  ", 4)]
    public void Evaluate_ComputesExpectedValue(string expression, double expected)
    {
        var result = CalcEvaluator.Evaluate(expression);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Value.Value, 9);
    }

    [Fact]
    public void Evaluate_Constants()
    {
        Assert.Equal(Math.PI * 2, CalcEvaluator.Evaluate("pi*2")!.Value.Value, 12);
        Assert.Equal(Math.E + 1, CalcEvaluator.Evaluate("e+1")!.Value.Value, 12);
    }

    [Fact]
    public void Evaluate_NegativeZeroIsPlainZero()
    {
        var value = CalcEvaluator.Evaluate("-0*1")!.Value.Value;

        Assert.Equal("0", CalcEvaluator.Format(value));
        Assert.False(double.IsNegative(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1/0")]
    [InlineData("5%0")]
    [InlineData("1/(2-2)")]
    [InlineData("sqrt(-1)")]
    [InlineData("log(0)")]
    [InlineData("ln(-3)")]
    [InlineData("10^1000")]
    [InlineData("2+")]
    [InlineData("+")]
    [InlineData("*3")]
    [InlineData("(2+3")]
    [InlineData("2+3)")]
    [InlineData("()")]
    [InlineData("2 3")]
    [InlineData("2(3)")]
    [InlineData("2pi")]
    [InlineData("pi2")]
    [InlineData("1.2.3")]
    [InlineData("1,2,3")]
    [InlineData("192.168.1.1")]
    [InlineData("foo+1")]
    [InlineData("sqrt 4")]
    [InlineData("sqrt()")]
    [InlineData("max(1,2)")]
    [InlineData("0x")]
    [InlineData("0xG")]
    [InlineData("0b102")]
    [InlineData("0x1FFFFFFFFFFFFFFF+1")]
    [InlineData("2 ** 3")]
    [InlineData("1e5+1")]
    [InlineData("5 + $3")]
    public void Evaluate_InvalidOrUndefined_ReturnsNull(string expression)
    {
        Assert.Null(CalcEvaluator.Evaluate(expression));
    }

    [Fact]
    public void Evaluate_DeepNesting_DoesNotOverflowStack()
    {
        var deep = new string('(', 5000) + "1" + new string(')', 5000);
        var unary = new string('-', 5000) + "1";

        Assert.Null(CalcEvaluator.Evaluate(deep));
        Assert.Null(CalcEvaluator.Evaluate(unary));
        Assert.Null(CalcEvaluator.Evaluate(new string('1', 300)));
    }

    [Fact]
    public void Evaluate_ReportsStructureCounts()
    {
        var result = CalcEvaluator.Evaluate("sqrt(4)+pi*2")!.Value;

        Assert.Equal(2, result.BinaryOperators);
        Assert.Equal(1, result.FunctionCalls);
        Assert.Equal(3, result.Operands);
    }

    [Theory]
    [InlineData(48, "48")]
    [InlineData(-7, "-7")]
    [InlineData(0, "0")]
    [InlineData(3.5, "3.5")]
    [InlineData(0.30000000000000004, "0.3")]
    [InlineData(1.0 / 3.0, "0.333333333333")]
    [InlineData(123456789012345, "123456789012345")]
    [InlineData(1e20, "1E+20")]
    public void Format_UsesInvariantAndTrimsNoise(double value, string expected)
    {
        Assert.Equal(expected, CalcEvaluator.Format(value));
    }

    [Theory]
    [InlineData(48, "0x30")]
    [InlineData(255, "0xFF")]
    [InlineData(0, "0x0")]
    [InlineData(-1, null)]
    [InlineData(2.5, null)]
    [InlineData(1e19, null)]
    public void FormatHex_OnlyForNonNegativeIntegers(double value, string? expected)
    {
        Assert.Equal(expected, CalcEvaluator.FormatHex(value));
    }

    [Fact]
    public void Format_IgnoresCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("3.5", CalcEvaluator.Format(3.5));
            Assert.Equal(7, CalcEvaluator.Evaluate("3,5*2")!.Value.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
