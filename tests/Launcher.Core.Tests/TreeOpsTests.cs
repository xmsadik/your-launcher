using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests;

public class TreeOpsTests
{
    private static FolderNode Folder(string id, string name, params Node[] children) => new()
    {
        Id = id,
        Name = name,
        Children = children.ToList(),
    };

    private static AppNode App(string id, string name) => new() { Id = id, Name = name, Target = @"C:\a.exe" };

    private static UrlNode Url(string id, string name) => new() { Id = id, Name = name, Target = "https://x" };

    // ---- FindParent ----

    [Fact]
    public void FindParent_RootLevelNode_ReturnsRoot()
    {
        var leaf = App("a", "A");
        var root = Folder("root", "Root", leaf);

        Assert.Same(root, TreeOps.FindParent(root, leaf));
    }

    [Fact]
    public void FindParent_NestedNode_ReturnsImmediateParent()
    {
        var leaf = App("a", "A");
        var inner = Folder("inner", "Inner", leaf);
        var root = Folder("root", "Root", inner);

        Assert.Same(inner, TreeOps.FindParent(root, leaf));
    }

    [Fact]
    public void FindParent_NodeNotInTree_ReturnsNull()
    {
        var root = Folder("root", "Root");
        var stray = App("a", "A");

        Assert.Null(TreeOps.FindParent(root, stray));
    }

    [Fact]
    public void FindParent_RootItself_ReturnsNull()
    {
        var root = Folder("root", "Root");

        Assert.Null(TreeOps.FindParent(root, root));
    }

    [Fact]
    public void FindParentById_MatchesByIdRatherThanReference()
    {
        var leaf = App("a", "A");
        var root = Folder("root", "Root", leaf);

        Assert.Same(root, TreeOps.FindParentById(root, "a"));
        Assert.Null(TreeOps.FindParentById(root, "missing"));
    }

    // ---- Add / Remove ----

    [Fact]
    public void Add_WithoutIndex_AppendsAtEnd()
    {
        var root = Folder("root", "Root", App("a", "A"));
        var b = App("b", "B");

        TreeOps.Add(root, b);

        Assert.Equal(new[] { "a", "b" }, root.Children.Select(n => n.Id));
    }

    [Fact]
    public void Add_WithIndex_InsertsAtThatPosition()
    {
        var root = Folder("root", "Root", App("a", "A"), App("c", "C"));
        var b = App("b", "B");

        TreeOps.Add(root, b, 1);

        Assert.Equal(new[] { "a", "b", "c" }, root.Children.Select(n => n.Id));
    }

    [Fact]
    public void Remove_RootLevelLeaf_RemovesIt()
    {
        var leaf = App("a", "A");
        var root = Folder("root", "Root", leaf);

        Assert.True(TreeOps.Remove(root, leaf));
        Assert.Empty(root.Children);
    }

    [Fact]
    public void Remove_Folder_RemovesWholeSubtreeWithIt()
    {
        var grandchild = App("gc", "GC");
        var child = Folder("child", "Child", grandchild);
        var root = Folder("root", "Root", child);

        Assert.True(TreeOps.Remove(root, child));
        Assert.Empty(root.Children);
        // The subtree is gone with its parent - nothing left referencing grandchild under root.
        Assert.Null(TreeOps.FindParent(root, grandchild));
    }

    [Fact]
    public void Remove_NodeNotInTree_ReturnsFalse()
    {
        var root = Folder("root", "Root");
        Assert.False(TreeOps.Remove(root, App("a", "A")));
    }

    // ---- MoveUp / MoveDown (display-group aware) ----

    [Fact]
    public void MoveUp_FirstInGroup_ReturnsFalse()
    {
        var a = App("a", "A");
        var root = Folder("root", "Root", a, App("b", "B"));

        Assert.False(TreeOps.MoveUp(root, a));
    }

    [Fact]
    public void MoveDown_LastInGroup_ReturnsFalse()
    {
        var b = App("b", "B");
        var root = Folder("root", "Root", App("a", "A"), b);

        Assert.False(TreeOps.MoveDown(root, b));
    }

    [Fact]
    public void MoveDown_MiddleOfGroup_SwapsWithNextInStoredOrder()
    {
        var a = App("a", "A");
        var b = App("b", "B");
        var c = App("c", "C");
        var root = Folder("root", "Root", a, b, c);

        Assert.True(TreeOps.MoveDown(root, a));

        Assert.Equal(new[] { "b", "a", "c" }, root.Children.Select(n => n.Id));
    }

    [Fact]
    public void MoveUp_MixedFoldersAndItems_OnlySwapsWithinOwnGroup()
    {
        // Stored order: folder F1, folder F2, item I1, item I2. Folders and items are separate groups
        // even though the folder-then-others display order interleaves nothing here; the important case
        // is verified below where item I1 must not "jump" past folders when moved up.
        var f1 = Folder("f1", "F1");
        var f2 = Folder("f2", "F2");
        var i1 = App("i1", "I1");
        var i2 = App("i2", "I2");
        var root = Folder("root", "Root", f1, f2, i1, i2);

        // I1 is first within the item group -> MoveUp must fail, not swap with the last folder.
        Assert.False(TreeOps.MoveUp(root, i1));
        Assert.Equal(new[] { "f1", "f2", "i1", "i2" }, root.Children.Select(n => n.Id));

        // I2 is second within the item group -> MoveUp swaps with I1 only.
        Assert.True(TreeOps.MoveUp(root, i2));
        Assert.Equal(new[] { "f1", "f2", "i2", "i1" }, root.Children.Select(n => n.Id));

        // F2 moved up swaps only with F1, not with any item.
        Assert.True(TreeOps.MoveUp(root, f2));
        Assert.Equal(new[] { "f2", "f1", "i2", "i1" }, root.Children.Select(n => n.Id));
    }

    [Fact]
    public void MoveUp_SingleNodeInGroup_ReturnsFalse()
    {
        var onlyFolder = Folder("f1", "F1");
        var root = Folder("root", "Root", onlyFolder, App("a", "A"));

        Assert.False(TreeOps.MoveUp(root, onlyFolder));
        Assert.False(TreeOps.MoveDown(root, onlyFolder));
    }

    [Fact]
    public void MoveUpDown_NodeWithoutParent_ReturnsFalse()
    {
        var root = Folder("root", "Root");
        var stray = App("a", "A");

        Assert.False(TreeOps.MoveUp(root, stray));
        Assert.False(TreeOps.MoveDown(root, stray));
    }

    // ---- MoveTo (cut/paste) ----

    [Fact]
    public void MoveTo_ToDifferentFolder_AppendsAtEndAndRemovesFromOldParent()
    {
        var node = App("a", "A");
        var source = Folder("src", "Src", node);
        var target = Folder("dst", "Dst", App("existing", "Existing"));
        var root = Folder("root", "Root", source, target);

        var result = TreeOps.MoveTo(root, node, target);

        Assert.Equal(MoveOutcome.Moved, result);
        Assert.Empty(source.Children);
        Assert.Equal(new[] { "existing", "a" }, target.Children.Select(n => n.Id));
    }

    [Fact]
    public void MoveTo_AlreadyInTargetFolder_IsNoOp()
    {
        var node = App("a", "A");
        var target = Folder("dst", "Dst", node);
        var root = Folder("root", "Root", target);

        var result = TreeOps.MoveTo(root, node, target);

        Assert.Equal(MoveOutcome.NoOp, result);
        Assert.Equal(new[] { "a" }, target.Children.Select(n => n.Id));
    }

    [Fact]
    public void MoveTo_TargetIsNodeItself_IsRejected()
    {
        var folder = Folder("f", "F");
        var root = Folder("root", "Root", folder);

        var result = TreeOps.MoveTo(root, folder, folder);

        Assert.Equal(MoveOutcome.Rejected, result);
    }

    [Fact]
    public void MoveTo_TargetInsideNodesOwnSubtree_IsRejected()
    {
        var grandchild = Folder("gc", "GC");
        var child = Folder("child", "Child", grandchild);
        var root = Folder("root", "Root", child);

        var result = TreeOps.MoveTo(root, child, grandchild);

        Assert.Equal(MoveOutcome.Rejected, result);
        // Nothing was mutated.
        Assert.Same(child, root.Children[0]);
        Assert.Same(grandchild, child.Children[0]);
    }

    [Fact]
    public void MoveTo_LeafNodeIntoUnrelatedFolder_Succeeds()
    {
        var leaf = App("a", "A");
        var root = Folder("root", "Root", leaf);
        var target = Folder("dst", "Dst");
        root.Children.Add(target);

        var result = TreeOps.MoveTo(root, leaf, target);

        Assert.Equal(MoveOutcome.Moved, result);
        Assert.Contains(leaf, target.Children);
    }

    // ---- Duplicate ----

    [Fact]
    public void Duplicate_Leaf_InsertsRightAfterOriginalWithCopySuffixAndNewId()
    {
        var original = App("a", "A");
        var root = Folder("root", "Root", original, App("b", "B"));

        var clone = TreeOps.Duplicate(root, original);

        Assert.NotNull(clone);
        Assert.Equal("A (copy)", clone!.Name);
        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equal(new[] { "a", clone.Id, "b" }, root.Children.Select(n => n.Id));
    }

    [Fact]
    public void Duplicate_Folder_DeepCopiesDescendantsWithFreshIdsAndNoSharedState()
    {
        var grandchild = App("gc", "GC");
        grandchild.Keywords.Add("kw");
        var child = Folder("child", "Child", grandchild);
        var original = Folder("orig", "Orig", child);
        var root = Folder("root", "Root", original);

        var clone = (FolderNode)TreeOps.Duplicate(root, original)!;

        Assert.Equal("Orig (copy)", clone.Name);
        Assert.NotEqual(original.Id, clone.Id);

        var clonedChild = (FolderNode)clone.Children[0];
        var clonedGrandchild = (AppNode)clonedChild.Children[0];

        Assert.NotEqual(child.Id, clonedChild.Id);
        Assert.NotEqual(grandchild.Id, clonedGrandchild.Id);

        // Deep independence: mutating the clone must not affect the original.
        clonedGrandchild.Keywords.Add("extra");
        clonedGrandchild.Target = "changed";
        Assert.DoesNotContain("extra", grandchild.Keywords);
        Assert.Equal(@"C:\a.exe", grandchild.Target);
        Assert.NotSame(grandchild.Keywords, clonedGrandchild.Keywords);
    }

    [Fact]
    public void Duplicate_IdsAreUniqueAcrossWholeTreeAfterMultipleDuplications()
    {
        var leaf = App("a", "A");
        var root = Folder("root", "Root", leaf);

        var clone1 = TreeOps.Duplicate(root, leaf)!;
        var clone2 = TreeOps.Duplicate(root, leaf)!;

        var allIds = CollectIds(root).ToList();
        Assert.Equal(allIds.Count, allIds.Distinct().Count());
        Assert.Equal(3, allIds.Count); // original + 2 copies
        Assert.NotEqual(clone1.Id, clone2.Id);
    }

    [Fact]
    public void Duplicate_NodeNotInTree_ReturnsNull()
    {
        var root = Folder("root", "Root");
        Assert.Null(TreeOps.Duplicate(root, App("a", "A")));
    }

    [Fact]
    public void Duplicate_PreservesIconAndDescriptionButAsIndependentCopy()
    {
        var original = new UrlNode
        {
            Id = "u",
            Name = "Site",
            Target = "https://example.com",
            Description = "desc",
            Icon = new IconSpec { Kind = IconKind.Emoji, Value = "🌍" },
        };
        var root = Folder("root", "Root", original);

        var clone = (UrlNode)TreeOps.Duplicate(root, original)!;

        Assert.Equal("desc", clone.Description);
        Assert.NotNull(clone.Icon);
        Assert.Equal("🌍", clone.Icon!.Value);
        Assert.NotSame(original.Icon, clone.Icon);
    }

    private static IEnumerable<string> CollectIds(FolderNode folder)
    {
        foreach (var child in folder.Children)
        {
            yield return child.Id;
            if (child is FolderNode childFolder)
            {
                foreach (var id in CollectIds(childFolder))
                {
                    yield return id;
                }
            }
        }
    }

    // ---- NewId ----

    [Fact]
    public void NewId_ReturnsUniqueGuidStrings()
    {
        var id1 = TreeOps.NewId();
        var id2 = TreeOps.NewId();

        Assert.NotEqual(id1, id2);
        Assert.True(Guid.TryParse(id1, out _));
    }
}
