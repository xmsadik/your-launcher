using YourLauncher.Core.Model;

namespace YourLauncher.Core.Config;

/// <summary>
/// Loads and saves config.json from a given directory (injectable so tests can point at a temp dir;
/// the app points this at %APPDATA%\Your Launcher, or YOURLAUNCHER_CONFIG_DIR when set).
/// Save is atomic: write to config.json.tmp, then File.Replace into config.json, moving the previous
/// version to config.backup.json in the same call (spec §4.5 / decision notes).
/// </summary>
public sealed class ConfigStore
{
    private const string ConfigFileName = "config.json";
    private const string BackupFileName = "config.backup.json";
    private const string TempFileName = "config.json.tmp";

    private readonly string _directory;

    public ConfigStore(string directory)
    {
        _directory = directory;
    }

    public string ConfigPath => Path.Combine(_directory, ConfigFileName);
    public string BackupPath => Path.Combine(_directory, BackupFileName);
    private string TempPath => Path.Combine(_directory, TempFileName);

    public static LauncherConfig CreateDefault() => new()
    {
        Version = 1,
        Settings = new Settings(),
        Root = new FolderNode { Id = "root", Name = "Root", Children = new List<Node>() },
    };

    /// <summary>
    /// Loads config.json, creating a default file if none exists. On corrupt/unreadable JSON, returns a
    /// failure result and leaves the file on disk untouched — callers should fall back to an empty
    /// in-memory config rather than crashing or overwriting the bad file.
    /// </summary>
    public ConfigLoadResult Load()
    {
        Directory.CreateDirectory(_directory);

        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = CreateDefault();
            Save(defaultConfig);
            return ConfigLoadResult.Ok(defaultConfig);
        }

        string json;
        try
        {
            json = File.ReadAllText(ConfigPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigLoadResult.Failed(ex.Message);
        }

        return ConfigSerializer.Deserialize(json);
    }

    /// <summary>Writes config atomically: temp file, then File.Replace (or File.Move if no prior file exists).</summary>
    public void Save(LauncherConfig config)
    {
        Directory.CreateDirectory(_directory);
        var json = ConfigSerializer.Serialize(config);
        File.WriteAllText(TempPath, json);

        if (File.Exists(ConfigPath))
        {
            File.Replace(TempPath, ConfigPath, BackupPath);
        }
        else
        {
            File.Move(TempPath, ConfigPath);
        }
    }
}
