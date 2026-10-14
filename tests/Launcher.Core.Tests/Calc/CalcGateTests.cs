using YourLauncher.Core.Calc;

namespace YourLauncher.Core.Tests.Calc;

public class CalcGateTests
{
    [Theory]
    [InlineData("2+3")]
    [InlineData("2 + 3")]
    [InlineData("3,5*2")]
    [InlineData("48/2")]
    [InlineData("7%3")]
    [InlineData("2^8")]
    [InlineData("sqrt(16)")]
    [InlineData("abs(-3)")]
    [InlineData("pi*2")]
    [InlineData("0x1F+1")]
    [InlineData("(1+2)*3")]
    public void LooksLikeMath_ProducesResult(string query)
    {
        Assert.NotNull(CalcGate.TryEvaluate(query));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notepad")]
    [InlineData("2048")]
    [InlineData("2048 game")]
    [InlineData("3,5")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("(5)")]
    [InlineData("((5))")]
    [InlineData("pi")]
    [InlineData("e")]
    [InlineData("0x1F")]
    [InlineData("0b101")]
    [InlineData("7-zip")]
    [InlineData("vs-code")]
    [InlineData("x264")]
    [InlineData("c++")]
    [InlineData("192.168.1.1")]
    [InlineData("1.2.3")]
    [InlineData("3d")]
    [InlineData("windows 11")]
    [InlineData("office 365")]
    [InlineData("sqrt")]
    [InlineData("log")]
    [InlineData("sin cos")]
    [InlineData("5+")]
    [InlineData("1/0")]
    [InlineData("a+b")]
    [InlineData("C:\\Users")]
    [InlineData("https://example.com/a-b")]
    [InlineData("100%")]
    [InlineData("--")]
    [InlineData("+++")]
    [InlineData("()")]
    public void NotMath_ProducesNoRow(string query)
    {
        Assert.Null(CalcGate.TryEvaluate(query));
    }

    [Fact]
    public void KnownResidualFalsePositives_AreReallyArithmetic()
    {
        // Documented trade-off: these are valid arithmetic, so they do get a row.
        Assert.Equal(2000, CalcGate.TryEvaluate("2024-10-14")!.Value.Value);
        Assert.Equal(-679, CalcGate.TryEvaluate("555-1234")!.Value.Value);
    }
}
