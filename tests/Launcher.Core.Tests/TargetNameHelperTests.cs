using YourLauncher.Core.Config;

namespace YourLauncher.Core.Tests;

public class TargetNameHelperTests : IDisposable
{
    private readonly string _tempDir;

    public TargetNameHelperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "YourLauncherTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void SuggestName_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal("", TargetNameHelper.SuggestName(""));
        Assert.Equal("", TargetNameHelper.SuggestName("   "));
    }

    [Fact]
    public void SuggestName_ExistingDirectory_ReturnsDirectoryName()
    {
        var dir = Path.Combine(_tempDir, "MyProject");
        Directory.CreateDirectory(dir);

        Assert.Equal("MyProject", TargetNameHelper.SuggestName(dir));
    }

    [Fact]
    public void SuggestName_ExistingDirectoryWithTrailingSeparator_ReturnsDirectoryName()
    {
        var dir = Path.Combine(_tempDir, "MyProject");
        Directory.CreateDirectory(dir);

        Assert.Equal("MyProject", TargetNameHelper.SuggestName(dir + "\\"));
    }

    [Fact]
    public void SuggestName_ExistingNonExeFile_ReturnsFileNameWithoutExtension()
    {
        var file = Path.Combine(_tempDir, "report.xlsx");
        File.WriteAllText(file, "x");

        Assert.Equal("report", TargetNameHelper.SuggestName(file));
    }

    [Fact]
    public void SuggestName_ExistingExeFile_PrefersFileDescriptionLookup()
    {
        var file = Path.Combine(_tempDir, "app.exe");
        File.WriteAllText(file, "x");

        var result = TargetNameHelper.SuggestName(file, _ => "My App");

        Assert.Equal("My App", result);
    }

    [Fact]
    public void SuggestName_ExistingExeFile_FallsBackToFileNameWhenDescriptionLookupReturnsNull()
    {
        var file = Path.Combine(_tempDir, "app.exe");
        File.WriteAllText(file, "x");

        var result = TargetNameHelper.SuggestName(file, _ => null);

        Assert.Equal("app", result);
    }

    [Fact]
    public void SuggestName_ExistingExeFile_FallsBackToFileNameWhenNoDescriptionLookupGiven()
    {
        var file = Path.Combine(_tempDir, "app.exe");
        File.WriteAllText(file, "x");

        Assert.Equal("app", TargetNameHelper.SuggestName(file));
    }

    [Fact]
    public void SuggestName_HttpsUrl_ReturnsHost()
    {
        Assert.Equal("github.com", TargetNameHelper.SuggestName("https://github.com/anthropics/claude"));
    }

    [Fact]
    public void SuggestName_HttpUrl_ReturnsHost()
    {
        Assert.Equal("example.com", TargetNameHelper.SuggestName("http://example.com"));
    }

    [Fact]
    public void SuggestName_BareHostWithoutScheme_ReturnsHost()
    {
        Assert.Equal("github.com", TargetNameHelper.SuggestName("github.com"));
    }

    [Fact]
    public void SuggestName_NonExistentAbsoluteWindowsPath_ReturnsFileNameWithoutExtension()
    {
        Assert.Equal("notes", TargetNameHelper.SuggestName(@"D:\Projects\notes.txt"));
    }

    [Fact]
    public void SuggestName_NonExistentExePath_UsesFileDescriptionLookupEvenThoughFileDoesNotExist()
    {
        // The Editor calls SuggestName as soon as the user types/pastes a target, before it necessarily
        // exists on disk (e.g. still typing a path); the exe branch should still try the lookup.
        var result = TargetNameHelper.SuggestName(@"C:\NoSuchDir\tool.exe", _ => "Tool App");
        Assert.Equal("Tool App", result);
    }

    [Fact]
    public void SuggestName_UncPath_ReturnsFileNameWithoutExtension()
    {
        Assert.Equal("readme", TargetNameHelper.SuggestName(@"\\server\share\readme.md"));
    }

    [Fact]
    public void SuggestName_PlainTextWithNoPathOrUrlShape_ReturnsTextItself()
    {
        Assert.Equal("just some text", TargetNameHelper.SuggestName("just some text"));
    }
}
