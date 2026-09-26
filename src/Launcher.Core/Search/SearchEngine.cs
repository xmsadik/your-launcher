using System.Globalization;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Search;

/// <summary>Which field produced a result's winning score, used to decide whether to highlight the name.</summary>
public enum MatchedField
{
    Name,
    Keywords,
    Description,
    Command,
}

/// <summary>One ranked search hit (spec §7).</summary>
public sealed class SearchResult
{
    public required Node Node { get; init; }
    public required IReadOnlyList<FolderNode> ParentChain { get; init; }
    public required string Breadcrumb { get; init; }
    public required int Depth { get; init; }
    public required int Score { get; init; }
    public required MatchTier Tier { get; init; }
    public required MatchedField MatchedField { get; init; }

    /// <summary>Matched char indices into the node's (length-preserving-normalized) name; empty unless <see cref="MatchedField"/> is Name.</summary>
    public required IReadOnlyList<int> NamePositions { get; init; }
}

/// <summary>
/// Whole-tree search over a <see cref="FlatIndex"/> (spec §7). Each node is scored on name (weight 1.0),
/// best-matching keyword (0.8), description (0.6) and command text (0.5, CommandNode only); the best
/// weighted field wins. Sort: score desc, then usage desc, then depth asc, then Turkish-aware alphabetical.
/// </summary>
public sealed class SearchEngine
{
    private const int NameWeight = 10;
    private const int KeywordWeight = 8;
    private const int DescriptionWeight = 6;
    private const int CommandWeight = 5;

    private static readonly StringComparer TurkishIgnoreCase =
        StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    /// <summary>Usage score per node id (higher = used more/more recently), for the tie-breaker only. Defaults to 0 for every node until Phase 6 wires up usage.json.</summary>
    private readonly Func<string, double>? _usageScore;

    public SearchEngine(Func<string, double>? usageScore = null) => _usageScore = usageScore;

    public IReadOnlyList<SearchResult> Search(FlatIndex index, string query, int limit = 50)
    {
        var normalizedQuery = TextNormalizer.Normalize(query).Trim();
        if (normalizedQuery.Length == 0)
        {
            return Array.Empty<SearchResult>();
        }

        var hits = new List<(FlatIndexEntry Entry, MatchResult Match, MatchedField Field, int WeightedScore)>();
        foreach (var entry in index.Entries)
        {
            var hit = ScoreEntry(entry, normalizedQuery);
            if (hit is not null)
            {
                hits.Add(hit.Value);
            }
        }

        return hits
            .OrderByDescending(h => h.WeightedScore)
            .ThenByDescending(h => _usageScore?.Invoke(h.Entry.Node.Id) ?? 0.0)
            .ThenBy(h => h.Entry.Depth)
            .ThenBy(h => h.Entry.Node.Name, TurkishIgnoreCase)
            .Take(limit)
            .Select(h => new SearchResult
            {
                Node = h.Entry.Node,
                ParentChain = h.Entry.ParentChain,
                Breadcrumb = h.Entry.Breadcrumb,
                Depth = h.Entry.Depth,
                Score = h.WeightedScore,
                Tier = h.Match.Tier,
                MatchedField = h.Field,
                NamePositions = h.Field == MatchedField.Name ? h.Match.Positions : Array.Empty<int>(),
            })
            .ToList();
    }

    private static (FlatIndexEntry, MatchResult, MatchedField, int)? ScoreEntry(FlatIndexEntry entry, string normalizedQuery)
    {
        (MatchResult Match, MatchedField Field, int Weighted)? best = null;

        var nameMatch = FuzzyScorer.Score(normalizedQuery, entry.NormalizedName, entry.NameWordStarts);
        if (nameMatch is not null)
        {
            best = Consider(best, nameMatch.Value, MatchedField.Name, NameWeight);
        }

        foreach (var keyword in entry.NormalizedKeywords)
        {
            var keywordMatch = FuzzyScorer.Score(normalizedQuery, keyword);
            if (keywordMatch is not null)
            {
                best = Consider(best, keywordMatch.Value, MatchedField.Keywords, KeywordWeight);
            }
        }

        if (entry.NormalizedDescription.Length > 0)
        {
            var descriptionMatch = FuzzyScorer.Score(normalizedQuery, entry.NormalizedDescription);
            if (descriptionMatch is not null)
            {
                best = Consider(best, descriptionMatch.Value, MatchedField.Description, DescriptionWeight);
            }
        }

        if (entry.NormalizedCommand.Length > 0)
        {
            var commandMatch = FuzzyScorer.Score(normalizedQuery, entry.NormalizedCommand);
            if (commandMatch is not null)
            {
                best = Consider(best, commandMatch.Value, MatchedField.Command, CommandWeight);
            }
        }

        return best is null ? null : (entry, best.Value.Match, best.Value.Field, best.Value.Weighted);
    }

    private static (MatchResult Match, MatchedField Field, int Weighted) Consider(
        (MatchResult Match, MatchedField Field, int Weighted)? current, MatchResult candidate, MatchedField field, int weight)
    {
        var weighted = candidate.Score * weight / 10;
        return current is null || weighted > current.Value.Weighted
            ? (candidate, field, weighted)
            : current.Value;
    }
}
