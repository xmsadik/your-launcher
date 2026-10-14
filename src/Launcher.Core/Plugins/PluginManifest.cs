namespace YourLauncher.Core.Plugins;

/// <summary>A validated plugin.json: everything needed to start the plugin's process and show its rows.</summary>
/// <param name="Name">Shown in the "searching…" placeholder; defaults to the folder name.</param>
/// <param name="Keyword">The plugin only runs when the query starts with "&lt;keyword&gt; ", so no process is spawned on ordinary keystrokes.</param>
/// <param name="Command">Executable to start (PATH lookup, or relative to the plugin folder when it contains a path separator).</param>
/// <param name="Args">Command-line arguments, passed as a list - never through a shell.</param>
/// <param name="WorkingDirectory">Null = the plugin folder.</param>
/// <param name="TimeoutMs">Hard limit for one run; the process tree is killed when it is exceeded.</param>
/// <param name="DebounceMs">Quiet time after the last keystroke before the plugin is started.</param>
/// <param name="Glyph">Optional Segoe Fluent glyph for the rows.</param>
/// <param name="Icon">Optional image/ico/exe path for the rows, relative to the plugin folder or absolute.</param>
public sealed record PluginManifest(
    string Name,
    string Keyword,
    string Command,
    IReadOnlyList<string> Args,
    string? WorkingDirectory,
    int TimeoutMs,
    int DebounceMs,
    string? Glyph,
    string? Icon);

/// <summary>A plugin found on disk: its manifest plus the folder it lives in.</summary>
public sealed record LoadedPlugin(PluginManifest Manifest, string Directory);

/// <summary>Result of parsing one plugin.json: either a manifest or a human-readable reason it was rejected.</summary>
public sealed record PluginManifestResult(PluginManifest? Manifest, string? Error)
{
    public bool Success => Manifest is not null;
}
