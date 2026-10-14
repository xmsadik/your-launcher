using YourLauncher.Core.Search;

namespace YourLauncher.Core.Plugins;

/// <summary>Decides whether a typed query belongs to a plugin ("gh foo" belongs to keyword "gh").</summary>
public static class PluginQueryMatcher
{
    /// <summary>
    /// True when <paramref name="query"/> starts with "&lt;keyword&gt; " (Turkish-aware, case-insensitive via
    /// <see cref="TextNormalizer"/>, which maps chars 1:1 so indices carry over). <paramref name="inner"/> is the
    /// text after the keyword with leading blanks removed; it may be empty ("gh " still claims, the plugin gets
    /// ""). A bare keyword without the space does not claim - the user is still typing it.
    /// </summary>
    public static bool TryMatch(string keyword, string query, out string inner)
    {
        inner = "";
        var prefixLength = keyword.Length + 1;
        if (query.Length < prefixLength || !char.IsWhiteSpace(query[keyword.Length]))
        {
            return false;
        }

        if (!string.Equals(TextNormalizer.Normalize(query[..keyword.Length]), TextNormalizer.Normalize(keyword), StringComparison.Ordinal))
        {
            return false;
        }

        inner = query[prefixLength..].TrimStart();
        return true;
    }
}
