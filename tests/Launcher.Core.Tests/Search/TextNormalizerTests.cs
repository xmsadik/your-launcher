using YourLauncher.Core.Search;

namespace YourLauncher.Core.Tests.Search;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("IZMIR", "izmir")]
    [InlineData("İzmir", "izmir")]
    [InlineData("Şifre", "sifre")]
    [InlineData("ISIK", "isik")]
    [InlineData("IŞIK", "isik")]
    [InlineData("ışık", "isik")]
    [InlineData("ÜÖÇĞ", "uocg")]
    public void Normalize_FoldsTurkishCasingAndDiacritics(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_PreservesLength_ForMixedString()
    {
        const string input = "İzmir'de Şifre Yöneticisi - Çanta.exe";
        var result = TextNormalizer.Normalize(input);

        Assert.Equal(input.Length, result.Length);
    }

    [Fact]
    public void Normalize_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", TextNormalizer.Normalize(""));
    }

    [Fact]
    public void Normalize_IsIdempotentOnAlreadyLowercaseAscii()
    {
        Assert.Equal("visual studio code", TextNormalizer.Normalize("visual studio code"));
    }
}
