using YourLauncher.Core.Usage;

namespace YourLauncher.Core.Tests.Usage;

public class UsageScorerTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Record_NewNode_CreatesEntryWithCountOne()
    {
        var data = new UsageData();
        UsageScorer.Record(data, "a", Now);

        Assert.Equal(1, data.Entries["a"].UseCount);
        Assert.Equal(Now, data.Entries["a"].LastUsedUtc);
    }

    [Fact]
    public void Record_ExistingNode_IncrementsCountAndBumpsLastUsed()
    {
        var data = new UsageData();
        UsageScorer.Record(data, "a", Now.AddDays(-2));
        UsageScorer.Record(data, "a", Now);

        Assert.Equal(2, data.Entries["a"].UseCount);
        Assert.Equal(Now, data.Entries["a"].LastUsedUtc);
    }

    [Fact]
    public void Score_UnknownNode_IsZero()
    {
        var data = new UsageData();
        Assert.Equal(0, UsageScorer.Score(data, "missing", Now));
    }

    [Theory]
    [InlineData(0, 4.0)]
    [InlineData(1, 4.0)]
    [InlineData(1.5, 2.0)]
    [InlineData(7, 2.0)]
    [InlineData(10, 1.0)]
    [InlineData(30, 1.0)]
    [InlineData(60, 0.5)]
    public void Score_AgeBuckets_ApplyExpectedWeight(double ageDays, double expectedWeight)
    {
        var data = new UsageData();
        data.Entries["a"] = new UsageEntry { UseCount = 1, LastUsedUtc = Now.AddDays(-ageDays) };

        Assert.Equal(expectedWeight, UsageScorer.Score(data, "a", Now));
    }

    [Fact]
    public void Score_HigherUseCountSameAge_OutranksLower()
    {
        var data = new UsageData();
        data.Entries["frequent"] = new UsageEntry { UseCount = 10, LastUsedUtc = Now.AddHours(-1) };
        data.Entries["rare"] = new UsageEntry { UseCount = 1, LastUsedUtc = Now.AddHours(-1) };

        Assert.True(UsageScorer.Score(data, "frequent", Now) > UsageScorer.Score(data, "rare", Now));
    }

    [Fact]
    public void Score_RecentLowCount_CanOutrankStaleHighCount()
    {
        var data = new UsageData();
        data.Entries["recent"] = new UsageEntry { UseCount = 1, LastUsedUtc = Now.AddHours(-1) }; // weight 4 -> 4
        data.Entries["stale"] = new UsageEntry { UseCount = 3, LastUsedUtc = Now.AddDays(-60) }; // weight 0.5 -> 1.5

        Assert.True(UsageScorer.Score(data, "recent", Now) > UsageScorer.Score(data, "stale", Now));
    }

    [Fact]
    public void Score_IsDeterministic_ForFixedNow()
    {
        var data = new UsageData();
        UsageScorer.Record(data, "a", Now.AddDays(-3));
        UsageScorer.Record(data, "a", Now.AddDays(-1));

        var first = UsageScorer.Score(data, "a", Now);
        var second = UsageScorer.Score(data, "a", Now);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Prune_RemovesEntriesNotInExistingIds()
    {
        var data = new UsageData();
        data.Entries["kept"] = new UsageEntry { UseCount = 1, LastUsedUtc = Now };
        data.Entries["gone"] = new UsageEntry { UseCount = 1, LastUsedUtc = Now };

        UsageScorer.Prune(data, new[] { "kept" });

        Assert.True(data.Entries.ContainsKey("kept"));
        Assert.False(data.Entries.ContainsKey("gone"));
    }

    [Fact]
    public void Prune_EmptyExistingIds_RemovesEverything()
    {
        var data = new UsageData();
        data.Entries["a"] = new UsageEntry { UseCount = 1, LastUsedUtc = Now };

        UsageScorer.Prune(data, Array.Empty<string>());

        Assert.Empty(data.Entries);
    }

    [Fact]
    public void Deserialize_CorruptJson_ReturnsEmpty()
    {
        var data = UsageSerializer.Deserialize("{ this is not json");
        Assert.Empty(data.Entries);
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsEmpty()
    {
        var data = UsageSerializer.Deserialize("");
        Assert.Empty(data.Entries);
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTrips()
    {
        var data = new UsageData();
        UsageScorer.Record(data, "a", Now);
        UsageScorer.Record(data, "a", Now);
        UsageScorer.Record(data, "b", Now.AddDays(-5));

        var json = UsageSerializer.Serialize(data);
        var roundTripped = UsageSerializer.Deserialize(json);

        Assert.Equal(2, roundTripped.Entries["a"].UseCount);
        Assert.Equal(1, roundTripped.Entries["b"].UseCount);
        Assert.Equal(Now, roundTripped.Entries["a"].LastUsedUtc);
    }
}
