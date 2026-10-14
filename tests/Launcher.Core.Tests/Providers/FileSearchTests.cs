using YourLauncher.Core.Config;
using YourLauncher.Core.Model;
using YourLauncher.Core.Providers;

namespace YourLauncher.Core.Tests.Providers;

public class FileSearchTests
{
    [Theory]
    [InlineData("f report", "f", "report")]
    [InlineData("F  report 2024 ", "f", "report 2024")]
    [InlineData("file my doc", "file", "my doc")]
    [InlineData("f\treport", "f", "report")]
    public void TryStripPrefix_AcceptsPrefixFollowedByWhitespaceAndText(string query, string prefix, string expected)
    {
        Assert.True(ProviderClaim.TryStripPrefix(query, prefix, out var inner));
        Assert.Equal(expected, inner);
    }

    [Theory]
    [InlineData("fox", "f")]        // no space after the prefix: an ordinary search
    [InlineData("f", "f")]          // nothing typed yet
    [InlineData("f   ", "f")]       // prefix and blanks only: stays a normal search
    [InlineData("report f", "f")]   // prefix must be at the start
    [InlineData("f report", "")]    // empty prefix switches the feature off
    [InlineData("f report", "  ")]
    public void TryStripPrefix_RejectsEverythingElse(string query, string prefix)
    {
        Assert.False(ProviderClaim.TryStripPrefix(query, prefix, out _));
    }

    [Fact]
    public void Pick_FirstClaimerInRegistrationOrderWins()
    {
        var plain = new FakeProvider(claims: false);
        var first = new FakeProvider(claims: true, inner: "one");
        var second = new FakeProvider(claims: true, inner: "two");

        var claim = ProviderClaim.Pick([plain, first, second], "q");

        Assert.NotNull(claim);
        Assert.Same(first, claim!.Value.Provider);
        Assert.Equal("one", claim.Value.InnerQuery);
    }

    [Fact]
    public void Pick_NoClaimerOrThrowingClaimer_GivesNullOrSkips()
    {
        Assert.Null(ProviderClaim.Pick([new FakeProvider(claims: false)], "q"));
        Assert.Null(ProviderClaim.Pick([], "q"));

        var good = new FakeProvider(claims: true, inner: "ok");
        var claim = ProviderClaim.Pick([new FakeProvider(claims: true, throws: true), good], "q");
        Assert.Same(good, claim!.Value.Provider);
    }

    [Fact]
    public void BuildWindowsSearchSql_OneLikePerTerm_QuotesDoubled()
    {
        var sql = FileSearchText.BuildWindowsSearchSql("report o'brien", 20);

        Assert.NotNull(sql);
        Assert.StartsWith("SELECT TOP 20 System.ItemPathDisplay FROM SystemIndex WHERE SCOPE='file:' AND ", sql);
        Assert.Contains("System.FileName LIKE '%report%'", sql);
        Assert.Contains("System.FileName LIKE '%o''brien%'", sql);
        Assert.Contains(" AND System.FileName LIKE ", sql);
    }

    [Fact]
    public void BuildWindowsSearchSql_QuoteInjectionStaysInsideTheLiteral()
    {
        var sql = FileSearchText.BuildWindowsSearchSql("x'--", 5)!;

        Assert.Contains("LIKE '%x''--%'", sql);
        Assert.Equal(0, sql.Replace("''", "").Count(c => c == '\'') % 2); // quotes still balance
    }

    [Fact]
    public void BuildWindowsSearchSql_SetWildcardBrackets_AreNeutralized()
    {
        var sql = FileSearchText.BuildWindowsSearchSql("a[b c]d [", 20)!;

        Assert.Contains("LIKE '%a_b%'", sql);
        Assert.Contains("LIKE '%c_d%'", sql);
        Assert.Contains("LIKE '%_%'", sql);
        Assert.DoesNotContain("[", sql);
        Assert.DoesNotContain("]", sql);
    }

    [Fact]
    public void BuildForLiteralPath_DoesNotExpandEnvironmentVariables()
    {
        var plan = YourLauncher.Core.Launch.CommandLineBuilder.BuildForLiteralPath(@"C:\a%TEMP%b\x.txt");

        Assert.Equal(@"C:\a%TEMP%b\x.txt", plan.FileName);
        Assert.True(plan.UseShellExecute);
        Assert.Null(plan.Arguments);
    }

    [Fact]
    public void BuildWindowsSearchSql_BlankQuery_IsNull()
    {
        Assert.Null(FileSearchText.BuildWindowsSearchSql("   ", 20));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Documents\report.pdf", "report.pdf", "C: › Users › me › Documents")]
    [InlineData(@"C:\Users\me\Documents\", "Documents", "C: › Users › me")]
    [InlineData(@"C:\readme.txt", "readme.txt", "C:")]
    [InlineData(@"C:\", @"C:\", "")]
    [InlineData(@"\\server\share\a.txt", "a.txt", "server › share")]
    public void NameAndSubtitle_FromFullPath(string path, string name, string subtitle)
    {
        Assert.Equal(name, FileSearchText.NameOf(path));
        Assert.Equal(subtitle, FileSearchText.ParentSubtitle(path));
    }

    [Fact]
    public void HighlightPositions_CoverMatchedTermsInTheName_NotFuzzyScatter()
    {
        Assert.Equal([0, 1, 2], FileSearchText.HighlightPositions("report.pdf", "rep"));
        Assert.Equal([0, 1, 2, 7, 8, 9], FileSearchText.HighlightPositions("report.pdf", "rep pdf"));
        Assert.Empty(FileSearchText.HighlightPositions("report.pdf", "zzz"));
        Assert.Empty(FileSearchText.HighlightPositions("notes.txt", "nts")); // scattered letters only
    }

    [Fact]
    public void Settings_FileSearchPrefix_DefaultsToFAndRoundTrips()
    {
        Assert.Equal("f", new Settings().FileSearchPrefix);

        var config = new LauncherConfig();
        config.Settings.FileSearchPrefix = "ara";
        var json = ConfigSerializer.Serialize(config);
        Assert.Contains("\"fileSearchPrefix\": \"ara\"", json);
        Assert.Equal("ara", ConfigSerializer.Deserialize(json).Config!.Settings.FileSearchPrefix);

        config.Settings.FileSearchPrefix = "";
        var off = ConfigSerializer.Deserialize(ConfigSerializer.Serialize(config)).Config!;
        Assert.Equal("", off.Settings.FileSearchPrefix); // empty (feature off) must survive, not fall back to "f"
    }

    [Fact]
    public void Settings_MissingFileSearchPrefix_KeepsTheDefault()
    {
        var result = ConfigSerializer.Deserialize("""{ "version": 1, "settings": { "hotkey": "Alt+Space" }, "root": { "id": "root", "type": "folder", "name": "Root", "children": [] } }""");

        Assert.True(result.Success, result.Error);
        Assert.Equal("f", result.Config!.Settings.FileSearchPrefix);
    }

    private sealed class FakeProvider(bool claims, string inner = "", bool throws = false) : IAsyncResultProvider
    {
        public int DebounceMs => 0;

        public Task<IReadOnlyList<ProviderResult>> QueryAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderResult>>([]);

        public bool TryClaim(string query, out string innerQuery)
        {
            if (throws)
            {
                throw new InvalidOperationException("boom");
            }

            innerQuery = inner;
            return claims;
        }
    }
}
