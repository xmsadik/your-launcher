using System.Text.Json;
using YourLauncher.Core.Json;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Config;

/// <summary>Result of <see cref="TreeOps.MoveTo"/> (cut/paste, spec §6.3).</summary>
public enum MoveOutcome
{
    Moved,

    /// <summary>node is already a direct child of targetFolder; nothing to do.</summary>
    NoOp,

    /// <summary>targetFolder is node itself, or inside node's own subtree.</summary>
    Rejected,
}

/// <summary>
/// Pure tree-editing operations on the <see cref="FolderNode"/> tree rooted at config.Root (spec §6.3,
/// Phase 3). All operations mutate the tree in place (no immutable-tree copying) and identify nodes by
/// reference unless an "...ById" overload is used. Callers (the App layer) are responsible for saving
/// the config and refreshing the search index/UI after any successful mutation.
///
/// "Display group": the panel always lists folders before non-folders within a single folder's children
/// (see MainViewModel.RefreshItems), but the two groups keep their own separate stored order in
/// <see cref="FolderNode.Children"/>. <see cref="MoveUp"/>/<see cref="MoveDown"/> therefore operate
/// within a node's own group (folder vs. non-folder) so the *visible* result is always a one-step move,
/// never a move that appears to do nothing (because the other group's node was skipped) or one that
/// jumps across the folder/non-folder boundary.
/// </summary>
public static class TreeOps
{
    /// <summary>New unique node id. Same format as Phase 1 (Guid.NewGuid().ToString(), i.e. "D" format with dashes).</summary>
    public static string NewId() => Guid.NewGuid().ToString();

    /// <summary>Finds the direct parent of <paramref name="node"/> within the tree rooted at <paramref name="root"/>, by reference. Null if not found (including when node IS root).</summary>
    public static FolderNode? FindParent(FolderNode root, Node node)
    {
        foreach (var child in root.Children)
        {
            if (ReferenceEquals(child, node))
            {
                return root;
            }

            if (child is FolderNode childFolder)
            {
                var found = FindParent(childFolder, node);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>Same as <see cref="FindParent(FolderNode, Node)"/> but matched by <see cref="Node.Id"/>.</summary>
    public static FolderNode? FindParentById(FolderNode root, string id)
    {
        foreach (var child in root.Children)
        {
            if (child.Id == id)
            {
                return root;
            }

            if (child is FolderNode childFolder)
            {
                var found = FindParentById(childFolder, id);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>Finds a node anywhere in the tree by id (root itself is never returned - callers already hold the root).</summary>
    public static Node? FindById(FolderNode root, string id)
    {
        foreach (var child in root.Children)
        {
            if (child.Id == id)
            {
                return child;
            }

            if (child is FolderNode childFolder)
            {
                var found = FindById(childFolder, id);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>Adds <paramref name="node"/> to <paramref name="folder"/>, appended at the end by default, or at <paramref name="index"/> (clamped) when given.</summary>
    public static void Add(FolderNode folder, Node node, int? index = null)
    {
        if (index is { } i)
        {
            folder.Children.Insert(Math.Clamp(i, 0, folder.Children.Count), node);
        }
        else
        {
            folder.Children.Add(node);
        }
    }

    /// <summary>Removes node (and, if it's a folder, its whole subtree with it) from wherever it lives under root. False if not found.</summary>
    public static bool Remove(FolderNode root, Node node)
    {
        var parent = FindParent(root, node);
        return parent is not null && parent.Children.Remove(node);
    }

    /// <summary>Moves node one step earlier within its display group (folder vs. non-folder). False at the group's start, or if node has no parent.</summary>
    public static bool MoveUp(FolderNode root, Node node) => MoveWithinGroup(root, node, -1);

    /// <summary>Moves node one step later within its display group (folder vs. non-folder). False at the group's end, or if node has no parent.</summary>
    public static bool MoveDown(FolderNode root, Node node) => MoveWithinGroup(root, node, 1);

    private static bool MoveWithinGroup(FolderNode root, Node node, int direction)
    {
        var parent = FindParent(root, node);
        if (parent is null)
        {
            return false;
        }

        var siblings = parent.Children;
        var isFolder = node is FolderNode;
        var group = siblings.Where(n => (n is FolderNode) == isFolder).ToList();

        var groupIndex = group.IndexOf(node);
        var targetGroupIndex = groupIndex + direction;
        if (targetGroupIndex < 0 || targetGroupIndex >= group.Count)
        {
            return false;
        }

        var swapWith = group[targetGroupIndex];
        var storedIndexOfNode = siblings.IndexOf(node);
        var storedIndexOfSwapWith = siblings.IndexOf(swapWith);
        (siblings[storedIndexOfNode], siblings[storedIndexOfSwapWith]) = (siblings[storedIndexOfSwapWith], siblings[storedIndexOfNode]);
        return true;
    }

    /// <summary>
    /// Cut/paste (spec §6.3): moves node into targetFolder, appended at the end. Rejects moving a
    /// folder into itself or into its own subtree; no-ops if node is already a direct child of
    /// targetFolder.
    /// </summary>
    public static MoveOutcome MoveTo(FolderNode root, Node node, FolderNode targetFolder)
    {
        if (ReferenceEquals(node, targetFolder))
        {
            return MoveOutcome.Rejected;
        }

        if (node is FolderNode nodeAsFolder && IsSameOrDescendant(nodeAsFolder, targetFolder))
        {
            return MoveOutcome.Rejected;
        }

        var parent = FindParent(root, node);
        if (parent is null)
        {
            return MoveOutcome.Rejected;
        }

        if (ReferenceEquals(parent, targetFolder))
        {
            return MoveOutcome.NoOp;
        }

        parent.Children.Remove(node);
        targetFolder.Children.Add(node);
        return MoveOutcome.Moved;
    }

    private static bool IsSameOrDescendant(FolderNode ancestor, FolderNode candidate)
    {
        if (ReferenceEquals(ancestor, candidate))
        {
            return true;
        }

        foreach (var child in ancestor.Children)
        {
            if (child is FolderNode childFolder && IsSameOrDescendant(childFolder, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Deep-copies node (fresh ids for it and every descendant; no shared mutable state with the
    /// original - see <see cref="DeepClone"/>), names it "&lt;original&gt; (copy)", and inserts it right
    /// after the original in its parent. Returns the new node, or null if node has no parent under root
    /// (i.e. it's not actually in this tree).
    /// </summary>
    public static Node? Duplicate(FolderNode root, Node node)
    {
        var parent = FindParent(root, node);
        if (parent is null)
        {
            return null;
        }

        var clone = DeepClone(node);
        clone.Name = node.Name + " (copy)";
        AssignNewIds(clone);

        var index = parent.Children.IndexOf(node);
        parent.Children.Insert(index + 1, clone);
        return clone;
    }

    /// <summary>Deep-copies a node (and, for a folder, its whole subtree) via a JSON round trip through the same source-generated context config.json uses, so the clone shares no mutable list/object with the original.</summary>
    private static Node DeepClone(Node node)
    {
        var json = JsonSerializer.Serialize(node, LauncherJsonContext.Default.Node);
        return JsonSerializer.Deserialize(json, LauncherJsonContext.Default.Node)
            ?? throw new InvalidOperationException("Deep clone round-trip produced a null node.");
    }

    private static void AssignNewIds(Node node)
    {
        node.Id = NewId();
        if (node is FolderNode folder)
        {
            foreach (var child in folder.Children)
            {
                AssignNewIds(child);
            }
        }
    }
}
