using YourLauncher.Core.Bookmarks;
using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests.Bookmarks;

public class BookmarkImportTests
{
    private static FolderNode Root(params Node[] children) => new() { Id = "root", Name = "Root", Children = children.ToList() };

    private static FolderNode Folder(string id, string name, params Node[] children) =>
        new() { Id = id, Name = name, Children = children.ToList() };

    private static UrlNode Url(string name, string target) => new() { Id = TreeOps.NewId(), Name = name, Target = target };

    private static FolderNode Imported(params Node[] children) => new() { Id = TreeOps.NewId(), Name = "Chrome bookmarks", Children = children.ToList() };

    [Fact]
    public void Apply_FirstImport_AppendsToTargetFolder()
    {
        var root = Root();
        var imported = Imported(Url("A", "https://a.example"), Url("B", "https://b.example"));

        var (folder, replaced, count) = BookmarkImport.Apply(root, root, imported, "chrome/default");

        Assert.False(replaced);
        Assert.Equal(2, count);
        Assert.Same(imported, folder);
        Assert.Equal("bookmarks:chrome/default", folder.Id);
        Assert.Single(root.Children);
        Assert.Same(imported, root.Children[0]);
    }

    [Fact]
    public void Apply_FirstImport_AppendsToGivenTargetFolder_NotAlwaysRoot()
    {
        var sub = Folder("sub", "Sub");
        var root = Root(sub);
        var imported = Imported(Url("A", "https://a.example"));

        BookmarkImport.Apply(root, sub, imported, "chrome/default");

        Assert.Single(root.Children); // "sub" itself - Apply never touches root's own children when targetFolder is a nested folder.
        Assert.Same(sub, root.Children[0]);
        Assert.Single(sub.Children);
        Assert.Same(imported, sub.Children[0]);
    }

    [Fact]
    public void Apply_ReImport_ReplacesChildrenInPlace_KeepsNameAndPosition()
    {
        var existing = Folder("bookmarks:chrome/default", "My Renamed Bookmarks", Url("Old", "https://old.example"));
        var sibling = Folder("sibling", "Sibling");
        var root = Root(sibling, existing); // existing was moved to 2nd position by the user.

        var imported = Imported(Url("New", "https://new.example"));
        var (folder, replaced, count) = BookmarkImport.Apply(root, root, imported, "chrome/default");

        Assert.True(replaced);
        Assert.Equal(1, count);
        Assert.Same(existing, folder);
        Assert.Equal("My Renamed Bookmarks", folder.Name); // name kept, not overwritten by the import's own name.
        Assert.Equal("bookmarks:chrome/default", folder.Id); // id unchanged.
        Assert.Equal(new[] { "sibling", "bookmarks:chrome/default" }, root.Children.Select(c => c.Id)); // position unchanged.
        var url = Assert.IsType<UrlNode>(Assert.Single(folder.Children));
        Assert.Equal("New", url.Name);
    }

    [Fact]
    public void Apply_ReImport_FindsPreviousImportEvenIfMovedToAnotherFolder()
    {
        var moved = Folder("bookmarks:chrome/default", "Chrome bookmarks", Url("Old", "https://old.example"));
        var otherFolder = Folder("other", "Other", moved); // user moved the imported folder into "Other".
        var root = Root(otherFolder);

        var imported = Imported(Url("New", "https://new.example"));
        var (folder, replaced, _) = BookmarkImport.Apply(root, root, imported, "chrome/default");

        Assert.True(replaced);
        Assert.Same(moved, folder);
        Assert.Contains(moved, otherFolder.Children); // still where the user put it, not moved back to targetFolder.
    }

    [Fact]
    public void Apply_DifferentSourceKey_DoesNotMatchPreviousImport()
    {
        var existingChrome = Folder("bookmarks:chrome/default", "Chrome bookmarks", Url("Old", "https://old.example"));
        var root = Root(existingChrome);

        var imported = Imported(Url("New", "https://new.example"));
        var (folder, replaced, _) = BookmarkImport.Apply(root, root, imported, "edge/default"); // different source

        Assert.False(replaced);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("bookmarks:edge/default", folder.Id);
    }

    [Fact]
    public void Apply_CountsOnlyUrlNodes_NotFolders()
    {
        var imported = Imported(
            Url("A", "https://a.example"),
            Folder("f", "Sub", Url("B", "https://b.example"), Url("C", "https://c.example")));
        var root = Root();

        var (_, _, count) = BookmarkImport.Apply(root, root, imported, "chrome/default");

        Assert.Equal(3, count);
    }

    [Fact]
    public void ImportedFolderId_IsDeterministicPerSourceKey()
    {
        Assert.Equal("bookmarks:chrome/default", BookmarkImport.ImportedFolderId("chrome/default"));
        Assert.Equal("bookmarks:html/bookmarks.html", BookmarkImport.ImportedFolderId("html/bookmarks.html"));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\Bookmarks.html", "html/bookmarks.html")]
    [InlineData(@"C:\Users\me\Downloads\MY-EXPORT.HTM", "html/my-export.htm")]
    public void HtmlSourceKey_IsLowercasedFileName(string path, string expected)
    {
        Assert.Equal(expected, BookmarkImport.HtmlSourceKey(path));
    }

    [Fact]
    public void Apply_IdTakenByNonFolder_NeverCreatesDuplicateId()
    {
        var root = Root(Url("Hand-edited", "https://x.example"));
        root.Children[0].Id = "bookmarks:chrome/default";

        var (folder, replaced, _) = BookmarkImport.Apply(root, root, Imported(Url("A", "https://a.example")), "chrome/default");

        Assert.False(replaced);
        Assert.NotEqual("bookmarks:chrome/default", folder.Id);
        Assert.Equal(2, root.Children.Count);
    }
}
