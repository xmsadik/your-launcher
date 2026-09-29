using YourLauncher.Core.Diagnostics;

namespace YourLauncher.Core.Tests;

public sealed class VersionMarkerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "YourLauncherTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string MarkerPath => Path.Combine(_dir, VersionMarker.FileName);

    private void WriteMarker(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(MarkerPath, text);
    }

    [Fact]
    public void FreshInstall_NoConfigNoMarker_FalseAndRecords()
    {
        Assert.False(VersionMarker.CheckAndRecord(_dir, new Version(0, 1, 1, 0)));
        Assert.Equal("0.1.1.0", File.ReadAllText(MarkerPath));
    }

    [Fact]
    public void ExistingConfigWithoutMarker_TreatedAsUpdate()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), "{}");

        Assert.True(VersionMarker.CheckAndRecord(_dir, new Version(0, 1, 1, 0)));
        Assert.Equal("0.1.1.0", File.ReadAllText(MarkerPath));
    }

    [Fact]
    public void OlderMarker_TrueAndRecordsNewVersion()
    {
        WriteMarker("0.1.0.0");
        Assert.True(VersionMarker.CheckAndRecord(_dir, new Version(0, 1, 1, 0)));
        Assert.Equal("0.1.1.0", File.ReadAllText(MarkerPath));
    }

    [Fact]
    public void SameVersion_FalseOnEveryLaterRun()
    {
        WriteMarker("0.1.1.0");
        Assert.False(VersionMarker.CheckAndRecord(_dir, new Version(0, 1, 1, 0)));
        Assert.False(VersionMarker.CheckAndRecord(_dir, new Version(0, 1, 1, 0)));
    }

    [Fact]
    public void UpdateNoticeShownOnlyOnce()
    {
        WriteMarker("0.1.0.0");
        Assert.True(VersionMarker.CheckAndRecord(_dir, new Version(0, 1, 1, 0)));
        Assert.False(VersionMarker.CheckAndRecord(_dir, new Version(0, 1, 1, 0)));
    }

    [Fact]
    public void Downgrade_FalseAndRecordsRunningVersion()
    {
        WriteMarker("0.2.0.0");
        Assert.False(VersionMarker.CheckAndRecord(_dir, new Version(0, 1, 1, 0)));
        Assert.Equal("0.1.1.0", File.ReadAllText(MarkerPath));
    }

    [Fact]
    public void GarbageMarker_FalseAndRewritten()
    {
        WriteMarker("not a version");
        Assert.False(VersionMarker.CheckAndRecord(_dir, new Version(0, 1, 1, 0)));
        Assert.Equal("0.1.1.0", File.ReadAllText(MarkerPath));
    }
}
