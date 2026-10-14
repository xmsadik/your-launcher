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
}
