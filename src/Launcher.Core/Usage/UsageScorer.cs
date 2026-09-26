namespace YourLauncher.Core.Usage;

/// <summary>
/// Pure frecency scoring over <see cref="UsageData"/> (spec §8.4): used only as a search-ranking tie
/// breaker (<c>SearchEngine</c>'s usage-score parameter) - it never affects navigation-mode order, and it
/// never runs before <c>useCount</c>/recency within a search result set already sorted by match tier and
/// field weight.
/// </summary>
public static class UsageScorer
{
    /// <summary>Records one launch of <paramref name="nodeId"/> at <paramref name="nowUtc"/> - increments useCount, bumps lastUsedUtc.</summary>
    public static void Record(UsageData data, string nodeId, DateTime nowUtc)
    {
        if (data.Entries.TryGetValue(nodeId, out var entry))
        {
            entry.UseCount++;
            entry.LastUsedUtc = nowUtc;
        }
        else
        {
            data.Entries[nodeId] = new UsageEntry { UseCount = 1, LastUsedUtc = nowUtc };
        }
    }

    /// <summary>
    /// Deterministic frecency score for a node at <paramref name="nowUtc"/>: useCount weighted by how
    /// recently it was last used (age buckets, spec-chosen since the spec itself only asks for "a
    /// deterministic frecency" without prescribing the formula): ≤1 day old ×4, ≤7 days ×2, ≤30 days ×1,
    /// older ×0.5. A node with no recorded usage scores 0 (spec: ties otherwise fall through to depth then
    /// alphabetical, unaffected by usage).
    /// </summary>
    public static double Score(UsageData data, string nodeId, DateTime nowUtc)
    {
        if (!data.Entries.TryGetValue(nodeId, out var entry))
        {
            return 0;
        }

        var ageDays = (nowUtc - entry.LastUsedUtc).TotalDays;
        var weight = ageDays switch
        {
            <= 1 => 4.0,
            <= 7 => 2.0,
            <= 30 => 1.0,
            _ => 0.5,
        };

        return entry.UseCount * weight;
    }

    /// <summary>Drops every recorded entry whose node id no longer exists in the tree (called after a delete or an import, and once at startup).</summary>
    public static void Prune(UsageData data, IReadOnlyCollection<string> existingIds)
    {
        var existing = existingIds as HashSet<string> ?? new HashSet<string>(existingIds);
        foreach (var id in data.Entries.Keys.Where(id => !existing.Contains(id)).ToList())
        {
            data.Entries.Remove(id);
        }
    }
}
