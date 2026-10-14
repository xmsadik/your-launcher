using YourLauncher.Core.Launch;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Search;

/// <summary>A parameterized node recognized from search text such as "jira ABC-123".</summary>
/// <param name="Node">The query node (its URL, command or app arguments contain <c>{query}</c>).</param>
/// <param name="Query">Everything after the trigger word, trimmed; never empty.</param>
/// <param name="ParentChain">Root through the node's parent, like <see cref="SearchResult.ParentChain"/>.</param>
/// <param name="Breadcrumb">Ancestor folder names, like <see cref="SearchResult.Breadcrumb"/>.</param>
public sealed record QueryTriggerMatch(
    Node Node,
    string Query,
    IReadOnlyList<FolderNode> ParentChain,
    string Breadcrumb);

/// <summary>
/// Recognizes "&lt;trigger&gt; &lt;query&gt;" search text (design rule 6). The trigger is the FIRST
/// whitespace-separated word; it must equal - Turkish-aware and case-insensitive, via
/// <see cref="TextNormalizer"/> - one of a query node's keywords or its whole name, and the query part
/// must not be blank. Pure and index-based so it is unit-testable and costs one pass over the entries.
/// </summary>
public static class QueryTrigger
{
    private static readonly char[] Whitespace = [' ', '\t', '\r', '\n'];

    /// <summary>All query nodes whose keyword/name equals the first word, in tree order (so the user controls ties by ordering). Empty when the text has no query part.</summary>
    public static IReadOnlyList<QueryTriggerMatch> Match(FlatIndex index, string searchText)
    {
        if (!TrySplit(searchText, out var word, out var query))
        {
            return Array.Empty<QueryTriggerMatch>();
        }

        var normalizedWord = TextNormalizer.Normalize(word);
        List<QueryTriggerMatch>? matches = null;
        foreach (var entry in index.Entries)
        {
            if (!QueryTemplate.IsQueryNode(entry.Node))
            {
                continue;
            }

            if (entry.NormalizedName == normalizedWord || entry.NormalizedKeywords.Contains(normalizedWord))
            {
                matches ??= new List<QueryTriggerMatch>();
                matches.Add(new QueryTriggerMatch(entry.Node, query, entry.ParentChain, entry.Breadcrumb));
            }
        }

        return matches ?? (IReadOnlyList<QueryTriggerMatch>)Array.Empty<QueryTriggerMatch>();
    }

    /// <summary>Splits at the first whitespace run: leading whitespace is ignored, the rest is trimmed. False when there is no word or no non-blank remainder.</summary>
    public static bool TrySplit(string searchText, out string word, out string query)
    {
        word = "";
        query = "";

        var text = searchText.Trim();
        var split = text.IndexOfAny(Whitespace);
        if (split <= 0)
        {
            return false;
        }

        var rest = text[(split + 1)..].Trim();
        if (rest.Length == 0)
        {
            return false;
        }

        word = text[..split];
        query = rest;
        return true;
    }
}
