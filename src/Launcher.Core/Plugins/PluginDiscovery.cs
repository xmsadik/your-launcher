using YourLauncher.Core.Search;

namespace YourLauncher.Core.Plugins;

/// <summary>Plugins that loaded plus one readable line per folder that did not.</summary>
public sealed record PluginScanResult(IReadOnlyList<LoadedPlugin> Plugins, IReadOnlyList<string> Problems);

/// <summary>
/// Scans <c>&lt;config dir&gt;\plugins\*\plugin.json</c>. Folders are visited in name order so "first folder
/// wins" for a duplicate keyword is deterministic. Never throws: an unreadable folder becomes a problem line.
/// Folders without a plugin.json are ignored silently (they may hold shared files).
/// </summary>
public static class PluginDiscovery
{
    public const string ManifestFileName = "plugin.json";

    public static PluginScanResult Scan(string pluginsDirectory)
    {
        var plugins = new List<LoadedPlugin>();
        var problems = new List<string>();
        if (!Directory.Exists(pluginsDirectory))
        {
            return new PluginScanResult(plugins, problems);
        }

        string[] folders;
        try
        {
            folders = Directory.GetDirectories(pluginsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"plugins: {ex.Message}");
            return new PluginScanResult(plugins, problems);
        }

        Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
        var byKeyword = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var folderName = Path.GetFileName(folder);
            var manifestPath = Path.Combine(folder, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            string json;
            try
            {
                json = File.ReadAllText(manifestPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{folderName}: {ex.Message}");
                continue;
            }

            var parsed = PluginManifestParser.Parse(json, folderName);
            if (parsed.Manifest is not { } manifest)
            {
                problems.Add($"{folderName}: {parsed.Error}");
                continue;
            }

            var key = TextNormalizer.Normalize(manifest.Keyword);
            if (byKeyword.TryGetValue(key, out var owner))
            {
                problems.Add($"{folderName}: keyword \"{manifest.Keyword}\" is already used by \"{owner}\".");
                continue;
            }

            byKeyword[key] = folderName;
            plugins.Add(new LoadedPlugin(manifest, folder));
        }

        return new PluginScanResult(plugins, problems);
    }
}
