using YourLauncher.Core.Icons;

namespace YourLauncher.Core.Tests.Icons;

public class TargetCheckTests : IDisposable
{
    private readonly string? _originalPath;
    private readonly string _tempRoot;

    public TargetCheckTests()
    {
        _originalPath = Environment.GetEnvironmentVariable("PATH");
        _tempRoot = Path.Combine(Path.GetTempPath(), "YourLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    // ---- ShouldCheckExistence matrix (spec §7.2) ----

    [Theory]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData(@"D:\Projects\report.xlsx")]
    [InlineData(@"C:\")]
    public void ShouldCheckExistence_LocalFullyQualifiedPath_True(string path) =>
        Assert.True(TargetCheck.ShouldCheckExistence(path));

    [Theory]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"\\server\share")]
    public void ShouldCheckExistence_Unc_False(string path) =>
        Assert.False(TargetCheck.ShouldCheckExistence(path));

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/file")]
    [InlineData("shell:AppsFolder")]
    [InlineData("mailto:a@b.com")]
    public void ShouldCheckExistence_UriOrShellScheme_False(string value) =>
        Assert.False(TargetCheck.ShouldCheckExistence(value));

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData("git")]
    [InlineData("pwsh")]
    public void ShouldCheckExistence_BareName_False(string value) =>
        Assert.False(TargetCheck.ShouldCheckExistence(value));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldCheckExistence_EmptyOrWhitespace_False(string value) =>
        Assert.False(TargetCheck.ShouldCheckExistence(value));

    [Fact]
    public void ShouldCheckExistence_RelativePath_False() =>
        Assert.False(TargetCheck.ShouldCheckExistence(@"..\relative\path.txt"));

    // ---- IsMissing ----

    [Fact]
    public void IsMissing_ExistingFile_False()
    {
        var file = Path.Combine(_tempRoot, "exists.txt");
        File.WriteAllText(file, "x");
        Assert.False(TargetCheck.IsMissing(file));
    }

    [Fact]
    public void IsMissing_ExistingDirectory_False() =>
        Assert.False(TargetCheck.IsMissing(_tempRoot));

    [Fact]
    public void IsMissing_NonExistentLocalPath_True() =>
        Assert.True(TargetCheck.IsMissing(Path.Combine(_tempRoot, "nope.exe")));

    [Fact]
    public void IsMissing_Unc_NeverCheckedNetworkPath_False() =>
        Assert.False(TargetCheck.IsMissing(@"\\some-unreachable-server\share\file.txt"));

    [Fact]
    public void IsMissing_Url_NeverChecked_False() =>
        Assert.False(TargetCheck.IsMissing("https://example.com/definitely-not-a-real-page"));

    [Fact]
    public void IsMissing_Empty_False() =>
        Assert.False(TargetCheck.IsMissing(""));

    [Fact]
    public void IsMissing_BareName_ResolvableOnPath_False()
    {
        var exePath = Path.Combine(_tempRoot, "tool.exe");
        File.WriteAllText(exePath, "");
        Environment.SetEnvironmentVariable("PATH", _tempRoot);

        Assert.False(TargetCheck.IsMissing("tool.exe"));
    }

    [Fact]
    public void IsMissing_BareName_NotResolvableOnPath_True()
    {
        Environment.SetEnvironmentVariable("PATH", _tempRoot);
        Assert.True(TargetCheck.IsMissing("definitely-not-a-real-tool.exe"));
    }

    [Fact]
    public void IsMissing_EnvVariableExpandedBeforeChecking()
    {
        var file = Path.Combine(_tempRoot, "target.txt");
        File.WriteAllText(file, "x");
        Environment.SetEnvironmentVariable("YL_TEST_TARGET_DIR", _tempRoot);
        try
        {
            Assert.False(TargetCheck.IsMissing("%YL_TEST_TARGET_DIR%\\target.txt"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("YL_TEST_TARGET_DIR", null);
        }
    }

    // ---- ResolveExistingPath (spec §10 item 7: "Open file location") ----

    [Fact]
    public void ResolveExistingPath_ExistingFile_ReturnsExpandedPath()
    {
        var file = Path.Combine(_tempRoot, "exists.txt");
        File.WriteAllText(file, "x");
        Assert.Equal(file, TargetCheck.ResolveExistingPath(file));
    }

    [Fact]
    public void ResolveExistingPath_ExistingDirectory_ReturnsPath() =>
        Assert.Equal(_tempRoot, TargetCheck.ResolveExistingPath(_tempRoot));

    [Fact]
    public void ResolveExistingPath_NonExistentLocalPath_ReturnsNull() =>
        Assert.Null(TargetCheck.ResolveExistingPath(Path.Combine(_tempRoot, "nope.exe")));

    [Fact]
    public void ResolveExistingPath_BareName_ResolvableOnPath_ReturnsFullPath()
    {
        var exePath = Path.Combine(_tempRoot, "tool.exe");
        File.WriteAllText(exePath, "");
        Environment.SetEnvironmentVariable("PATH", _tempRoot);

        Assert.Equal(exePath, TargetCheck.ResolveExistingPath("tool.exe"));
    }

    [Fact]
    public void ResolveExistingPath_BareName_NotResolvable_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("PATH", _tempRoot);
        Assert.Null(TargetCheck.ResolveExistingPath("definitely-not-a-real-tool.exe"));
    }

    [Fact]
    public void ResolveExistingPath_Unc_NeverResolved_ReturnsNull() =>
        Assert.Null(TargetCheck.ResolveExistingPath(@"\\some-unreachable-server\share\file.txt"));

    [Fact]
    public void ResolveExistingPath_Empty_ReturnsNull() =>
        Assert.Null(TargetCheck.ResolveExistingPath(""));
}
