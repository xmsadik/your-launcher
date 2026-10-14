using System.Diagnostics;
using YourLauncher.App.Localization;
using YourLauncher.App.Providers.FileSearch;
using YourLauncher.App.Services;
using YourLauncher.Core.Providers;

namespace YourLauncher.App.Providers;

/// <summary>
/// File search ("f report"): a prefix-triggered async provider (tier <see cref="ResultTier.Background"/>).
/// It claims a query only when it starts with <c>settings.fileSearchPrefix</c> + a space, so ordinary
/// searches never pay for it; while it claims, the panel shows only its rows. The backend is chosen per
/// query: the first available of the given backends (Everything when it is running, else Windows Search).
/// <para>
/// Enter opens the file the way a path item does (<see cref="LaunchService.OpenPath"/>, literal path), Ctrl+Enter reveals it in Explorer (<see cref="ProviderResult.LocationPath"/>).
/// </para>
/// </summary>
public sealed class FileSearchProvider : IAsyncResultProvider
{
    public const int MaxResults = 20;

    private readonly IReadOnlyList<IFileSearchBackend> _backends;
    private readonly Func<string> _prefix;
    private readonly LaunchService _launchService;

    /// <param name="backends">In order of preference; the first whose <c>IsAvailable</c> is true serves the query.</param>
    /// <param name="prefix">Reads the current <c>settings.fileSearchPrefix</c> (it can change while running).</param>
    public FileSearchProvider(IReadOnlyList<IFileSearchBackend> backends, Func<string> prefix, LaunchService launchService)
    {
        _backends = backends;
        _prefix = prefix;
        _launchService = launchService;
    }

    /// <summary>Waits for a pause in typing: every keystroke would otherwise start an index query.</summary>
    public int DebounceMs => 150;

    public bool ClaimOnly => true;

    public string? PendingText => Strings.FileSearchPending;

    public bool TryClaim(string query, out string innerQuery) =>
        ProviderClaim.TryStripPrefix(query, _prefix(), out innerQuery);

    public async Task<IReadOnlyList<ProviderResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        var backend = _backends.FirstOrDefault(b => b.IsAvailable());
        if (backend is null)
        {
            return [];
        }

        var stopwatch = Stopwatch.StartNew();
        var paths = await backend.SearchAsync(query, MaxResults, cancellationToken).ConfigureAwait(false);
        Debug.WriteLine($"[YourLauncher] File search '{query}' via {backend.Name} -> {paths.Count} result(s) in {stopwatch.ElapsedMilliseconds} ms");

        var results = new List<ProviderResult>(paths.Count);
        for (var i = 0; i < paths.Count && results.Count < MaxResults; i++)
        {
            results.Add(ToResult(paths[i], query, score: paths.Count - i));
        }

        return results;
    }

    /// <summary>The backend's own order is the ranking: descending scores keep it (the merger sorts by score first).</summary>
    private ProviderResult ToResult(string path, string query, int score)
    {
        var name = FileSearchText.NameOf(path);
        var key = "file:" + path.ToLowerInvariant();
        return new ProviderResult
        {
            Id = key,
            UsageKey = key,
            Title = name,
            Subtitle = FileSearchText.ParentSubtitle(path),
            Glyph = "", // Document
            IconPath = path,
            LocationPath = path,
            Tier = ResultTier.Background,
            Score = score,
            HighlightPositions = FileSearchText.HighlightPositions(name, query),
            Execute = () => _launchService.OpenPath(path, name),
        };
    }
}
