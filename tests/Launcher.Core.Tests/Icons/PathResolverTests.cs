using YourLauncher.Core.Icons;

namespace YourLauncher.Core.Tests.Icons;

/// <summary>
/// PATH/PATHEXT are process-wide environment variables; every test here saves and restores both so this
/// class can safely run alongside the rest of the suite (xUnit doesn't parallelize tests within one class
/// by default, so these never race each other).
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class PathResolverTests : IDisposable
{
    private readonly string? _originalPath;
    private readonly string? _originalPathExt;
    private readonly string _tempRoot;

    public PathResolverTests()
    {
        _originalPath = Environment.GetEnvironmentVariable("PATH");
        _originalPathExt = Environment.GetEnvironmentVariable("PATHEXT");
        _tempRoot = Path.Combine(Path.GetTempPath(), "YourLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        Environment.SetEnvironmentVariable("PATHEXT", _originalPathExt);
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void FindOnPath_ExactNameWithExtension_Found()
    {
        var exePath = Path.Combine(_tempRoot, "tool.exe");
        File.WriteAllText(exePath, "");
        Environment.SetEnvironmentVariable("PATH", _tempRoot);

        var found = PathResolver.FindOnPath("tool.exe");

        Assert.Equal(exePath, found);
    }

    [Fact]
    public void FindOnPath_NoExtension_ResolvesViaPathExt()
    {
        var exePath = Path.Combine(_tempRoot, "tool.exe");
        File.WriteAllText(exePath, "");
        Environment.SetEnvironmentVariable("PATH", _tempRoot);
        Environment.SetEnvironmentVariable("PATHEXT", ".COM;.EXE;.BAT");

        var found = PathResolver.FindOnPath("tool");

        // Windows paths are case-insensitive; PATHEXT itself is conventionally uppercase (".EXE"), so the
        // resolved path's extension casing may differ from the file's on-disk casing - compare ignoring case.
        Assert.Equal(exePath, found, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindOnPath_MultipleDirectories_SearchesInOrder()
    {
        var dir1 = Path.Combine(_tempRoot, "dir1");
        var dir2 = Path.Combine(_tempRoot, "dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        var expected = Path.Combine(dir2, "found.exe");
        File.WriteAllText(expected, "");
        Environment.SetEnvironmentVariable("PATH", $"{dir1}{Path.PathSeparator}{dir2}");

        var found = PathResolver.FindOnPath("found.exe");

        Assert.Equal(expected, found);
    }

    [Fact]
    public void FindOnPath_NotFound_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("PATH", _tempRoot);
        Assert.Null(PathResolver.FindOnPath("definitely-not-here.exe"));
    }

    [Fact]
    public void FindOnPath_EmptyName_ReturnsNull()
    {
        Assert.Null(PathResolver.FindOnPath(""));
        Assert.Null(PathResolver.FindOnPath("   "));
    }

    [Fact]
    public void FindOnPath_RealNotepad_ResolvesOnDefaultPath()
    {
        // Sanity check against the real environment (System32 is on %PATH% on every Windows install this
        // runs on): confirms the PATHEXT-less lookup used by config.example.json's "notepad.exe" bare-name
        // node actually works, not just the synthetic-directory cases above.
        var found = PathResolver.FindOnPath("notepad.exe");
        Assert.NotNull(found);
        Assert.True(File.Exists(found));
    }
}
