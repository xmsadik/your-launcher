using System.Security.Cryptography;
using System.Text;
using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.StartMenu;

/// <summary>What <see cref="StartMenuImport.Apply"/> did, for the status line and for navigating to the folder.</summary>
/// <param name="Folder">The "Start menu" folder (new or existing).</param>
/// <param name="Created">True when the folder did not exist before.</param>
/// <param name="Added">Nodes created.</param>
/// <param name="Updated">Existing imported nodes whose target was refreshed.</param>
/// <param name="Removed">Previously imported nodes dropped because their app was seen and unchecked.</param>
public readonly record struct StartMenuImportResult(FolderNode Folder, bool Created, int Added, int Updated, int Removed);

/// <summary>
/// Applies the user's Start-menu selection to the live tree (one-time, user-picked; no ongoing sync). Same idea
/// as <see cref="Bookmarks.BookmarkImport"/>: a deterministic folder id lets a re-import find "its" folder
/// wherever the user has moved or renamed it. Unlike bookmarks the folder is updated in place instead of
/// replaced wholesale: every imported node has a deterministic id derived from its source key, so a re-import
/// keeps what the user changed on those nodes (name, icon, keywords, arguments, usage history), adds newly
/// selected apps, and removes only nodes it imported itself whose app was seen in this scan and unchecked. An
/// imported node whose app was not seen (uninstalled, shortcut renamed, a source failed to load) is left alone. Nodes the user added to the folder by
/// hand have ordinary GUID ids and are never touched.
/// </summary>
public static class StartMenuImport
{
    /// <summary>Deterministic id of the import folder. Contains ':' like the bookmark folders.</summary>
    public const string FolderId = "startmenu:imported";

    /// <summary>Prefix of every imported node id; marks "created by this import". No ':' so the usage prune treats them as ordinary node ids.</summary>
    public const string ItemIdPrefix = "sm-";

    /// <summary>Stable node id for a source key: "sm-" + first 16 hex chars of SHA-256(key).</summary>
    public static string ItemId(string key) =>
        ItemIdPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();

    public static bool IsImportedId(string id) => id.StartsWith(ItemIdPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Ids of imported nodes currently anywhere in the tree - the picker pre-checks the matching apps on a
    /// re-import so the previous selection is the starting point.
    /// </summary>
    public static HashSet<string> ExistingImportedIds(FolderNode root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Collect(root);
        return ids;

        void Collect(FolderNode folder)
        {
            foreach (var child in folder.Children)
            {
                if (IsImportedId(child.Id))
                {
                    ids.Add(child.Id);
                }

                if (child is FolderNode sub)
                {
                    Collect(sub);
                }
            }
        }
    }

    /// <param name="root">The tree root; a new import folder is appended here.</param>
    /// <param name="folderName">Localized name for a newly created folder (an existing folder keeps whatever the user named it).</param>
    /// <param name="selected">The chosen apps with their already-built nodes (ids are assigned here).</param>
    /// <param name="seenKeys">Keys of every app the scan found, checked or not. Only imported nodes among these can be removed.</param>
    public static StartMenuImportResult Apply(
        FolderNode root, string folderName, IReadOnlyList<(string Key, Node Node)> selected, IEnumerable<string> seenKeys)
    {
        var created = false;
        if (TreeOps.FindById(root, FolderId) is not FolderNode folder)
        {
            // The id being taken by a non-folder is only possible via a hand-edited config.json: never
            // duplicate an id - this import then just gets a fresh, unlinked folder.
            folder = new FolderNode { Id = TreeOps.FindById(root, FolderId) is null ? FolderId : TreeOps.NewId(), Name = folderName };
            TreeOps.Add(root, folder);
            created = true;
        }

        int added = 0, updated = 0;
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, node) in selected)
        {
            var id = ItemId(key);
            if (!selectedIds.Add(id))
            {
                continue; // two entries with one key: first wins.
            }

            // Found anywhere in the tree: the user may have moved an imported node out of the folder. Refresh
            // it where it is instead of adding a second node with the same id.
            if (TreeOps.FindById(root, id) is { } existing)
            {
                if (RefreshLaunchFields(existing, node))
                {
                    updated++;
                }

                continue;
            }

            node.Id = id;
            TreeOps.Add(folder, node);
            added++;
        }

        var seenIds = new HashSet<string>(seenKeys.Select(ItemId), StringComparer.Ordinal);
        var removed = folder.Children.RemoveAll(c => IsImportedId(c.Id) && seenIds.Contains(c.Id) && !selectedIds.Contains(c.Id));
        return new StartMenuImportResult(folder, created, added, updated, removed);
    }

    /// <summary>
    /// Copies only the target from the freshly built node to the existing one, and only when both are the same
    /// kind; arguments and working directory stay because the user may have edited them (e.g. added {query}).
    /// Returns true when something changed.
    /// </summary>
    private static bool RefreshLaunchFields(Node existing, Node fresh)
    {
        switch (existing, fresh)
        {
            case (AppNode oldApp, AppNode newApp):
                var changed = oldApp.Target != newApp.Target;
                oldApp.Target = newApp.Target;
                return changed;

            case (PathNode oldPath, PathNode newPath):
                var pathChanged = oldPath.Target != newPath.Target;
                oldPath.Target = newPath.Target;
                return pathChanged;

            default:
                return false;
        }
    }
}
