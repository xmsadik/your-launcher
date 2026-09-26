using YourLauncher.Core.Model;
using YourLauncher.Core.Search;

namespace YourLauncher.Core.Tests.Search;

public class SearchEngineTests
{
    private static FolderNode Root(params Node[] children) =>
        new() { Id = "root", Name = "Root", Children = children.ToList() };

    private static FlatIndex BuildIndex(FolderNode root) => FlatIndex.Build(root);

    [Fact]
    public void Search_Sifre_FindsSifreYoneticisi()
    {
        var root = Root(new AppNode { Id = "1", Name = "Şifre Yöneticisi", Target = "x.exe" });
        var results = new SearchEngine().Search(BuildIndex(root), "sifre");

        Assert.Single(results);
        Assert.Equal("Şifre Yöneticisi", results[0].Node.Name);
    }

    [Fact]
    public void Search_IZMIR_FindsIzmir()
    {
        var root = Root(new PathNode { Id = "1", Name = "İzmir", Target = "C:\\İzmir" });
        var results = new SearchEngine().Search(BuildIndex(root), "IZMIR");

        Assert.Single(results);
        Assert.Equal("İzmir", results[0].Node.Name);
    }

    [Fact]
    public void Search_Rapor_FindsHaftalikRapor_WithPositionsOverRaporChars()
    {
        var root = Root(new PathNode { Id = "1", Name = "Haftalık Rapor", Target = "x.xlsx" });
        var results = new SearchEngine().Search(BuildIndex(root), "rapor");

        Assert.Single(results);
        Assert.Equal(MatchedField.Name, results[0].MatchedField);
        Assert.Equal(new[] { 9, 10, 11, 12, 13 }, results[0].NamePositions);
    }

    [Fact]
    public void Search_Vsc_RanksVsCodeNotesAboveVisualStudioCodeAboveVisualBasic()
    {
        var root = Root(
            new AppNode { Id = "basic", Name = "Visual Basic", Target = "vb.exe" },
            new AppNode { Id = "vscode", Name = "Visual Studio Code", Target = "code.exe" },
            new PathNode { Id = "notes", Name = "vscode-notes.txt", Target = "notes.txt" });

        var results = new SearchEngine().Search(BuildIndex(root), "vsc");

        Assert.Equal(3, results.Count);
        Assert.Equal("notes", results[0].Node.Id);
        Assert.Equal("vscode", results[1].Node.Id);
        Assert.Equal("basic", results[2].Node.Id);
    }

    [Fact]
    public void Search_NameMatchBeatsSameTierKeywordMatch()
    {
        var root = Root(
            new AppNode { Id = "by-keyword", Name = "Bar", Target = "bar.exe", Keywords = new List<string> { "foo" } },
            new AppNode { Id = "by-name", Name = "Foo", Target = "foo.exe" });

        var results = new SearchEngine().Search(BuildIndex(root), "foo");

        Assert.Equal(2, results.Count);
        Assert.Equal("by-name", results[0].Node.Id);
        Assert.Equal(MatchedField.Name, results[0].MatchedField);
        Assert.Equal("by-keyword", results[1].Node.Id);
        Assert.Equal(MatchedField.Keywords, results[1].MatchedField);
    }

    [Fact]
    public void Search_KeywordOnlyMatch_HasEmptyNamePositions()
    {
        var root = Root(new AppNode { Id = "1", Name = "Bar", Target = "bar.exe", Keywords = new List<string> { "foo" } });
        var results = new SearchEngine().Search(BuildIndex(root), "foo");

        Assert.Single(results);
        Assert.Equal(MatchedField.Keywords, results[0].MatchedField);
        Assert.Empty(results[0].NamePositions);
    }

    [Fact]
    public void Search_TieBreak_UsageScoreBeatsDepthBeatsAlphabetical()
    {
        var deepButUsed = new AppNode { Id = "deep", Name = "Test", Target = "a.exe" };
        var shallow = new AppNode { Id = "shallow", Name = "Test", Target = "b.exe" };
        var deepFolder = new FolderNode { Id = "folder", Name = "Sub", Children = new List<Node> { deepButUsed } };
        var root = Root(shallow, deepFolder);

        var index = BuildIndex(root);

        // No usage info: shallower node wins (depth 0 < depth 1) even though it's listed second in children.
        var noUsage = new SearchEngine().Search(index, "test");
        Assert.Equal("shallow", noUsage[0].Node.Id);
        Assert.Equal("deep", noUsage[1].Node.Id);

        // With usage favoring the deep node, usage beats depth.
        var withUsage = new SearchEngine(id => id == "deep" ? 100.0 : 0.0).Search(index, "test");
        Assert.Equal("deep", withUsage[0].Node.Id);
        Assert.Equal("shallow", withUsage[1].Node.Id);
    }

    [Fact]
    public void Search_TieBreak_AlphabeticalUsesTurkishCollation()
    {
        var root = Root(
            new AppNode { Id = "dosya", Name = "Dosya", Target = "d.exe", Keywords = new List<string> { "x" } },
            new AppNode { Id = "canta", Name = "Çanta", Target = "c.exe", Keywords = new List<string> { "x" } },
            new AppNode { Id = "can", Name = "Can", Target = "cc.exe", Keywords = new List<string> { "x" } });

        var results = new SearchEngine().Search(BuildIndex(root), "x");

        // Turkish alphabet order: ... C, Ç, D ... so "Can" < "Çanta" < "Dosya".
        Assert.Equal(new[] { "can", "canta", "dosya" }, results.Select(r => r.Node.Id));
    }

    [Fact]
    public void Search_MultiToken_MatchesSapDevSystem()
    {
        var root = Root(
            new FolderNode { Id = "1", Name = "SAP Dev System" },
            new AppNode { Id = "2", Name = "Unrelated", Target = "x.exe" });

        var results = new SearchEngine().Search(BuildIndex(root), "sap dev");

        Assert.Single(results);
        Assert.Equal("1", results[0].Node.Id);
    }

    [Fact]
    public void Search_ThirdLevelNode_FoundFromRoot_WithTwoLevelBreadcrumb()
    {
        var target = new AppNode { Id = "target", Name = "Deep Target", Target = "x.exe" };
        var b = new FolderNode { Id = "b", Name = "B", Children = new List<Node> { target } };
        var a = new FolderNode { Id = "a", Name = "A", Children = new List<Node> { b } };
        var root = Root(a);

        var results = new SearchEngine().Search(BuildIndex(root), "deep target");

        Assert.Single(results);
        Assert.Equal("target", results[0].Node.Id);
        Assert.Equal("A › B", results[0].Breadcrumb);
        Assert.Equal(2, results[0].Depth);
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsNoResults()
    {
        var root = Root(new AppNode { Id = "1", Name = "Anything", Target = "x.exe" });
        Assert.Empty(new SearchEngine().Search(BuildIndex(root), "   "));
    }
}
