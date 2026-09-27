using YourLauncher.Core.Bookmarks;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests.Bookmarks;

public class ChromiumBookmarkParserTests
{
    private const string RootsWrapper = """{"roots":{"bookmark_bar":{{BAR}},"other":{{OTHER}},"synced":{{SYNCED}}}}""";

    private static string Wrap(string bar = """{"children":[]}""", string other = """{"children":[]}""", string synced = """{"children":[]}""") =>
        RootsWrapper.Replace("{{BAR}}", bar).Replace("{{OTHER}}", other).Replace("{{SYNCED}}", synced);

    [Fact]
    public void Parse_SimpleUrl_AddedUnderBookmarksBar()
    {
        var json = Wrap(bar: """{"children":[{"type":"url","name":"Example","url":"https://example.com"}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");

        Assert.Equal("Chrome bookmarks", result.Name);
        var bar = Assert.IsType<FolderNode>(Assert.Single(result.Children));
        Assert.Equal("Bookmarks bar", bar.Name);
        var url = Assert.IsType<UrlNode>(Assert.Single(bar.Children));
        Assert.Equal("Example", url.Name);
        Assert.Equal("https://example.com", url.Target);
    }

    [Fact]
    public void Parse_NestedFolder_PreservesOrder()
    {
        var json = Wrap(bar: """
            {"children":[
                {"type":"url","name":"First","url":"https://a.example"},
                {"type":"folder","name":"Dev","children":[
                    {"type":"url","name":"GitHub","url":"https://github.com"},
                    {"type":"url","name":"GitLab","url":"https://gitlab.com"}
                ]},
                {"type":"url","name":"Last","url":"https://b.example"}
            ]}
            """);

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");
        var bar = (FolderNode)result.Children[0];

        Assert.Equal(new[] { "First", "Dev", "Last" }, bar.Children.Select(c => c.Name));
        var dev = Assert.IsType<FolderNode>(bar.Children[1]);
        Assert.Equal(new[] { "GitHub", "GitLab" }, dev.Children.Select(c => c.Name));
    }

    [Fact]
    public void Parse_EmptyRoots_AllDropped()
    {
        var json = Wrap(); // bar/other/synced all {"children":[]}

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");

        Assert.Empty(result.Children);
    }

    [Fact]
    public void Parse_OtherAndSyncedRoots_UseFixedNames()
    {
        var json = Wrap(
            other: """{"children":[{"type":"url","name":"Other link","url":"https://o.example"}]}""",
            synced: """{"children":[{"type":"url","name":"Synced link","url":"https://s.example"}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");

        Assert.Equal(new[] { "Other bookmarks", "Mobile bookmarks" }, result.Children.Select(c => c.Name));
    }

    [Fact]
    public void Parse_JavascriptBookmarklet_Skipped()
    {
        var json = Wrap(bar: """{"children":[{"type":"url","name":"Bookmarklet","url":"javascript:alert(1)"}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");

        Assert.Empty(result.Children); // the bar ends up empty and is dropped too.
    }

    [Fact]
    public void Parse_EmptyTitle_UsesUrlHost()
    {
        var json = Wrap(bar: """{"children":[{"type":"url","name":"","url":"https://example.com/page"}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");
        var url = (UrlNode)((FolderNode)result.Children[0]).Children[0];

        Assert.Equal("example.com", url.Name);
    }

    [Fact]
    public void Parse_EmptyTitle_NoHost_UsesUrlItself()
    {
        var json = Wrap(bar: """{"children":[{"type":"url","name":"","url":"about:blank"}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");
        var url = (UrlNode)((FolderNode)result.Children[0]).Children[0];

        Assert.Equal("about:blank", url.Name);
    }

    [Fact]
    public void Parse_EmptyFolderName_UsesUnnamed()
    {
        var json = Wrap(bar: """{"children":[{"type":"folder","name":"","children":[{"type":"url","name":"X","url":"https://x.example"}]}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");
        var folder = (FolderNode)((FolderNode)result.Children[0]).Children[0];

        Assert.Equal("(unnamed)", folder.Name);
    }

    [Fact]
    public void Parse_NestedEmptyFolder_PrunedRecursively()
    {
        var json = Wrap(bar: """{"children":[{"type":"folder","name":"Dev","children":[{"type":"folder","name":"Empty","children":[]}]}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");

        Assert.Empty(result.Children); // "Empty" is dropped, which then leaves "Dev" empty too, which is also dropped, which leaves the bar empty, which is dropped too.
    }

    [Fact]
    public void Parse_NodeWithoutUrl_Skipped()
    {
        var json = Wrap(bar: """{"children":[{"type":"url","name":"No target"}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");

        Assert.Empty(result.Children);
    }

    [Fact]
    public void Parse_UnknownNodeType_Skipped()
    {
        var json = Wrap(bar: """{"children":[{"type":"trash","name":"Whatever"},{"type":"url","name":"Kept","url":"https://kept.example"}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");
        var bar = (FolderNode)result.Children[0];

        var url = Assert.IsType<UrlNode>(Assert.Single(bar.Children));
        Assert.Equal("Kept", url.Name);
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsBookmarkImportException()
    {
        Assert.Throws<BookmarkImportException>(() => ChromiumBookmarkParser.Parse("{ not valid json", "Chrome bookmarks"));
    }

    [Fact]
    public void Parse_MissingRoots_ThrowsBookmarkImportException()
    {
        Assert.Throws<BookmarkImportException>(() => ChromiumBookmarkParser.Parse("{}", "Chrome bookmarks"));
    }

    [Fact]
    public void Parse_EveryNode_GetsAFreshId()
    {
        var json = Wrap(bar: """{"children":[{"type":"url","name":"A","url":"https://a.example"},{"type":"url","name":"B","url":"https://b.example"}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");
        var bar = (FolderNode)result.Children[0];

        var ids = new[] { result.Id, bar.Id }.Concat(bar.Children.Select(c => c.Id)).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void Parse_RootNotAnObject_ThrowsBookmarkImportException(string json)
    {
        Assert.Throws<BookmarkImportException>(() => ChromiumBookmarkParser.Parse(json, "Chrome bookmarks"));
    }

    [Fact]
    public void Parse_NonObjectChildren_AreSkipped()
    {
        var json = Wrap(bar: """{"children":[null,"x",42,{"type":"url","name":"Ok","url":"https://ok.example"}]}""");

        var result = ChromiumBookmarkParser.Parse(json, "Chrome bookmarks");

        var bar = Assert.IsType<FolderNode>(Assert.Single(result.Children));
        Assert.Equal("Ok", Assert.IsType<UrlNode>(Assert.Single(bar.Children)).Name);
    }

    [Fact]
    public void Parse_MultipleFiles_MergedUnderSameRoots_InFileOrder()
    {
        var account = Wrap(bar: """{"children":[{"type":"url","name":"Account","url":"https://a.example"}]}""");
        var local = Wrap(
            bar: """{"children":[{"type":"url","name":"Local","url":"https://l.example"}]}""",
            other: """{"children":[{"type":"url","name":"Other","url":"https://o.example"}]}""");

        var result = ChromiumBookmarkParser.Parse(new[] { account, local }, "Chrome bookmarks");

        Assert.Equal(new[] { "Bookmarks bar", "Other bookmarks" }, result.Children.Select(c => c.Name));
        var bar = Assert.IsType<FolderNode>(result.Children[0]);
        Assert.Equal(new[] { "Account", "Local" }, bar.Children.Select(c => c.Name));
    }

    [Fact]
    public void Parse_AccountFileEmpty_LocalFileEmpty_ResultHasNoChildren()
    {
        var result = ChromiumBookmarkParser.Parse(new[] { Wrap(), Wrap() }, "Chrome bookmarks");

        Assert.Empty(result.Children);
    }
}
