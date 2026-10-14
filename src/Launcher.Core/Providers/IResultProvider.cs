namespace YourLauncher.Core.Providers;

/// <summary>
/// A fast, in-memory source of extra rows (calculator, system commands, window switcher). Called on the UI
/// thread for every keystroke, so it must return within a frame or two; anything slower belongs in an
/// <see cref="IAsyncResultProvider"/>.
/// </summary>
public interface IResultProvider
{
    IReadOnlyList<ProviderResult> Query(string query);
}

/// <summary>
/// A slow source (file search, plugins). The panel waits <see cref="DebounceMs"/> after the last keystroke,
/// cancels the previous call on every new keystroke, and appends whatever comes back to the END of the list
/// without reordering or moving the selection.
/// </summary>
public interface IAsyncResultProvider
{
    /// <summary>Quiet time after the last keystroke before <see cref="QueryAsync"/> is called.</summary>
    int DebounceMs { get; }

    Task<IReadOnlyList<ProviderResult>> QueryAsync(string query, CancellationToken cancellationToken);

    /// <summary>
    /// A prefix-triggered provider ("f report" for file search, a plugin keyword) claims the whole query:
    /// return true with <paramref name="innerQuery"/> = the text after the prefix. While a provider claims the
    /// query the panel shows only that provider's rows (no node search, no other providers) and passes
    /// <paramref name="innerQuery"/> to <see cref="QueryAsync"/>. Providers that are not prefix-triggered keep
    /// the default (never claim) and get the raw query.
    /// </summary>
    bool TryClaim(string query, out string innerQuery)
    {
        innerQuery = query;
        return false;
    }

    /// <summary>
    /// True for a provider that only ever has rows for a query it claimed (file search): the panel then never
    /// calls <see cref="QueryAsync"/> with a raw, unclaimed query, where the text is not what the provider
    /// expects. Defaults to false (the provider also answers plain queries).
    /// </summary>
    bool ClaimOnly => false;

    /// <summary>Shown as the single row while a claimed query is waiting for results, e.g. "Searching files…"; null shows nothing.</summary>
    string? PendingText => null;
}
