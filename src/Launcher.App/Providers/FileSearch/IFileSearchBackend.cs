namespace YourLauncher.App.Providers.FileSearch;

/// <summary>
/// One way of finding files by name. <see cref="FileSearchProvider"/> asks the backends in order and uses the
/// first whose <see cref="IsAvailable"/> is true, so an installed and running Everything wins over the
/// built-in Windows Search index.
/// </summary>
public interface IFileSearchBackend
{
    /// <summary>Diagnostics only (Debug output).</summary>
    string Name { get; }

    /// <summary>Cheap check, run for every query: is this backend usable right now?</summary>
    bool IsAvailable();

    /// <summary>
    /// Full paths (files and folders) matching <paramref name="query"/>, best first, at most
    /// <paramref name="maxResults"/>. Returns an empty list on any failure or timeout; never throws except
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<IReadOnlyList<string>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}
