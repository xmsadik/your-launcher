using YourLauncher.Core.Icons;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests.Icons;

public class IconKeyTests
{
    [Fact]
    public void For_GlyphSpec_ReturnsNull()
    {
        var node = new AppNode { Target = "C:\\a.exe" };
        Assert.Null(IconKey.For(new IconSpec { Kind = IconKind.Glyph, Value = "\uE8B7" }, node));
    }

    [Fact]
    public void For_EmojiSpec_ReturnsNull()
    {
        var node = new AppNode { Target = "C:\\a.exe" };
        Assert.Null(IconKey.For(new IconSpec { Kind = IconKind.Emoji, Value = "📁" }, node));
    }

    [Fact]
    public void For_FileSpec_IsStableAndIncludesKindValueIndex()
    {
        var node = new PathNode();
        var spec = new IconSpec { Kind = IconKind.File, Value = "C:\\icons\\x.png" };

        var key1 = IconKey.For(spec, node);
        var key2 = IconKey.For(spec, node);

        Assert.NotNull(key1);
        Assert.Equal(key1, key2);
        Assert.StartsWith("file|", key1);
        Assert.Contains("c:\\icons\\x.png", key1);
    }

    [Fact]
    public void For_ExeSpec_DifferentIndexProducesDifferentKey()
    {
        var node = new AppNode();
        var key0 = IconKey.For(new IconSpec { Kind = IconKind.Exe, Value = "C:\\a.exe", Index = 0 }, node);
        var key1 = IconKey.For(new IconSpec { Kind = IconKind.Exe, Value = "C:\\a.exe", Index = 1 }, node);

        Assert.NotEqual(key0, key1);
    }

    [Fact]
    public void For_ExeSpec_NullIndexTreatedAsZero()
    {
        var node = new AppNode();
        var keyNull = IconKey.For(new IconSpec { Kind = IconKind.Exe, Value = "C:\\a.exe", Index = null }, node);
        var keyZero = IconKey.For(new IconSpec { Kind = IconKind.Exe, Value = "C:\\a.exe", Index = 0 }, node);

        Assert.Equal(keyZero, keyNull);
    }

    [Fact]
    public void For_FileVsExe_SameValueDifferentKind_ProducesDifferentKeys()
    {
        var node = new AppNode();
        var fileKey = IconKey.For(new IconSpec { Kind = IconKind.File, Value = "C:\\a.dll" }, node);
        var exeKey = IconKey.For(new IconSpec { Kind = IconKind.Exe, Value = "C:\\a.dll" }, node);

        Assert.NotEqual(fileKey, exeKey);
    }

    [Fact]
    public void For_CustomValue_IsCaseInsensitiveAndEnvExpanded()
    {
        var node = new AppNode();
        Environment.SetEnvironmentVariable("YL_TEST_ICON_DIR", "C:\\Icons");
        try
        {
            var key1 = IconKey.For(new IconSpec { Kind = IconKind.File, Value = "%YL_TEST_ICON_DIR%\\X.PNG" }, node);
            var key2 = IconKey.For(new IconSpec { Kind = IconKind.File, Value = "c:\\icons\\x.png" }, node);
            Assert.Equal(key1, key2);
        }
        finally
        {
            Environment.SetEnvironmentVariable("YL_TEST_ICON_DIR", null);
        }
    }

    [Fact]
    public void For_AutoApp_UsesExpandedLowercaseTarget()
    {
        var node = new AppNode { Target = "C:\\Program Files\\App\\App.EXE" };
        var key = IconKey.For(null, node);

        Assert.Equal("sys|c:\\program files\\app\\app.exe", key);
    }

    [Fact]
    public void For_AutoPath_UsesExpandedLowercaseTarget()
    {
        var node = new PathNode { Target = "D:\\Docs\\Report.PDF" };
        var key = IconKey.For(null, node);

        Assert.Equal("sys|d:\\docs\\report.pdf", key);
    }

    [Fact]
    public void For_AutoApp_DifferentTargets_ProduceDifferentKeys()
    {
        var a = IconKey.For(null, new AppNode { Target = "C:\\a.exe" });
        var b = IconKey.For(null, new AppNode { Target = "C:\\b.exe" });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void For_AutoFolder_ReturnsDefaultGlyphKey()
    {
        Assert.Equal("glyph|default-folder", IconKey.For(null, new FolderNode()));
    }

    [Fact]
    public void For_AutoCommand_ReturnsDefaultGlyphKey()
    {
        Assert.Equal("glyph|default-command", IconKey.For(null, new CommandNode()));
    }

    [Fact]
    public void For_AutoUrl_ReturnsDefaultGlyphKey()
    {
        Assert.Equal("glyph|default-url", IconKey.For(null, new UrlNode()));
    }
}
