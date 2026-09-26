using YourLauncher.Core.Config;

namespace YourLauncher.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "YourLauncherTests_" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenNoConfigExists_CreatesDefaultWithEmptyRoot()
    {
        var store = new ConfigStore(_tempDir);

        var result = store.Load();

        Assert.True(result.Success, result.Error);
        Assert.Equal("root", result.Config!.Root.Id);
        Assert.Empty(result.Config.Root.Children);
        Assert.True(File.Exists(store.ConfigPath));
    }

    [Fact]
    public void Save_WhenConfigAlreadyExists_WritesBackupOfPreviousVersion()
    {
        var store = new ConfigStore(_tempDir);
        var first = ConfigStore.CreateDefault();
        first.Settings.Hotkey = "Ctrl+Space";
        store.Save(first);

        var second = ConfigStore.CreateDefault();
        second.Settings.Hotkey = "Alt+Space";
        store.Save(second);

        Assert.True(File.Exists(store.BackupPath));
        var backupResult = ConfigSerializer.Deserialize(File.ReadAllText(store.BackupPath));
        Assert.Equal("Ctrl+Space", backupResult.Config!.Settings.Hotkey);

        var currentResult = ConfigSerializer.Deserialize(File.ReadAllText(store.ConfigPath));
        Assert.Equal("Alt+Space", currentResult.Config!.Settings.Hotkey);
    }

    [Fact]
    public void Load_WhenFileIsCorrupt_ReturnsFailureAndLeavesFileUntouched()
    {
        Directory.CreateDirectory(_tempDir);
        var store = new ConfigStore(_tempDir);
        const string corrupt = "{ this is not valid json";
        File.WriteAllText(store.ConfigPath, corrupt);

        var result = store.Load();

        Assert.False(result.Success);
        Assert.Null(result.Config);
        Assert.Equal(corrupt, File.ReadAllText(store.ConfigPath));
        Assert.False(File.Exists(store.BackupPath));
    }
}
