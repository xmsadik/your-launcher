using System.IO;
using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.App.Services;

/// <summary>Result of <see cref="ConfigService.Save"/> (Phase 3): never throws, mirrors <see cref="Config.ConfigLoadResult"/>'s shape.</summary>
public sealed record ConfigSaveResult(bool Success, string? Error)
{
    public static ConfigSaveResult Ok() => new(true, null);
    public static ConfigSaveResult Failed(string error) => new(false, error);
}

/// <summary>
/// App-level wrapper around Core's <see cref="ConfigStore"/>: resolves the config directory
/// (honoring YOURLAUNCHER_CONFIG_DIR for tests/smoke-checks) and falls back to an empty in-memory
/// config with a user-facing message when config.json is corrupt, without touching the file on disk.
///
/// Phase 3 (editing): <see cref="Save"/> is the single write path every tree mutation goes through.
/// When the on-disk config failed to load at startup (<see cref="IsReadOnly"/>), saving is refused
/// outright rather than silently overwriting the corrupt file with whatever (empty) in-memory tree we
/// started with - the caller keeps the in-memory edit but the file stays untouched until the user fixes
/// or restores it. A real IO failure on write (locked file, disk full, ...) is reported but never
/// throws; the in-memory change is kept either way (spec: launch/save failures never lose user input).
/// </summary>
public sealed class ConfigService
{
    private readonly ConfigStore _store;

    public LauncherConfig Config { get; }

    /// <summary>Set when the on-disk config could not be parsed; shown once in the panel's error line.</summary>
    public string? LoadError { get; }

    /// <summary>True once <see cref="LoadError"/> is set - editing must not overwrite the untouched corrupt file (spec, Phase 3).</summary>
    public bool IsReadOnly => LoadError is not null;

    /// <summary>
    /// UTC timestamp of our own last successful write, so Phase 5's FileSystemWatcher can tell "the file
    /// changed because we just saved it" apart from an external edit and skip reloading in that case.
    /// // Phase 5: wire this into the watcher's debounce/ignore logic.
    /// </summary>
    public DateTime? LastSelfWriteUtc { get; private set; }

    public ConfigService()
        : this(ResolveConfigDirectory())
    {
    }

    public ConfigService(string directory)
    {
        _store = new ConfigStore(directory);
        var result = _store.Load();

        if (result.Success)
        {
            Config = result.Config!;
        }
        else
        {
            LoadError = $"Config could not be read: {result.Error}. Using an empty tree; the file was not modified.";
            Config = new LauncherConfig
            {
                Version = 1,
                Settings = new Settings(),
                Root = new FolderNode { Id = "root", Name = "Root" },
            };
        }
    }

    public static string ResolveConfigDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("YOURLAUNCHER_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return overrideDir;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Your Launcher");
    }

    /// <summary>
    /// Saves the in-memory <see cref="Config"/> atomically. Refuses when <see cref="IsReadOnly"/>; never
    /// throws on an IO failure while writing (reports it instead) - either way the in-memory tree already
    /// holds the caller's edit, so nothing is lost.
    /// </summary>
    public ConfigSaveResult Save()
    {
        if (IsReadOnly)
        {
            return ConfigSaveResult.Failed(
                "Config is read-only because config.json could not be read. Fix or restore it first.");
        }

        try
        {
            _store.Save(Config);
            LastSelfWriteUtc = DateTime.UtcNow;
            return ConfigSaveResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigSaveResult.Failed($"Could not save config: {ex.Message}");
        }
    }
}
