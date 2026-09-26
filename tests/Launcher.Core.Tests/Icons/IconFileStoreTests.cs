using YourLauncher.Core.Icons;

namespace YourLauncher.Core.Tests.Icons;

public class IconFileStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public IconFileStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "YourLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Import_CreatesIconsDirIfMissing()
    {
        var source = WriteSourceFile("a.png", "hello");
        var iconsDir = Path.Combine(_tempRoot, "icons");
        Assert.False(Directory.Exists(iconsDir));

        IconFileStore.Import(source, iconsDir);

        Assert.True(Directory.Exists(iconsDir));
    }

    [Fact]
    public void Import_NamesFileByContentHash()
    {
        var source = WriteSourceFile("a.png", "hello world");
        var iconsDir = Path.Combine(_tempRoot, "icons");

        var dest = IconFileStore.Import(source, iconsDir);

        var fileName = Path.GetFileName(dest);
        Assert.Matches("^[0-9a-f]{16}\\.png$", fileName);
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public void Import_SameContent_ReusesExistingFile_NoDuplicate()
    {
        var source1 = WriteSourceFile("a.png", "identical content");
        var source2 = WriteSourceFile("b.png", "identical content");
        var iconsDir = Path.Combine(_tempRoot, "icons");

        var dest1 = IconFileStore.Import(source1, iconsDir);
        var dest2 = IconFileStore.Import(source2, iconsDir);

        Assert.Equal(dest1, dest2);
        Assert.Single(Directory.GetFiles(iconsDir));
    }

    [Fact]
    public void Import_DifferentContent_ProducesDifferentFiles()
    {
        var source1 = WriteSourceFile("a.png", "content A");
        var source2 = WriteSourceFile("b.png", "content B");
        var iconsDir = Path.Combine(_tempRoot, "icons");

        var dest1 = IconFileStore.Import(source1, iconsDir);
        var dest2 = IconFileStore.Import(source2, iconsDir);

        Assert.NotEqual(dest1, dest2);
        Assert.Equal(2, Directory.GetFiles(iconsDir).Length);
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".txt")]
    [InlineData(".svg")]
    [InlineData("")]
    public void Import_DisallowedExtension_Throws(string ext)
    {
        var source = WriteSourceFile("a" + ext, "data");
        var iconsDir = Path.Combine(_tempRoot, "icons");

        Assert.Throws<ArgumentException>(() => IconFileStore.Import(source, iconsDir));
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".ico")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".bmp")]
    [InlineData(".gif")]
    [InlineData(".PNG")]
    public void Import_AllowedExtensions_Succeed(string ext)
    {
        var source = WriteSourceFile("a" + ext, "data");
        var iconsDir = Path.Combine(_tempRoot, "icons");

        var dest = IconFileStore.Import(source, iconsDir);

        Assert.True(File.Exists(dest));
        Assert.Equal(ext.ToLowerInvariant(), Path.GetExtension(dest));
    }

    private string WriteSourceFile(string name, string content)
    {
        var path = Path.Combine(_tempRoot, name);
        File.WriteAllText(path, content);
        return path;
    }
}
