using System.IO;
using System.Security.Cryptography;
using System.Text;
using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.App.Services;

/// <summary>Result of <see cref="ConfigService.Save"/> (Phase 3): never throws, mirrors <see cref="Config.ConfigLoadResult"/>'s shape.</summary>
public sealed record ConfigSaveResult(bool Success, string? Error)
{
    public static ConfigSaveResult Ok() => new(true, null);
    public static ConfigSaveResult Failed(string error) => new(false, error);
}

/// <summary>Result of <see cref="ConfigService.Reload"/> / <see cref="ConfigService.KeepCurrentVersion"/> / <see cref="ConfigService.RestoreFromBackup"/> (Phase 5).</summary>
public sealed record ConfigReloadResult(bool Success, string? Error)
{
    public static ConfigReloadResult Ok() => new(true, null);
    public static ConfigReloadResult Failed(string error) => new(false, error);
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
///
/// Phase 5 (spec §4, revised §10 items 2-4): <see cref="Config"/>'s identity never changes across a
/// reload - <see cref="Reload"/> assigns fresh Root/Settings/Version onto the *existing* instance so
/// callers that captured a reference to <see cref="Config"/> at construction time (e.g. MainViewModel)
/// keep seeing the live tree without re-wiring anything. <see cref="LastWrittenHash"/> replaces the old
/// timestamp-based self-write heuristic: it's a SHA-256 of the exact bytes this instance itself last
/// wrote, so <c>ConfigWatcherService</c> can tell "the file changed because we just saved it" apart from
/// a genuine external edit with no timing guesses at all.
/// </summary>
public sealed class ConfigService
{
    private readonly ConfigStore _store;

    public LauncherConfig Config { get; }

    /// <summary>Set when the on-disk config could not be parsed; shown once in the panel's error line. Cleared on a successful reload/recovery.</summary>
    public string? LoadError { get; private set; }

    /// <summary>True once <see cref="LoadError"/> is set - editing must not overwrite the untouched corrupt file (spec, Phase 3).</summary>
    public bool IsReadOnly => LoadError is not null;

    /// <summary>True once at least one load/reload/recovery has populated <see cref="Config"/> from real on-disk data. False only when the very first load at startup failed - in that case there's no "current version" worth keeping, only a backup worth trying (spec §10 item 4).</summary>
    public bool HasEverLoadedSuccessfully { get; private set; }

    public bool HasBackup => File.Exists(_store.BackupPath);

    public string Directory { get; }

    public string ConfigPath => _store.ConfigPath;

    /// <summary>SHA-256 (hex, uppercase) of the exact UTF-8 bytes this instance itself last wrote to config.json.</summary>
    public string? LastWrittenHash { get; private set; }

    /// <summary>Raised after any successful reload/recovery applies new data into the existing <see cref="Config"/> instance in place.</summary>
    public event Action? ConfigReloaded;

    public ConfigService()
        : this(ResolveConfigDirectory())
    {
    }

    public ConfigService(string directory)
    {
        Directory = directory;
        _store = new ConfigStore(directory);
        var result = _store.Load();

        if (result.Success)
        {
            Config = result.Config!;
            HasEverLoadedSuccessfully = true;
            LastWrittenHash = ComputeHash(Config);
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
            HasEverLoadedSuccessfully = true;
            LastWrittenHash = ComputeHash(Config);
            return ConfigSaveResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigSaveResult.Failed($"Could not save config: {ex.Message}");
        }
    }

    /// <summary>
    /// Reloads config.json from disk IN PLACE (spec §10 item 2): never replaces the <see cref="Config"/>
    /// instance - assigns Root/Settings/Version from the freshly parsed file onto it, then raises
    /// <see cref="ConfigReloaded"/>. On a parse failure the in-memory tree is left completely untouched and
    /// <see cref="IsReadOnly"/> becomes (or stays) true, rather than ever being wiped to empty.
    /// </summary>
    public ConfigReloadResult Reload()
    {
        var result = _store.Load();
        if (!result.Success)
        {
            LoadError = "config.json has errors — kept the previous version.";
            return ConfigReloadResult.Failed(result.Error ?? "unknown error");
        }

        Config.Version = result.Config!.Version;
        Config.Settings = result.Config.Settings;
        Config.Root = result.Config.Root;

        LoadError = null;
        HasEverLoadedSuccessfully = true;
        LastWrittenHash = ComputeHash(Config);
        ConfigReloaded?.Invoke();
        return ConfigReloadResult.Ok();
    }

    /// <summary>
    /// Recovery action for a reload that found a corrupt file while good in-memory data already existed
    /// (spec §10 item 4): archives the corrupt file, then re-saves the in-memory tree over it - "keep what
    /// I had, ignore the bad edit on disk".
    /// </summary>
    public ConfigReloadResult KeepCurrentVersion()
    {
        ArchiveCorruptFile();

        // The corrupt file is still sitting at ConfigPath at this point. ConfigStore.Save()'s atomic
        // File.Replace(temp, ConfigPath, BackupPath) would otherwise back up *that* (the garbage we just
        // archived) into config.backup.json, clobbering whatever good backup already existed and
        // defeating RestoreFromBackup the next time it's actually needed. Deleting it first makes
        // Save() take the File.Move branch instead (nothing to back up), leaving the real backup alone.
        try
        {
            File.Delete(_store.ConfigPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort only - if this fails, Save() falls back to File.Replace and the backup ends up
            // holding the corrupt content, same as before; the recovery itself still succeeds either way.
        }

        LoadError = null;

        var save = Save();
        if (!save.Success)
        {
            return ConfigReloadResult.Failed(save.Error ?? "Could not save config.");
        }

        ConfigReloaded?.Invoke();
        return ConfigReloadResult.Ok();
    }

    /// <summary>
    /// Recovery action for a corrupt file with no good in-memory data to fall back on - i.e. config.json
    /// was already corrupt at startup (spec §10 item 4): archives the corrupt file, copies
    /// config.backup.json over it, then reloads.
    /// </summary>
    public ConfigReloadResult RestoreFromBackup()
    {
        if (!HasBackup)
        {
            return ConfigReloadResult.Failed("No backup file found.");
        }

        ArchiveCorruptFile();

        try
        {
            File.Copy(_store.BackupPath, _store.ConfigPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigReloadResult.Failed($"Could not restore backup: {ex.Message}");
        }

        return Reload();
    }

    /// <summary>Best-effort: copies the current (corrupt) config.json aside as <c>config.corrupt-&lt;timestamp&gt;.json</c> before a recovery action overwrites it (spec §10 item 4). Failure to archive doesn't block the recovery itself.</summary>
    private void ArchiveCorruptFile()
    {
        if (!File.Exists(_store.ConfigPath))
        {
            return;
        }

        var archivePath = Path.Combine(Directory, $"config.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        try
        {
            File.Copy(_store.ConfigPath, archivePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort archive only - proceed with the recovery regardless.
        }
    }

    private static string ComputeHash(LauncherConfig config)
    {
        var json = ConfigSerializer.Serialize(config);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
