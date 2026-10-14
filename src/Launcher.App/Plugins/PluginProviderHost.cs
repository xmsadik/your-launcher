using System.IO;
using YourLauncher.App.Localization;
using YourLauncher.Core.Plugins;
using YourLauncher.Core.Providers;
using YourLauncher.Core.Search;

namespace YourLauncher.App.Plugins;

/// <summary>
/// The list of async providers MainViewModel iterates: the fixed built-ins plus one <see cref="PluginProvider"/>
/// per plugin on disk. MainViewModel takes its provider list once at construction, so a rescan (startup,
/// config reload) swaps the plugin part of THIS list instead of asking the view model to change. Reads see an
/// immutable snapshot; <see cref="Rescan"/> replaces it atomically, so a keystroke racing a reload never sees a
/// half-built list.
/// </summary>
internal sealed class PluginProviderHost : IReadOnlyList<IAsyncResultProvider>
{
    public const string FolderName = "plugins";

    private readonly IAsyncResultProvider[] _fixed;
    private volatile IAsyncResultProvider[] _all;

    public PluginProviderHost(string configDirectory, IEnumerable<IAsyncResultProvider> fixedProviders)
    {
        PluginsDirectory = Path.Combine(configDirectory, FolderName);
        _fixed = fixedProviders.ToArray();
        _all = _fixed;
    }

    public string PluginsDirectory { get; }

    public int Count => _all.Length;

    public IAsyncResultProvider this[int index] => _all[index];

    /// <summary>
    /// Re-reads the plugins folder (a plugin whose keyword equals <paramref name="reservedKeyword"/>, the file-search
    /// prefix, is skipped and reported) and returns one line per plugin that was skipped (invalid manifest,
    /// duplicate keyword). Never throws - discovery turns IO failures into problem lines.
    /// </summary>
    public IReadOnlyList<string> Rescan(string? reservedKeyword = null)
    {
        var scan = PluginDiscovery.Scan(PluginsDirectory);
        var problems = scan.Problems.ToList();
        var reserved = string.IsNullOrWhiteSpace(reservedKeyword) ? null : TextNormalizer.Normalize(reservedKeyword.Trim());
        var plugins = new List<LoadedPlugin>();
        foreach (var plugin in scan.Plugins)
        {
            if (reserved is not null && TextNormalizer.Normalize(plugin.Manifest.Keyword) == reserved)
            {
                problems.Add(string.Format(Strings.PluginKeywordCollision, Path.GetFileName(plugin.Directory), plugin.Manifest.Keyword));
                continue;
            }

            plugins.Add(plugin);
        }

        _all = [.. _fixed, .. plugins.Select(p => new PluginProvider(p))];
        return problems;
    }

    /// <summary>Creates the folder if needed (so the Settings button always has something to open) and returns it.</summary>
    public string EnsureDirectory()
    {
        Directory.CreateDirectory(PluginsDirectory);
        return PluginsDirectory;
    }

    public IEnumerator<IAsyncResultProvider> GetEnumerator() => ((IEnumerable<IAsyncResultProvider>)_all).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
