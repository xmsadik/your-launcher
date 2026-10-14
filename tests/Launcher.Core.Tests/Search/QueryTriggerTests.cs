using YourLauncher.Core.Model;
using YourLauncher.Core.Search;

namespace YourLauncher.Core.Tests.Search;

public class QueryTriggerTests
{
    private static FlatIndex Index(params Node[] children) =>
        FlatIndex.Build(new FolderNode { Id = "root", Name = "Root", Children = children.ToList() });

    private static UrlNode Jira(params string[] keywords) => new()
    {
        Id = "jira", Name = "Jira", Target = "https://j/browse/{query}", Keywords = keywords.ToList(),
    };

    [Theory]
    [InlineData("jira ABC-123", "ABC-123")]
    [InlineData("JIRA   ABC-123  ", "ABC-123")]
    [InlineData("  jira ABC 123 more", "ABC 123 more")]
    [InlineData("jira\tABC", "ABC")]
    public void Match_ByNameWord_ReturnsTheRestAsQuery(string text, string expectedQuery)
    {
        var matches = QueryTrigger.Match(Index(Jira()), text);

        var match = Assert.Single(matches);
        Assert.Equal("jira", match.Node.Id);
        Assert.Equal(expectedQuery, match.Query);
    }

    [Fact]
    public void Match_ByKeyword_IsCaseInsensitive()
    {
        var matches = QueryTrigger.Match(Index(Jira("tickets", "JQ")), "jq ABC-1");

        Assert.Single(matches);
    }

    [Fact]
    public void Match_IsTurkishAware()
    {
        var node = new UrlNode { Id = "s", Name = "Şirket", Target = "https://s/?q={query}" };
        var otherKeyword = new UrlNode { Id = "i", Name = "İş", Target = "https://i/?q={query}", Keywords = { "ISLER" } };

        Assert.Single(QueryTrigger.Match(Index(node), "SIRKET abc"));
        Assert.Single(QueryTrigger.Match(Index(node), "şirket abc"));
        Assert.Single(QueryTrigger.Match(Index(otherKeyword), "işler abc")); // İ/I and ş/s fold together
        Assert.Single(QueryTrigger.Match(Index(otherKeyword), "iş abc"));
    }

    [Theory]
    [InlineData("jira")]
    [InlineData("jira ")]
    [InlineData("jira    ")]
    [InlineData("")]
    [InlineData("   ")]
    public void Match_WithoutQueryPart_IsEmpty(string text) =>
        Assert.Empty(QueryTrigger.Match(Index(Jira()), text));

    [Fact]
    public void Match_RequiresWholeFirstWord_NotAPrefixOrFuzzy()
    {
        Assert.Empty(QueryTrigger.Match(Index(Jira()), "jir ABC"));
        Assert.Empty(QueryTrigger.Match(Index(Jira()), "jiraa ABC"));
        Assert.Empty(QueryTrigger.Match(Index(Jira()), "x jira ABC"));
    }

    [Fact]
    public void Match_NonQueryNodes_AreIgnored()
    {
        var plain = new UrlNode { Id = "p", Name = "Jira", Target = "https://j" };
        var path = new PathNode { Id = "pa", Name = "Jira", Target = @"C:\{query}" };

        Assert.Empty(QueryTrigger.Match(Index(plain, path), "jira ABC"));
    }

    [Fact]
    public void Match_MultiWordName_NeverMatchesBecauseTheTriggerIsOneWord()
    {
        var node = new UrlNode { Id = "n", Name = "My Jira", Target = "https://j/{query}" };

        Assert.Empty(QueryTrigger.Match(Index(node), "my jira ABC"));
    }

    [Fact]
    public void Match_SeveralNodes_ComeInTreeOrder_AcrossFolders()
    {
        var inner = new UrlNode { Id = "b", Name = "Docs", Target = "https://b/{query}", Keywords = { "g" } };
        var folder = new FolderNode { Id = "f", Name = "Web", Children = { inner } };
        var first = new UrlNode { Id = "a", Name = "Google", Target = "https://a/{query}", Keywords = { "g" } };

        var matches = QueryTrigger.Match(Index(folder, first), "g hello");

        Assert.Equal(new[] { "b", "a" }, matches.Select(m => m.Node.Id).ToArray());
        Assert.Equal("Web", matches[0].Breadcrumb);
        Assert.Equal(2, matches[0].ParentChain.Count);
    }

    [Fact]
    public void Match_CommandAndAppNodes_Work()
    {
        var command = new CommandNode { Id = "c", Name = "Say", Command = "echo {query}" };
        var app = new AppNode { Id = "a", Name = "Find", Target = "f.exe", Arguments = "{query}" };

        Assert.Single(QueryTrigger.Match(Index(command, app), "say hi"));
        Assert.Single(QueryTrigger.Match(Index(command, app), "find hi"));
    }
}
