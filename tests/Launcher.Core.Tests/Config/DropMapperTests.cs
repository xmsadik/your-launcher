using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests.Config;

/// <summary>
/// <see cref="DropMapper.Map"/> mapping table (spec §6/§10 items 6d/6f) - <paramref name="resolveShellLink"/>
/// below stands in for the real <c>IShellLinkW</c> resolution (App layer, Windows-only), so this exercises
/// the pure mapping decision entirely off fake shortcut info.
/// </summary>
public class DropMapperTests
{
    private static ShellLinkInfo NoLink(string _) => throw new InvalidOperationException("Not a .lnk in this test.");

    // ---- .exe / .bat / .cmd / .com / .msc dropped directly -> app (spec §10 item 6f) ----

    [Theory]
    [InlineData(@"C:\Tools\build.exe")]
    [InlineData(@"C:\Tools\run.bat")]
    [InlineData(@"C:\Tools\run.cmd")]
    [InlineData(@"C:\Tools\run.com")]
    [InlineData(@"C:\Windows\System32\compmgmt.msc")]
    public void Map_AppExtension_ReturnsAppNode(string path)
    {
        var node = DropMapper.Map(path, NoLink, fileDescription: null);

        var app = Assert.IsType<AppNode>(node);
        Assert.Equal(path, app.Target);
    }

    [Fact]
    public void Map_Exe_NameComesFromFileDescription()
    {
        var node = DropMapper.Map(@"C:\Tools\build.exe", NoLink, path => path.EndsWith("build.exe") ? "Build Tool" : null);

        Assert.Equal("Build Tool", node.Name);
    }

    // ---- Anything else (file or directory) -> path, name via TargetNameHelper ----

    [Fact]
    public void Map_TextFile_ReturnsPathNode()
    {
        var node = DropMapper.Map(@"C:\Notes\todo.txt", NoLink, fileDescription: null);

        var path = Assert.IsType<PathNode>(node);
        Assert.Equal(@"C:\Notes\todo.txt", path.Target);
        Assert.Equal("todo", path.Name);
    }

    [Fact]
    public void Map_Directory_ReturnsPathNode()
    {
        var node = DropMapper.Map(@"C:\Projects\MyApp", NoLink, fileDescription: null);

        var path = Assert.IsType<PathNode>(node);
        Assert.Equal(@"C:\Projects\MyApp", path.Target);
    }

    // ---- .url -> url node ----

    [Fact]
    public void Map_UrlShortcut_ReadsUrlFromFile()
    {
        var file = Path.Combine(Path.GetTempPath(), $"YourLauncherTest_{Guid.NewGuid():N}.url");
        File.WriteAllText(file, "[InternetShortcut]\r\nURL=https://example.com/page\r\n");
        try
        {
            var node = DropMapper.Map(file, NoLink, fileDescription: null);

            var url = Assert.IsType<UrlNode>(node);
            Assert.Equal("https://example.com/page", url.Target);
            Assert.Equal(Path.GetFileNameWithoutExtension(file), url.Name);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Map_UrlShortcut_MissingUrlLine_EmptyTarget()
    {
        var file = Path.Combine(Path.GetTempPath(), $"YourLauncherTest_{Guid.NewGuid():N}.url");
        File.WriteAllText(file, "[InternetShortcut]\r\n");
        try
        {
            var node = DropMapper.Map(file, NoLink, fileDescription: null);

            var url = Assert.IsType<UrlNode>(node);
            Assert.Equal("", url.Target);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- .lnk -> app or path depending on the resolved target (spec §6/§10 item 6d/6f) ----

    [Fact]
    public void Map_Lnk_ExeTarget_ReturnsAppNodeWithArgsAndWorkingDir()
    {
        var info = new ShellLinkInfo(@"C:\Tools\build.exe", "--release", @"C:\Tools", null, null);
        var node = DropMapper.Map(@"C:\Shortcuts\Build.lnk", _ => info, fileDescription: null);

        var app = Assert.IsType<AppNode>(node);
        Assert.Equal("Build", app.Name); // .lnk file name without extension.
        Assert.Equal(@"C:\Tools\build.exe", app.Target);
        Assert.Equal("--release", app.Arguments);
        Assert.Equal(@"C:\Tools", app.WorkingDirectory);
    }

    [Theory]
    [InlineData(".bat")]
    [InlineData(".cmd")]
    [InlineData(".com")]
    [InlineData(".msc")]
    public void Map_Lnk_ToNonExeAppExtensionTarget_ReturnsAppNode(string ext)
    {
        var target = @"C:\Tools\run" + ext;
        var info = new ShellLinkInfo(target, null, null, null, null);
        var node = DropMapper.Map(@"C:\Shortcuts\Run.lnk", _ => info, fileDescription: null);

        var app = Assert.IsType<AppNode>(node);
        Assert.Equal(target, app.Target);
    }

    [Fact]
    public void Map_Lnk_NonExeTarget_ReturnsPathNode()
    {
        var info = new ShellLinkInfo(@"C:\Reports\weekly.xlsx", null, null, null, null);
        var node = DropMapper.Map(@"C:\Shortcuts\Report.lnk", _ => info, fileDescription: null);

        var path = Assert.IsType<PathNode>(node);
        Assert.Equal(@"C:\Reports\weekly.xlsx", path.Target);
        Assert.Equal("Report", path.Name);
    }

    [Fact]
    public void Map_Lnk_DirectoryTarget_ReturnsPathNode()
    {
        var info = new ShellLinkInfo(@"C:\Projects\MyApp", null, null, null, null);
        var node = DropMapper.Map(@"C:\Shortcuts\MyApp.lnk", _ => info, fileDescription: null);

        Assert.IsType<PathNode>(node);
    }

    [Fact]
    public void Map_Lnk_NoTarget_AdvertisedShortcut_ReturnsAppNodeTargetingTheLnkItself()
    {
        var info = new ShellLinkInfo(null, null, null, null, null);
        var node = DropMapper.Map(@"C:\Shortcuts\Office.lnk", _ => info, fileDescription: null);

        var app = Assert.IsType<AppNode>(node);
        Assert.Equal(@"C:\Shortcuts\Office.lnk", app.Target);
        Assert.Equal("Office", app.Name);
    }

    [Fact]
    public void Map_Lnk_WithIconLocation_SetsExeIconSpec()
    {
        var info = new ShellLinkInfo(@"C:\Tools\build.exe", null, null, @"C:\Tools\build.exe", 2);
        var node = DropMapper.Map(@"C:\Shortcuts\Build.lnk", _ => info, fileDescription: null);

        Assert.NotNull(node.Icon);
        Assert.Equal(IconKind.Exe, node.Icon!.Kind);
        Assert.Equal(@"C:\Tools\build.exe", node.Icon.Value);
        Assert.Equal(2, node.Icon.Index);
    }

    [Fact]
    public void Map_Lnk_WithoutIconLocation_LeavesIconNull_DefaultAutoIcon()
    {
        var info = new ShellLinkInfo(@"C:\Tools\build.exe", null, null, null, null);
        var node = DropMapper.Map(@"C:\Shortcuts\Build.lnk", _ => info, fileDescription: null);

        Assert.Null(node.Icon);
    }

    [Fact]
    public void Map_Lnk_EnvVarAndRelativeTargetPaths_PreservedAsReturned()
    {
        // Spec §10 item 6d: GetPath(SLGP_RAWPATH) preserves env vars/relative segments as-is - DropMapper
        // must not normalize/expand them itself.
        var info = new ShellLinkInfo(@"%LOCALAPPDATA%\Tools\build.exe", null, @"..\workdir", null, null);
        var node = DropMapper.Map(@"C:\Shortcuts\Build.lnk", _ => info, fileDescription: null);

        var app = Assert.IsType<AppNode>(node);
        Assert.Equal(@"%LOCALAPPDATA%\Tools\build.exe", app.Target);
        Assert.Equal(@"..\workdir", app.WorkingDirectory);
    }
}
