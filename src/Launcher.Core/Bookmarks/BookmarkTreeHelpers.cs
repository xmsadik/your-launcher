using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Bookmarks;

/// <summary>Small pieces of tree-building logic shared by <see cref="ChromiumBookmarkParser"/> and <see cref="NetscapeBookmarkParser"/> (spec §1.2/§1.3: both apply the exact same javascript:/empty-title/empty-folder rules).</summary>
internal static class BookmarkTreeHelpers
{
    /// <summary>Recursively drops any <see cref="FolderNode"/> that ends up with no children, including nested ones that only became empty once their own children were pruned (spec: "Drop folders that end up empty (recursively), including empty roots").</summary>
    public static void PruneEmptyFolders(FolderNode folder)
    {
        for (var i = folder.Children.Count - 1; i >= 0; i--)
        {
            if (folder.Children[i] is FolderNode child)
            {
                PruneEmptyFolders(child);
                if (child.Children.Count == 0)
                {
                    folder.Children.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>"javascript:" bookmarklets are skipped entirely (spec), case-insensitively per the scheme's own casing rules.</summary>
    public static bool IsJavascriptBookmarklet(string url) =>
        url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Empty bookmark title -> the URL's host, or the raw URL if it has none (spec).</summary>
    public static string TitleOrUrlHost(string? title, string url) =>
        !string.IsNullOrEmpty(title)
            ? title
            : Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
                ? uri.Host
                : url;

    /// <summary>Empty folder name -> "(unnamed)" (spec).</summary>
    public static string FolderNameOrUnnamed(string? name) => string.IsNullOrEmpty(name) ? "(unnamed)" : name;

    public static FolderNode NewFolder(string name) => new() { Id = TreeOps.NewId(), Name = name };
}
