namespace YourLauncher.Core.Providers;

/// <summary>The provider that claimed a query and the text it should be queried with.</summary>
public readonly record struct ProviderClaimResult(IAsyncResultProvider Provider, string InnerQuery);

/// <summary>
/// Decides which prefix-triggered async provider (file search, plugin keyword) owns a query. Pure so the
/// "first claimer in registration order wins" rule is unit tested without the panel.
/// </summary>
public static class ProviderClaim
{
    /// <summary>The first provider whose <see cref="IAsyncResultProvider.TryClaim"/> accepts <paramref name="query"/>; null when none does. A provider that throws is skipped.</summary>
    public static ProviderClaimResult? Pick(IEnumerable<IAsyncResultProvider> providers, string query)
    {
        foreach (var provider in providers)
        {
            try
            {
                if (provider.TryClaim(query, out var inner))
                {
                    return new ProviderClaimResult(provider, inner);
                }
            }
            catch (Exception)
            {
                // A broken plugin must not take the whole search box down; it simply does not claim.
            }
        }

        return null;
    }

    /// <summary>
    /// "f report" with prefix "f" gives true and "report". The prefix must be followed by whitespace (so "fox"
    /// is not a file search for "ox"), is compared case-insensitively, and the remainder is trimmed. False for
    /// an empty prefix (feature off) and for a bare "f " with nothing after it, which stays a normal search.
    /// </summary>
    public static bool TryStripPrefix(string query, string prefix, out string inner)
    {
        inner = "";
        prefix = prefix.Trim();
        if (prefix.Length == 0
            || query.Length <= prefix.Length
            || !query.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !char.IsWhiteSpace(query[prefix.Length]))
        {
            return false;
        }

        var rest = query[prefix.Length..].Trim();
        if (rest.Length == 0)
        {
            return false;
        }

        inner = rest;
        return true;
    }
}
