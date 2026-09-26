using YourLauncher.Core.Search;

namespace YourLauncher.Core.Tests.Search;

public class FuzzyScorerTests
{
    [Fact]
    public void Score_ExactMatch_IsExactTier()
    {
        var result = FuzzyScorer.Score("code", "code");

        Assert.NotNull(result);
        Assert.Equal(MatchTier.Exact, result!.Value.Tier);
        Assert.Equal(1000, result.Value.Score);
        Assert.Equal(new[] { 0, 1, 2, 3 }, result.Value.Positions);
    }

    [Fact]
    public void Score_PrefixMatch_IsPrefixTier()
    {
        var result = FuzzyScorer.Score("vsc", "vscode-notes.txt");

        Assert.NotNull(result);
        Assert.Equal(MatchTier.Prefix, result!.Value.Tier);
        Assert.Equal(new[] { 0, 1, 2 }, result.Value.Positions);
    }

    [Fact]
    public void Score_WordStartAcronym_IsWordStartTier()
    {
        var result = FuzzyScorer.Score("vsc", "visual studio code");

        Assert.NotNull(result);
        Assert.Equal(MatchTier.WordStart, result!.Value.Tier);
        Assert.Equal(new[] { 0, 7, 14 }, result.Value.Positions);
    }

    [Fact]
    public void Score_WordStartGreedyPrefixRuns_MatchesVisualStudioCode()
    {
        // "visco" -> "Vis"ual "Co"de: two runs, each a prefix starting at a word start.
        var result = FuzzyScorer.Score("visco", "visual studio code");

        Assert.NotNull(result);
        Assert.Equal(MatchTier.WordStart, result!.Value.Tier);
        Assert.Equal(new[] { 0, 1, 2, 14, 15 }, result.Value.Positions);
    }

    [Fact]
    public void Score_ContiguousMidWordOccurrence_IsSubstringTier()
    {
        // "sual" occurs inside "visual" but not at a word start.
        var result = FuzzyScorer.Score("sual", "visual studio");

        Assert.NotNull(result);
        Assert.Equal(MatchTier.Substring, result!.Value.Tier);
        Assert.Equal(new[] { 2, 3, 4, 5 }, result.Value.Positions);
    }

    [Fact]
    public void Score_ScatteredInOrderChars_IsFuzzyTier()
    {
        var result = FuzzyScorer.Score("ab", "xaxbx");

        Assert.NotNull(result);
        Assert.Equal(MatchTier.Fuzzy, result!.Value.Tier);
        Assert.Equal(new[] { 1, 3 }, result.Value.Positions);
    }

    [Fact]
    public void Score_NoInOrderMatch_ReturnsNull()
    {
        Assert.Null(FuzzyScorer.Score("ba", "ab"));
    }

    [Fact]
    public void Tiers_AreOrderedExactAbovePrefixAboveWordStartAboveSubstringAboveFuzzy()
    {
        var exact = FuzzyScorer.Score("code", "code")!.Value;
        var prefix = FuzzyScorer.Score("cod", "code review")!.Value;
        var wordStart = FuzzyScorer.Score("vsc", "visual studio code")!.Value;
        var substring = FuzzyScorer.Score("sual", "visual studio")!.Value;
        var fuzzy = FuzzyScorer.Score("ab", "xaxbx")!.Value;

        Assert.True(exact.Score > prefix.Score);
        Assert.True(prefix.Score > wordStart.Score);
        Assert.True(wordStart.Score > substring.Score);
        Assert.True(substring.Score > fuzzy.Score);
    }

    [Fact]
    public void Score_Vsc_PrefersVsCodeNotesOverVisualStudioCode_ButVisualStudioCodeBeatsScatteredOnly()
    {
        // Documented, expected ordering: a literal prefix match beats a word-start (acronym) match, even
        // though "Visual Studio Code" is the intuitively "better" candidate for a human. A scattered-only
        // (Fuzzy tier) candidate loses to both.
        var prefixHit = FuzzyScorer.Score("vsc", "vscode-notes.txt")!.Value;
        var wordStartHit = FuzzyScorer.Score("vsc", "visual studio code")!.Value;
        var fuzzyHit = FuzzyScorer.Score("vsc", "visual basic")!.Value;

        Assert.True(prefixHit.Score > wordStartHit.Score);
        Assert.True(wordStartHit.Score > fuzzyHit.Score);
    }

    [Fact]
    public void Score_MultiToken_RequiresAllTokensToMatch()
    {
        var result = FuzzyScorer.Score("sap dev", "sap dev system");

        Assert.NotNull(result);
        // "sap" is a Prefix match (text starts with it); "dev" is a WordStart match at index 4.
        Assert.Equal(MatchTier.WordStart, result!.Value.Tier);
    }

    [Fact]
    public void Score_MultiToken_MissingTokenFailsWholeMatch()
    {
        Assert.Null(FuzzyScorer.Score("sap nope", "sap dev system"));
    }

    [Fact]
    public void Score_CamelCaseWordStart_DetectedWhenWordStartsPrecomputedFromOriginalCasing()
    {
        var original = "VsCodeInsiders";
        var normalized = TextNormalizer.Normalize(original);
        var wordStarts = FuzzyScorer.ComputeWordStarts(original);

        // "vci" -> V(0) + C(2) + I(6), each at a camelCase boundary.
        var result = FuzzyScorer.Score("vci", normalized, wordStarts);

        Assert.NotNull(result);
        Assert.Equal(MatchTier.WordStart, result!.Value.Tier);
    }

    [Fact]
    public void Score_RaporFindsWordStartInHaftalikRapor_WithPositionsOverTheRaporChars()
    {
        var normalized = TextNormalizer.Normalize("Haftalık Rapor");
        var result = FuzzyScorer.Score("rapor", normalized);

        Assert.NotNull(result);
        Assert.Equal(MatchTier.WordStart, result!.Value.Tier);
        Assert.Equal(new[] { 9, 10, 11, 12, 13 }, result.Value.Positions);
    }
}
