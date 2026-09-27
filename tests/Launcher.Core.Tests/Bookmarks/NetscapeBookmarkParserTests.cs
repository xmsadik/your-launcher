using YourLauncher.Core.Bookmarks;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests.Bookmarks;

public class NetscapeBookmarkParserTests
{
    [Fact]
    public void Parse_TopLevelLink_AddedDirectlyToResultFolder()
    {
        const string html = """
            <!DOCTYPE NETSCAPE-Bookmark-file-1>
            <H1>Bookmarks</H1>
            <DL><p>
                <DT><A HREF="https://example.com">Example</A>
            </DL><p>
            """;

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");

        Assert.Equal("Bookmarks (export)", result.Name);
        var url = Assert.IsType<UrlNode>(Assert.Single(result.Children));
        Assert.Equal("Example", url.Name);
        Assert.Equal("https://example.com", url.Target);
    }

    [Fact]
    public void Parse_FolderWithChildren_NestedCorrectly()
    {
        const string html = """
            <DL><p>
                <DT><H3>Dev</H3>
                <DL><p>
                    <DT><A HREF="https://github.com">GitHub</A>
                    <DT><A HREF="https://gitlab.com">GitLab</A>
                </DL><p>
            </DL><p>
            """;

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");

        var dev = Assert.IsType<FolderNode>(Assert.Single(result.Children));
        Assert.Equal("Dev", dev.Name);
        Assert.Equal(new[] { "GitHub", "GitLab" }, dev.Children.Select(c => c.Name));
    }

    [Fact]
    public void Parse_TopLevelAndNestedLinks_PreservesOrder()
    {
        const string html = """
            <DL><p>
                <DT><A HREF="https://first.example">First</A>
                <DT><H3>Dev</H3>
                <DL><p>
                    <DT><A HREF="https://inner.example">Inner</A>
                </DL><p>
                <DT><A HREF="https://last.example">Last</A>
            </DL><p>
            """;

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");

        Assert.Equal(new[] { "First", "Dev", "Last" }, result.Children.Select(c => c.Name));
    }

    [Fact]
    public void Parse_H1_Ignored()
    {
        const string html = """
            <H1>My Bookmarks Menu</H1>
            <DL><p>
                <DT><A HREF="https://a.example">A</A>
            </DL><p>
            """;

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");

        Assert.Single(result.Children); // only the <A>, nothing derived from <H1>.
    }

    [Fact]
    public void Parse_CaseInsensitiveTags_StillParsed()
    {
        const string html = """
            <dl><p>
                <dt><a href="https://example.com">Example</a>
            </dl><p>
            """;

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");

        var url = Assert.IsType<UrlNode>(Assert.Single(result.Children));
        Assert.Equal("https://example.com", url.Target);
    }

    [Fact]
    public void Parse_HtmlEntities_DecodedInNameAndHref()
    {
        const string html = """
            <DL><p>
                <DT><A HREF="https://example.com/?a=1&amp;b=2">Tom &amp; Jerry</A>
            </DL><p>
            """;

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");
        var url = (UrlNode)result.Children[0];

        Assert.Equal("Tom & Jerry", url.Name);
        Assert.Equal("https://example.com/?a=1&b=2", url.Target);
    }

    [Fact]
    public void Parse_JavascriptBookmarklet_Skipped()
    {
        const string html = """
            <DL><p>
                <DT><A HREF="javascript:alert(1)">Bookmarklet</A>
                <DT><A HREF="https://kept.example">Kept</A>
            </DL><p>
            """;

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");

        var url = Assert.IsType<UrlNode>(Assert.Single(result.Children));
        Assert.Equal("Kept", url.Name);
    }

    [Fact]
    public void Parse_LinkWithoutHref_Skipped()
    {
        const string html = """
            <DL><p>
                <DT><A>No href</A>
                <DT><A HREF="https://kept.example">Kept</A>
            </DL><p>
            """;

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");

        var url = Assert.IsType<UrlNode>(Assert.Single(result.Children));
        Assert.Equal("Kept", url.Name);
    }

    [Fact]
    public void Parse_EmptyTitle_UsesUrlHost()
    {
        const string html = """
            <DL><p>
                <DT><A HREF="https://example.com/page"></A>
            </DL><p>
            """;

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");
        var url = (UrlNode)result.Children[0];

        Assert.Equal("example.com", url.Name);
    }

    [Fact]
    public void Parse_EmptyFolderName_UsesUnnamed()
    {
        const string html = """
            <DL><p>
                <DT><H3></H3>
                <DL><p>
                    <DT><A HREF="https://x.example">X</A>
                </DL><p>
            </DL><p>
            """;

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");
        var folder = (FolderNode)result.Children[0];

        Assert.Equal("(unnamed)", folder.Name);
    }

    [Fact]
    public void Parse_NestedEmptyFolder_PrunedRecursively()
    {
        const string html = """
            <DL><p>
                <DT><H3>Dev</H3>
                <DL><p>
                    <DT><H3>Empty</H3>
                    <DL><p>
                    </DL><p>
                </DL><p>
            </DL><p>
            """;

        Assert.Throws<BookmarkImportException>(() => NetscapeBookmarkParser.Parse(html, "Bookmarks (export)"));
    }

    [Fact]
    public void Parse_NoBookmarksAtAll_Throws()
    {
        const string html = "<H1>Bookmarks</H1><DL><p></DL><p>";

        var ex = Assert.Throws<BookmarkImportException>(() => NetscapeBookmarkParser.Parse(html, "Bookmarks (myfile)"));
        Assert.Contains("Bookmarks (myfile)", ex.Message);
    }

    [Fact]
    public void Parse_UnclosedDtAndP_TreatedAsNormal()
    {
        // No closing </DT> or </p> anywhere - exactly how real Netscape exports look (spec: "unclosed <DT>/<p> are normal").
        const string html = "<DL><p><DT><A HREF=\"https://a.example\">A</A><DT><A HREF=\"https://b.example\">B</A></DL><p>";

        var result = NetscapeBookmarkParser.Parse(html, "Bookmarks (export)");

        Assert.Equal(new[] { "A", "B" }, result.Children.Select(c => c.Name));
    }
}
