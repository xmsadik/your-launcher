using YourLauncher.Core.Model;

namespace YourLauncher.Core.Config;

/// <summary>
/// Pure merge-side logic for Import (spec §10/§11, revised §10 item 9). "Replace" itself needs no helper
/// beyond assigning <see cref="LauncherConfig.Root"/> - the imported tree already came from a fresh
/// deserialize, so it's never aliased with the currently-loaded tree; only "Merge" needs real logic here
/// (id collision renaming, including nested collisions against ids the merge itself just introduced).
/// </summary>
public static class ConfigImport
{
    /// <summary>
    /// Appends every child of <paramref name="importedRoot"/> to <paramref name="currentRoot"/> in order.
    /// Any imported id (anywhere in the imported subtree) that collides with an id already present in
    /// either tree gets a fresh one - checked and reserved incrementally, so two colliding imported ids
    /// never end up renamed to the same new id. No folder-name merging: an imported "Dev" folder becomes a
    /// second sibling "Dev", it does not fold into an existing one (documented in README). Returns the
    /// number of nodes added (imported root's whole subtree, root itself excluded).
    /// </summary>
    public static int Merge(FolderNode currentRoot, FolderNode importedRoot)
    {
        var reservedIds = new HashSet<string>(CollectIds(currentRoot));

        foreach (var child in importedRoot.Children)
        {
            RenameCollisions(child, reservedIds);
            currentRoot.Children.Add(child);
        }

        return CountDescendants(importedRoot);
    }

    /// <summary>Total node count under (not including) <paramref name="folder"/> - used for the "Imported N items" status line for both Merge and Replace.</summary>
    public static int CountDescendants(FolderNode folder) => folder.Children.Sum(CountNodes);

    private static int CountNodes(Node node) => 1 + (node is FolderNode folder ? CountDescendants(folder) : 0);

    private static void RenameCollisions(Node node, HashSet<string> reservedIds)
    {
        if (!reservedIds.Add(node.Id))
        {
            node.Id = TreeOps.NewId();
            reservedIds.Add(node.Id);
        }

        if (node is FolderNode folder)
        {
            foreach (var child in folder.Children)
            {
                RenameCollisions(child, reservedIds);
            }
        }
    }

    /// <summary>Every node id in the subtree rooted at <paramref name="node"/> (node itself included) - also used by the App layer to prune usage.json against the live tree.</summary>
    public static IEnumerable<string> CollectIds(Node node)
    {
        yield return node.Id;

        if (node is FolderNode folder)
        {
            foreach (var child in folder.Children)
            {
                foreach (var id in CollectIds(child))
                {
                    yield return id;
                }
            }
        }
    }
}
