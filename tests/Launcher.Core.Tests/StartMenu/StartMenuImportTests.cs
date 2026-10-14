using YourLauncher.Core.Config;
using YourLauncher.Core.Model;
using YourLauncher.Core.StartMenu;

namespace YourLauncher.Core.Tests.StartMenu;

public class StartMenuImportTests
{
    private static FolderNode Root(params Node[] children) => new() { Id = "root", Name = "Root", Children = children.ToList() };

    private static (string Key, Node Node) Entry(string key, string name, string target = @"C:\app.exe") =>
        (key, new AppNode { Name = name, Target = target });

    private static string[] Keys(params string[] keys) => keys;

    private static FolderNode StartFolder(FolderNode root) => (FolderNode)TreeOps.FindById(root, StartMenuImport.FolderId)!;

    [Fact]
    public void Apply_FirstImport_CreatesFolderAtRootWithStableIds()
    {
        var root = Root();

        var result = StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A"), Entry("pkg:b!app", "B")], Keys("lnk:a.lnk", "pkg:b!app"));

        Assert.True(result.Created);
        Assert.Equal((2, 0, 0), (result.Added, result.Updated, result.Removed));
        Assert.Same(root.Children.Single(), result.Folder);
        Assert.Equal("Start menu", result.Folder.Name);
        Assert.Equal(StartMenuImport.FolderId, result.Folder.Id);
        Assert.Equal(
            new[] { StartMenuImport.ItemId("lnk:a.lnk"), StartMenuImport.ItemId("pkg:b!app") },
            result.Folder.Children.Select(c => c.Id).ToArray());
        Assert.All(result.Folder.Children, c => Assert.DoesNotContain(':', c.Id));
    }

    [Fact]
    public void ItemId_IsDeterministicAndDistinct()
    {
        Assert.Equal(StartMenuImport.ItemId("lnk:a"), StartMenuImport.ItemId("lnk:a"));
        Assert.NotEqual(StartMenuImport.ItemId("lnk:a"), StartMenuImport.ItemId("lnk:b"));
    }

    [Fact]
    public void Apply_ReImport_UpdatesInPlaceWithoutDuplicating()
    {
        var root = Root();
        StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A", @"C:\old.exe")], Keys("lnk:a.lnk"));
        var first = StartFolder(root).Children.Single();
        first.Name = "My A";
        first.Keywords.Add("kw");

        var result = StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A", @"C:\new.exe")], Keys("lnk:a.lnk"));

        Assert.False(result.Created);
        Assert.Equal((0, 1, 0), (result.Added, result.Updated, result.Removed));
        var only = Assert.IsType<AppNode>(Assert.Single(StartFolder(root).Children));
        Assert.Same(first, only);
        Assert.Equal(@"C:\new.exe", only.Target);
        Assert.Equal("My A", only.Name);          // user edits survive
        Assert.Contains("kw", only.Keywords);
    }

    [Fact]
    public void Apply_ReImport_RemovesDeselectedImportedItems_KeepsManualOnes()
    {
        var root = Root();
        StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A"), Entry("lnk:b.lnk", "B")], Keys("lnk:a.lnk", "lnk:b.lnk"));
        var manual = new UrlNode { Name = "Mine", Target = "https://example.com" };
        StartFolder(root).Children.Add(manual);

        var result = StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A"), Entry("lnk:c.lnk", "C")], Keys("lnk:a.lnk", "lnk:b.lnk", "lnk:c.lnk"));

        Assert.Equal((1, 0, 1), (result.Added, result.Updated, result.Removed));
        var names = StartFolder(root).Children.Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "A", "Mine", "C" }, names);
    }

    [Fact]
    public void Apply_ReImport_KeepsImportedNodeWhoseAppWasNotSeen()
    {
        var root = Root();
        StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A"), Entry("pkg:b!app", "B")], Keys("lnk:a.lnk", "pkg:b!app"));

        // Second scan did not see B at all (uninstalled, or the AppsFolder source failed).
        var result = StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A")], Keys("lnk:a.lnk"));

        Assert.Equal(0, result.Removed);
        Assert.Equal(new[] { "A", "B" }, StartFolder(root).Children.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void Apply_ReImport_RemovesSeenButUncheckedNode()
    {
        var root = Root();
        StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A"), Entry("lnk:b.lnk", "B")], Keys("lnk:a.lnk", "lnk:b.lnk"));

        var result = StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A")], Keys("lnk:a.lnk", "lnk:b.lnk"));

        Assert.Equal(1, result.Removed);
        Assert.Equal("A", Assert.Single(StartFolder(root).Children).Name);
    }

    [Fact]
    public void Apply_ReImport_RefreshKeepsUserArgumentsAndWorkingDirectory()
    {
        var root = Root();
        StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A", @"C:\old.exe")], Keys("lnk:a.lnk"));
        var node = (AppNode)StartFolder(root).Children.Single();
        node.Arguments = "--q {query}";
        node.WorkingDirectory = @"C:\work";

        StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A", @"C:\new.exe")], Keys("lnk:a.lnk"));

        Assert.Equal(@"C:\new.exe", node.Target);
        Assert.Equal("--q {query}", node.Arguments);
        Assert.Equal(@"C:\work", node.WorkingDirectory);
    }

    [Fact]
    public void Apply_ReImport_FindsRenamedAndMovedFolder()
    {
        var sub = new FolderNode { Id = "sub", Name = "Sub" };
        var root = Root(sub);
        StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A")], Keys("lnk:a.lnk"));
        var folder = StartFolder(root);
        TreeOps.Remove(root, folder);
        TreeOps.Add(sub, folder);
        folder.Name = "Programs";

        var result = StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A"), Entry("lnk:b.lnk", "B")], Keys("lnk:a.lnk", "lnk:b.lnk"));

        Assert.False(result.Created);
        Assert.Same(folder, result.Folder);
        Assert.Equal("Programs", folder.Name);
        Assert.Equal(2, folder.Children.Count);
        Assert.Single(root.Children); // still only "Sub" at the root
    }

    [Fact]
    public void Apply_ImportedNodeMovedOutOfFolder_IsRefreshedWhereItLives_NotDuplicated()
    {
        var root = Root();
        StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A", @"C:\old.exe")], Keys("lnk:a.lnk"));
        var node = StartFolder(root).Children.Single();
        StartFolder(root).Children.Clear();
        root.Children.Add(node);

        var result = StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A", @"C:\new.exe")], Keys("lnk:a.lnk"));

        Assert.Equal((0, 1, 0), (result.Added, result.Updated, result.Removed));
        Assert.Empty(StartFolder(root).Children);
        Assert.Equal(@"C:\new.exe", ((AppNode)node).Target);
    }

    [Fact]
    public void Apply_EmptySelection_RemovesAllImportedButKeepsFolder()
    {
        var root = Root();
        StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A")], Keys("lnk:a.lnk"));

        var result = StartMenuImport.Apply(root, "Start menu", [], Keys("lnk:a.lnk"));

        Assert.Equal(1, result.Removed);
        Assert.Empty(StartFolder(root).Children);
    }

    [Fact]
    public void Apply_FolderIdTakenByNonFolder_GetsFreshFolder()
    {
        var root = Root(new UrlNode { Id = StartMenuImport.FolderId, Name = "odd", Target = "x" });

        var result = StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A")], Keys("lnk:a.lnk"));

        Assert.True(result.Created);
        Assert.NotEqual(StartMenuImport.FolderId, result.Folder.Id);
        Assert.Equal(2, root.Children.Count);
    }

    [Fact]
    public void ExistingImportedIds_FindsNodesAnywhere()
    {
        var root = Root();
        StartMenuImport.Apply(root, "Start menu", [Entry("lnk:a.lnk", "A")], Keys("lnk:a.lnk"));
        root.Children.Add(new UrlNode { Name = "manual", Target = "x" });

        var ids = StartMenuImport.ExistingImportedIds(root);

        Assert.Equal(new[] { StartMenuImport.ItemId("lnk:a.lnk") }, ids.ToArray());
    }

    [Fact]
    public void Shortcut_MapsLikeADroppedLnk_WithDisplayName()
    {
        var app = new StartMenuApp(StartMenuApp.ShortcutKey("Sub/Tool.lnk"), "Tool", StartMenuAppKind.Shortcut, @"C:\Menu\Sub\Tool.lnk");

        var node = app.ToNode(_ => new ShellLinkInfo(@"C:\Tool\tool.exe", "--x", @"C:\Tool", null, null));

        var appNode = Assert.IsType<AppNode>(node);
        Assert.Equal("Tool", appNode.Name);
        Assert.Equal(@"C:\Tool\tool.exe", appNode.Target);
        Assert.Equal("--x", appNode.Arguments);
        Assert.Equal(@"C:\Tool", appNode.WorkingDirectory);
    }

    [Fact]
    public void Shortcut_WithoutResolvableTarget_KeepsTheLnkAsTarget()
    {
        var app = new StartMenuApp("lnk:x", "X", StartMenuAppKind.Shortcut, @"C:\Menu\X.lnk");

        var node = app.ToNode(_ => default);

        Assert.Equal(@"C:\Menu\X.lnk", Assert.IsType<AppNode>(node).Target);
    }

    [Fact]
    public void Packaged_BecomesPathNodeOnAppsFolderUri()
    {
        var app = new StartMenuApp("pkg:x", "Calculator", StartMenuAppKind.Packaged, "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");

        var node = Assert.IsType<PathNode>(app.ToNode(_ => default));

        Assert.Equal("Calculator", node.Name);
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", node.Target);
    }

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)]
    [InlineData("Microsoft.Office.EXCEL.EXE.15", false)]
    [InlineData(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Foo\foo.exe", false)]
    [InlineData(@"C:\Foo\foo.exe", false)]
    [InlineData("Chrome", false)]
    public void IsPackagedAppId_SeparatesStoreAppsFromDesktopEntries(string parsingName, bool expected)
    {
        Assert.Equal(expected, StartMenuApp.IsPackagedAppId(parsingName));
    }

    [Fact]
    public void ShortcutKey_NormalizesCaseAndSeparators()
    {
        Assert.Equal("lnk:sub/tool.lnk", StartMenuApp.ShortcutKey(@"Sub\Tool.LNK"));
        Assert.Equal("pkg:a_b!app", StartMenuApp.PackagedKey("A_B!App"));
    }
}
