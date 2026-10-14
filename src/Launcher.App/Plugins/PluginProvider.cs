using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using YourLauncher.App.Localization;
using YourLauncher.App.Services;
using YourLauncher.Core.Plugins;
using YourLauncher.Core.Providers;
using YourLauncher.Core.Search;

namespace YourLauncher.App.Plugins;

/// <summary>
/// One out-of-process plugin as a prefix-triggered <see cref="IAsyncResultProvider"/>. One provider per plugin
/// (rather than one multiplexer) because the debounce is a per-plugin setting and the panel already cancels and
/// debounces per provider. It claims the query only after "&lt;keyword&gt; ", so a plugin costs nothing - no
/// process, no row - until the user opts in by typing its keyword.
/// </summary>
internal sealed class PluginProvider : IAsyncResultProvider
{
    /// <summary>Puzzle piece (Segoe Fluent Icons) - "extension".</summary>
    private const string DefaultGlyph = "";

    private readonly LoadedPlugin _plugin;
    private readonly string _idKeyword;
    private readonly string? _pluginIcon;

    public PluginProvider(LoadedPlugin plugin)
    {
        _plugin = plugin;
        _idKeyword = TextNormalizer.Normalize(plugin.Manifest.Keyword);
        _pluginIcon = plugin.Manifest.Icon is { } icon ? PluginRunner.ResolveAgainst(plugin.Directory, icon) : null;
    }

    public int DebounceMs => _plugin.Manifest.DebounceMs;

    public string? PendingText => string.Format(Strings.PluginSearching, _plugin.Manifest.Name);

    public bool TryClaim(string query, out string innerQuery) =>
        PluginQueryMatcher.TryMatch(_plugin.Manifest.Keyword, query, out innerQuery);

    public async Task<IReadOnlyList<ProviderResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        var items = await PluginRunner.RunAsync(_plugin, query, cancellationToken).ConfigureAwait(false);
        var results = new List<ProviderResult>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            results.Add(ToResult(items[i], i, items.Count));
        }

        return results;
    }

    private ProviderResult ToResult(PluginItem item, int index, int count)
    {
        var icon = item.Icon is { } own ? PluginRunner.ResolveAgainst(_plugin.Directory, own) : _pluginIcon;
        var action = item.Action;
        return new ProviderResult
        {
            Id = $"plugin:{_idKeyword}:{index}",
            UsageKey = $"plugin:{_idKeyword}:{item.Title}",
            Title = item.Title,
            Subtitle = item.Subtitle,
            Glyph = _plugin.Manifest.Glyph ?? DefaultGlyph,
            IconPath = icon is not null && File.Exists(icon) ? icon : null,
            Tier = ResultTier.Background,
            Score = count - index, // the plugin's own order is the ranking.
            CloseAfterRun = true,
            Execute = () => Run(action, _plugin.Directory),
        };
    }

    /// <summary>
    /// Runs on the UI thread (the clipboard needs STA). Failures are a plain "false" - the row stays and the
    /// panel stays open - since a provider result has no error channel and a bad plugin must not spam errors.
    /// </summary>
    private static bool Run(PluginAction action, string pluginDirectory)
    {
        try
        {
            switch (action.Kind)
            {
                case PluginActionKind.Open:
                    Process.Start(new ProcessStartInfo(action.Target) { UseShellExecute = true })?.Dispose();
                    return true;
                case PluginActionKind.Copy:
                    return ClipboardHelper.TrySetText(action.Text);
                case PluginActionKind.Run:
                    // Same rules as the manifest command: a path separator means "inside the plugin folder".
                    var physicalDir = PackageContext.ToPhysicalPath(pluginDirectory, PluginDiscovery.ManifestFileName);
                    var command = action.Command.IndexOfAny(['\\', '/']) >= 0
                        ? PluginRunner.ResolveAgainst(physicalDir, action.Command)
                        : action.Command;
                    var startInfo = new ProcessStartInfo(command)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = physicalDir,
                    };
                    foreach (var arg in action.Args)
                    {
                        startInfo.ArgumentList.Add(arg);
                    }

                    Process.Start(startInfo)?.Dispose();
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Debug.WriteLine($"[YourLauncher] Plugin action failed: {ex.Message}");
            return false;
        }
    }
}
