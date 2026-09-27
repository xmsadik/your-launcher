using YourLauncher.Core.Bookmarks;

namespace YourLauncher.Core.Tests.Bookmarks;

/// <summary>
/// <see cref="ChromiumBookmarkLocator.Discover"/> against a fake <c>%LOCALAPPDATA%</c>/<c>%APPDATA%</c>
/// tree built under a temp directory (spec §1.1: "roots injected so tests use a temp dir") - never touches
/// the real profile.
/// </summary>
public class ChromiumBookmarkLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"YourLauncherBookmarkLocatorTest_{Guid.NewGuid():N}");

    private string LocalAppData => Path.Combine(_root, "Local");

    private string RoamingAppData => Path.Combine(_root, "Roaming");

    public ChromiumBookmarkLocatorTests()
    {
        Directory.CreateDirectory(LocalAppData);
        Directory.CreateDirectory(RoamingAppData);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void CreateChromeProfile(string profileDir)
    {
        var dir = Path.Combine(LocalAppData, "Google", "Chrome", "User Data", profileDir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Bookmarks"), "{}");
    }

    private void WriteChromeLocalState(string json) =>
        File.WriteAllText(Path.Combine(LocalAppData, "Google", "Chrome", "User Data", "Local State"), json);

    [Fact]
    public void Discover_NoBrowsersInstalled_ReturnsEmpty()
    {
        var sources = ChromiumBookmarkLocator.Discover(LocalAppData, RoamingAppData);
        Assert.Empty(sources);
    }

    [Fact]
    public void Discover_SingleProfile_DisplayNameIsJustBrowserName()
    {
        CreateChromeProfile("Default");

        var sources = ChromiumBookmarkLocator.Discover(LocalAppData, RoamingAppData);

        var source = Assert.Single(sources);
        Assert.Equal("Chrome", source.Browser);
        Assert.Equal("Chrome", source.DisplayName);
        Assert.Equal("chrome/default", source.SourceKey);
        Assert.Equal("Default", source.ProfileDir);
    }

    [Fact]
    public void Discover_ProfileDirWithoutBookmarksFile_Skipped()
    {
        CreateChromeProfile("Default");
        Directory.CreateDirectory(Path.Combine(LocalAppData, "Google", "Chrome", "User Data", "Profile 1")); // no Bookmarks file inside

        var sources = ChromiumBookmarkLocator.Discover(LocalAppData, RoamingAppData);

        Assert.Single(sources);
    }

    [Fact]
    public void Discover_UnrelatedDirectory_Ignored()
    {
        CreateChromeProfile("Default");
        Directory.CreateDirectory(Path.Combine(LocalAppData, "Google", "Chrome", "User Data", "System Profile")); // neither "Default" nor "Profile *"

        var sources = ChromiumBookmarkLocator.Discover(LocalAppData, RoamingAppData);

        Assert.Single(sources);
    }

    [Fact]
    public void Discover_MultipleProfiles_DisplayNameIncludesProfileNameFromLocalState()
    {
        CreateChromeProfile("Default");
        CreateChromeProfile("Profile 1");
        WriteChromeLocalState("""{"profile":{"info_cache":{"Default":{"name":"Person 1"},"Profile 1":{"name":"Work"}}}}""");

        var sources = ChromiumBookmarkLocator.Discover(LocalAppData, RoamingAppData);

        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, s => s.DisplayName == "Chrome (Person 1)" && s.ProfileDir == "Default");
        Assert.Contains(sources, s => s.DisplayName == "Chrome (Work)" && s.ProfileDir == "Profile 1");
    }

    [Fact]
    public void Discover_MalformedLocalState_FallsBackToDirName_DoesNotThrow()
    {
        CreateChromeProfile("Default");
        CreateChromeProfile("Profile 1");
        WriteChromeLocalState("{ not valid json ");

        var sources = ChromiumBookmarkLocator.Discover(LocalAppData, RoamingAppData);

        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, s => s.DisplayName == "Chrome (Default)");
        Assert.Contains(sources, s => s.DisplayName == "Chrome (Profile 1)");
    }

    [Fact]
    public void Discover_MissingLocalState_FallsBackToDirName()
    {
        CreateChromeProfile("Default");
        CreateChromeProfile("Profile 1");

        var sources = ChromiumBookmarkLocator.Discover(LocalAppData, RoamingAppData);

        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, s => s.DisplayName == "Chrome (Default)");
        Assert.Contains(sources, s => s.DisplayName == "Chrome (Profile 1)");
    }

    [Fact]
    public void Discover_Opera_SingleProfileDirectlyUnderDir()
    {
        var operaDir = Path.Combine(RoamingAppData, "Opera Software", "Opera Stable");
        Directory.CreateDirectory(operaDir);
        File.WriteAllText(Path.Combine(operaDir, "Bookmarks"), "{}");

        var sources = ChromiumBookmarkLocator.Discover(LocalAppData, RoamingAppData);

        var source = Assert.Single(sources);
        Assert.Equal("Opera", source.Browser);
        Assert.Equal("Opera", source.DisplayName);
        Assert.Equal("opera/", source.SourceKey);
        Assert.Equal("", source.ProfileDir);
    }

    [Fact]
    public void Discover_OperaAndOperaGx_BothDiscoveredIndependently()
    {
        foreach (var dir in new[] { "Opera Stable", "Opera GX Stable" })
        {
            var full = Path.Combine(RoamingAppData, "Opera Software", dir);
            Directory.CreateDirectory(full);
            File.WriteAllText(Path.Combine(full, "Bookmarks"), "{}");
        }

        var sources = ChromiumBookmarkLocator.Discover(LocalAppData, RoamingAppData);

        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, s => s.Browser == "Opera");
        Assert.Contains(sources, s => s.Browser == "Opera GX");
    }

    [Fact]
    public void Discover_MultipleDifferentBrowsers_AllDiscovered()
    {
        CreateChromeProfile("Default");
        var edgeDefault = Path.Combine(LocalAppData, "Microsoft", "Edge", "User Data", "Default");
        Directory.CreateDirectory(edgeDefault);
        File.WriteAllText(Path.Combine(edgeDefault, "Bookmarks"), "{}");

        var sources = ChromiumBookmarkLocator.Discover(LocalAppData, RoamingAppData);

        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, s => s.Browser == "Chrome" && s.SourceKey == "chrome/default");
        Assert.Contains(sources, s => s.Browser == "Edge" && s.SourceKey == "edge/default");
    }
}
