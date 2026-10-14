using YourLauncher.Core.Providers;

namespace YourLauncher.Core.Tests.Search;

public class ResultMergerTests
{
    private static ProviderResult R(string id, ResultTier tier, int score = 0, string? title = null) => new()
    {
        Id = id,
        Title = title ?? id,
        Glyph = "x",
        Tier = tier,
        Score = score,
        Execute = () => true,
    };

    private static double NoUsage(string _) => 0;

    private static string[] Describe(IEnumerable<MergedEntry> merged) =>
        merged.Select(m => m.IsNode ? $"node{m.NodeIndex}" : m.Provider!.Id).ToArray();

    [Fact]
    public void Merge_UsesFixedTierOrder_RegardlessOfInputOrder()
    {
        var results = new[]
        {
            R("win:a", ResultTier.Windows, 100),
            R("sys:a", ResultTier.SystemCommands, 1),
            R("calc:result", ResultTier.Calculator, 1),
            R("file:a", ResultTier.Background, 999),
        };

        Assert.Equal(
            new[] { "calc:result", "node0", "node1", "sys:a", "win:a", "file:a" },
            Describe(ResultMerger.Merge(2, results, NoUsage)));
    }

    [Fact]
    public void Merge_NoProviders_ReturnsNodesInOriginalOrder()
    {
        Assert.Equal(new[] { "node0", "node1", "node2" }, Describe(ResultMerger.Merge(3, Array.Empty<ProviderResult>(), NoUsage)));
    }

    [Fact]
    public void Merge_WithinTier_ScoreThenUsageThenTitle()
    {
        var results = new[]
        {
            R("sys:b", ResultTier.SystemCommands, 5, "B"),
            R("sys:a", ResultTier.SystemCommands, 5, "A"),
            R("sys:used", ResultTier.SystemCommands, 5, "Z"),
            R("sys:top", ResultTier.SystemCommands, 9, "Y"),
        };

        var merged = ResultMerger.Merge(0, results, id => id == "sys:used" ? 3.0 : 0.0);

        Assert.Equal(new[] { "sys:top", "sys:used", "sys:a", "sys:b" }, Describe(merged));
    }

    [Fact]
    public void Merge_DropsNodesTierResultsAndDuplicateIds()
    {
        var results = new[]
        {
            R("x:fake", ResultTier.Nodes),
            R("sys:a", ResultTier.SystemCommands, 2),
            R("sys:a", ResultTier.SystemCommands, 1),
        };

        var merged = ResultMerger.Merge(1, results, NoUsage);

        Assert.Equal(new[] { "node0", "sys:a" }, Describe(merged));
        Assert.Equal(2, merged[1].Provider!.Score);
    }

    [Fact]
    public void OrderAppended_SortsBatchAndSkipsAlreadyShown()
    {
        var batch = new[]
        {
            R("file:b", ResultTier.Background, 1),
            R("file:a", ResultTier.Background, 2),
            R("file:shown", ResultTier.Background, 9),
        };

        var ordered = ResultMerger.OrderAppended(batch, NoUsage, new HashSet<string> { "file:shown" });

        Assert.Equal(new[] { "file:a", "file:b" }, ordered.Select(r => r.Id));
    }

    [Fact]
    public void Merge_RanksByUsageKey_WhileIdsStayDistinct()
    {
        // Two windows of one app have distinct ids (so neither is dropped as a duplicate) but share a usage key.
        var results = new[]
        {
            R("win:other:1", ResultTier.Windows, 5, "A"),
            new ProviderResult { Id = "win:chrome:2", UsageKey = "win:chrome", Title = "B", Glyph = "x", Tier = ResultTier.Windows, Score = 5, Execute = () => true },
            new ProviderResult { Id = "win:chrome:3", UsageKey = "win:chrome", Title = "C", Glyph = "x", Tier = ResultTier.Windows, Score = 5, Execute = () => true },
        };

        var merged = ResultMerger.Merge(0, results, key => key == "win:chrome" ? 2.0 : 0.0);

        Assert.Equal(new[] { "win:chrome:2", "win:chrome:3", "win:other:1" }, Describe(merged));
    }

    [Fact]
    public void ProviderResult_UsageKeyDefaultsToId_AndNeedsColon()
    {
        Assert.Equal("sys:lock", R("sys:lock", ResultTier.SystemCommands).UsageKey);
        Assert.Throws<ArgumentException>(() => new ProviderResult { Id = "win:a", UsageKey = "nocolon", Title = "t", Glyph = "x", Execute = () => true });
    }

    [Fact]
    public void ProviderResult_IdWithoutColon_Throws()
    {
        Assert.Throws<ArgumentException>(() => R("nocolon", ResultTier.SystemCommands));
    }
}
