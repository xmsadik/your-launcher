using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Bookmarks;

/// <summary>
/// Applies a parsed bookmark import to the live tree (spec §1.4). One-time import, no ongoing sync: a
/// re-import of the same source (same <paramref name="sourceKey"/>) finds its own previous import by a
/// deterministic id and replaces just its contents, wherever in the tree the user has since moved or
/// renamed that folder to; a source imported for the first time is appended as a new folder instead.
/// </summary>
public static class BookmarkImport
{
    /// <summary><c>Config/DropMapper.cs</c>-style pure decision: does <paramref name="root"/> already hold a previous import of this source, or is this the first time? Either way returns the resulting folder, whether it was a fresh append or an in-place replace, plus the number of bookmarks (<see cref="UrlNode"/>s) it now holds.</summary>
    public static (FolderNode Folder, bool Replaced, int Count) Apply(FolderNode root, FolderNode targetFolder, FolderNode imported, string sourceKey)
    {
        var id = ImportedFolderId(sourceKey);
        var count = CountUrlNodes(imported);

        if (TreeOps.FindById(root, id) is FolderNode existing)
        {
            // Keep the existing folder's Name/Icon/Keywords/Description/position - the user may have
            // renamed or moved it since the last import (spec §1.4) - only its Children are replaced.
            existing.Children = imported.Children;
            return (existing, true, count);
        }

        // The id is taken by a non-folder (only possible via a hand-edited config.json): never create a
        // duplicate id - this import just becomes a fresh, unlinked folder.
        imported.Id = TreeOps.FindById(root, id) is null ? id : TreeOps.NewId();
        TreeOps.Add(targetFolder, imported);
        return (imported, false, count);
    }

    /// <summary>Deterministic id a re-import of the same source finds its previous import by (spec §1.4).</summary>
    public static string ImportedFolderId(string sourceKey) => "bookmarks:" + sourceKey;

    /// <summary>Source key for an HTML-file import (spec §1.4): <c>html/&lt;file name lowercased&gt;</c>.</summary>
    public static string HtmlSourceKey(string filePath) => "html/" + Path.GetFileName(filePath).ToLowerInvariant();

    private static int CountUrlNodes(Node node) => node switch
    {
        UrlNode => 1,
        FolderNode folder => folder.Children.Sum(CountUrlNodes),
        _ => 0,
    };
}
