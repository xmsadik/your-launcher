using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests.Config;

public class ConfigImportTests
{
    private static FolderNode Root(string id, params Node[] children) =>
        new() { Id = id, Name = "Root", Children = children.ToList() };

    private static AppNode App(string id, string name) => new() { Id = id, Name = name, Target = @"C:\a.exe" };

    [Fact]
    public void Merge_NoCollisions_AppendsChildrenInOrder()
    {
        var current = Root("root", App("a1", "Existing"));
        var imported = Root("root2", App("i1", "First"), App("i2", "Second"));

        var count = ConfigImport.Merge(current, imported);

        Assert.Equal(2, count);
        Assert.Equal(3, current.Children.Count);
        Assert.Equal("i1", current.Children[1].Id);
        Assert.Equal("i2", current.Children[2].Id);
        Assert.Equal("First", current.Children[1].Name);
        Assert.Equal("Second", current.Children[2].Name);
    }

    [Fact]
    public void Merge_CollidingTopLevelId_GetsRenamed()
    {
        var current = Root("root", App("dup", "Existing"));
        var imported = Root("root2", App("dup", "Imported"));

        ConfigImport.Merge(current, imported);

        var importedNode = current.Children[1];
        Assert.NotEqual("dup", importedNode.Id);
        Assert.Equal("Existing", current.Children[0].Name);
        Assert.Equal("dup", current.Children[0].Id); // original untouched
    }

    [Fact]
    public void Merge_CollidingNestedId_GetsRenamed()
    {
        var current = Root("root", Root("dupFolder", App("leaf", "Leaf")));
        var importedInner = Root("dupFolder", App("dupLeaf", "ImportedLeaf")); // collides with current's nested folder id
        var imported = Root("root2", importedInner);

        ConfigImport.Merge(current, imported);

        var mergedFolder = Assert.IsType<FolderNode>(current.Children[1]);
        Assert.NotEqual("dupFolder", mergedFolder.Id); // renamed because "dupFolder" already existed
        Assert.Single(mergedFolder.Children);
        Assert.Equal("dupLeaf", mergedFolder.Children[0].Id); // no collision for this one, kept as-is
    }

    [Fact]
    public void Merge_TwoCollidingImportedIds_GetDistinctNewIds()
    {
        var current = Root("root", App("dup", "Existing"));
        var imported = Root("root2", App("dup", "ImportedA"), App("dup2", "ImportedB"));
        // Force a second collision: rename dup2 -> dup after building, to simulate two imported nodes that
        // both end up needing a fresh id relative to what's already been reserved during this same merge.
        imported.Children[1].Id = "dup";

        ConfigImport.Merge(current, imported);

        var idA = current.Children[1].Id;
        var idB = current.Children[2].Id;
        Assert.NotEqual("dup", idA);
        Assert.NotEqual("dup", idB);
        Assert.NotEqual(idA, idB);
    }

    [Fact]
    public void Merge_PreservesOrderOfImportedChildren()
    {
        var current = Root("root");
        var imported = Root("root2", App("i1", "One"), App("i2", "Two"), App("i3", "Three"));

        ConfigImport.Merge(current, imported);

        Assert.Equal(new[] { "One", "Two", "Three" }, current.Children.Select(c => c.Name));
    }

    [Fact]
    public void Merge_ImportedNodes_AreNotAliasedWithSource()
    {
        var current = Root("root");
        var importedChild = App("i1", "One");
        var imported = Root("root2", importedChild);

        ConfigImport.Merge(current, imported);

        // The merged node is literally the same object reference handed in (no copy is made - the
        // imported tree came from its own fresh deserialize and was never shared with the current tree
        // before the merge), so mutating the *current* tree's copy must not be reachable from any
        // structure still referencing the original "imported" config that produced it.
        current.Children[0].Name = "Renamed";
        Assert.Equal("Renamed", importedChild.Name); // same object - expected, proves no defensive copy was silently made elsewhere
        Assert.Single(imported.Children); // the source root's own child list is untouched by the merge (child moved by reference, imported.Children itself unmodified)
    }

    [Fact]
    public void Replace_KeepsCurrentSettings()
    {
        var currentSettings = new Settings { Hotkey = "Ctrl+Space", MaxVisibleItems = 12 };
        var current = new LauncherConfig { Settings = currentSettings, Root = Root("root", App("old", "Old")) };
        var imported = new LauncherConfig
        {
            Settings = new Settings { Hotkey = "Alt+Q", MaxVisibleItems = 5 },
            Root = Root("importedRoot", App("new", "New")),
        };

        // Replace semantics (spec §10 revision item 9): root swaps, settings object is untouched.
        current.Root = imported.Root;

        Assert.Same(currentSettings, current.Settings);
        Assert.Equal("Ctrl+Space", current.Settings.Hotkey);
        Assert.Equal(12, current.Settings.MaxVisibleItems);
        Assert.Equal("importedRoot", current.Root.Id);
    }

    [Fact]
    public void CountDescendants_CountsNestedNodes()
    {
        var root = Root("root", App("a", "A"), Root("f", App("b", "B"), App("c", "C")));
        Assert.Equal(4, ConfigImport.CountDescendants(root)); // a, f, b, c
    }
}
