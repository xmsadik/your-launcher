using YourLauncher.Core.Search;

namespace YourLauncher.Core.Providers;

/// <summary>
/// Pure text helpers for file search rows, kept out of the App layer so they are unit tested: building the
/// Windows Search SQL (the one place user text is spliced into a query string, hence the escaping), and
/// turning a full path into the row's title, subtitle and highlight.
/// </summary>
public static class FileSearchText
{
    /// <summary>The query split on whitespace; every term must match (AND), in any order.</summary>
    public static string[] Terms(string query) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// SQL for the Windows Search index: file-name substring match per term (<c>LIKE '%term%'</c>), newest
    /// first, limited to the file system (<c>SCOPE='file:'</c> leaves out Outlook/IE history entries). "[" and "]" (set wildcards; a lone one is a syntax error) become "_". The only
    /// character that can break out of the string literal is the quote, which is doubled; <c>%</c> and
    /// <c>_</c> stay LIKE wildcards, which only widens a match and cannot alter the statement. Null when the
    /// query has no terms.
    /// </summary>
    public static string? BuildWindowsSearchSql(string query, int maxResults)
    {
        var terms = Terms(query);
        if (terms.Length == 0)
        {
            return null;
        }

        var conditions = string.Join(
            " AND ",
            terms.Select(t => $"System.FileName LIKE '%{t.Replace("'", "''").Replace('[', '_').Replace(']', '_')}%'"));
        return $"SELECT TOP {Math.Max(1, maxResults)} System.ItemPathDisplay FROM SystemIndex " +
               $"WHERE SCOPE='file:' AND {conditions} ORDER BY System.DateModified DESC";
    }

    /// <summary>File or folder name of a full path; a drive root ("C:\") has none and shows as itself.</summary>
    public static string NameOf(string fullPath)
    {
        var trimmed = fullPath.TrimEnd('\\', '/');
        var name = trimmed.Length == 0 ? "" : Path.GetFileName(trimmed);
        return name.Length == 0 ? fullPath : name;
    }

    /// <summary>
    /// The parent folder as "C: › Users › me › Documents" - the separator the list's path control trims on,
    /// so a long path keeps its most specific end visible. Empty for a drive root.
    /// </summary>
    public static string ParentSubtitle(string fullPath)
    {
        var parent = Path.GetDirectoryName(fullPath.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(parent)
            ? ""
            : string.Join(PathTrimmer.Separator, parent.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Indices into <paramref name="name"/> covered by the query terms (exact/prefix/substring matches only; a
    /// scattered fuzzy match would light up random letters). Empty when nothing matched in the name itself,
    /// e.g. the term hit the folder part of the path.
    /// </summary>
    public static IReadOnlyList<int> HighlightPositions(string name, string query)
    {
        var normalizedName = TextNormalizer.Normalize(name);
        if (normalizedName.Length != name.Length)
        {
            return []; // positions would not map back onto the displayed name.
        }

        var positions = new SortedSet<int>();
        foreach (var term in Terms(query))
        {
            var match = FuzzyScorer.Score(TextNormalizer.Normalize(term), normalizedName);
            if (match is { Tier: not MatchTier.Fuzzy } hit)
            {
                positions.UnionWith(hit.Positions);
            }
        }

        return positions.ToArray();
    }
}
