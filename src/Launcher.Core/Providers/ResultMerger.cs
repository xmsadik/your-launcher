using System.Globalization;

namespace YourLauncher.Core.Providers;

/// <summary>
/// One slot in the merged list: either the i-th search-engine node result (<see cref="NodeIndex"/> >= 0) or a
/// provider row. Keeping nodes as an index lets the caller map back to its own node view models.
/// </summary>
public readonly record struct MergedEntry(int NodeIndex, ProviderResult? Provider)
{
    public bool IsNode => Provider is null;
}

/// <summary>Pure ordering rules for the search-mode list (design rule 3); no UI, no I/O.</summary>
public static class ResultMerger
{
    private static readonly StringComparer TitleComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    /// <summary>
    /// Calculator first, then <paramref name="nodeCount"/> node results in the order the search engine gave
    /// them, then system commands, then windows (then any sync background results). Inside a provider tier:
    /// score desc, usage (by <see cref="ProviderResult.UsageKey"/>) desc, title. Results claiming <see cref="ResultTier.Nodes"/> are dropped: node rows
    /// come only from the search engine. A duplicate <see cref="ProviderResult.Id"/> keeps its first occurrence.
    /// </summary>
    public static IReadOnlyList<MergedEntry> Merge(int nodeCount, IEnumerable<ProviderResult> providerResults, Func<string, double> usage)
    {
        var ordered = Order(providerResults, usage);
        var merged = new List<MergedEntry>(nodeCount + 8);

        AddTier(ResultTier.Calculator);

        for (var i = 0; i < nodeCount; i++)
        {
            merged.Add(new MergedEntry(i, null));
        }

        AddTier(ResultTier.SystemCommands);
        AddTier(ResultTier.Windows);
        AddTier(ResultTier.Background);
        return merged;

        void AddTier(ResultTier tier)
        {
            foreach (var result in ordered.Where(r => r.Tier == tier))
            {
                merged.Add(new MergedEntry(-1, result));
            }
        }
    }

    /// <summary>
    /// Orders one batch of late (asynchronous) results the same way as inside a tier. The caller appends the
    /// batch after everything already shown and never reorders what is there; ids in
    /// <paramref name="alreadyShown"/> are skipped so a result is never listed twice.
    /// </summary>
    public static IReadOnlyList<ProviderResult> OrderAppended(
        IEnumerable<ProviderResult> results, Func<string, double> usage, ISet<string>? alreadyShown = null) =>
        Order(results.Where(r => alreadyShown is null || !alreadyShown.Contains(r.Id)), usage);

    private static List<ProviderResult> Order(IEnumerable<ProviderResult> results, Func<string, double> usage)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return results
            .Where(r => r.Tier != ResultTier.Nodes && seen.Add(r.Id))
            .OrderBy(r => r.Tier)
            .ThenByDescending(r => r.Score)
            .ThenByDescending(r => usage(r.UsageKey))
            .ThenBy(r => r.Title, TitleComparer)
            .ToList();
    }
}
