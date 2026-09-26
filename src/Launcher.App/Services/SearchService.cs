using YourLauncher.Core.Model;
using YourLauncher.Core.Search;

namespace YourLauncher.App.Services;

/// <summary>
/// Thin holder around Core's <see cref="FlatIndex"/>/<see cref="SearchEngine"/>: keeps the current
/// whole-tree index and rebuilds it whenever the config is (re)loaded. Phase 3/5 call
/// <see cref="Rebuild"/> after any tree edit or file-watcher reload; Phase 6 passes a usage-based
/// tie-breaker into the constructor.
/// </summary>
public sealed class SearchService
{
    private readonly SearchEngine _engine;
    private FlatIndex _index;

    public SearchService(LauncherConfig config, Func<string, double>? usageScore = null)
    {
        _engine = new SearchEngine(usageScore);
        _index = FlatIndex.Build(config.Root);
    }

    public void Rebuild(LauncherConfig config) => _index = FlatIndex.Build(config.Root);

    public IReadOnlyList<SearchResult> Search(string query, int limit = 50) => _engine.Search(_index, query, limit);
}
