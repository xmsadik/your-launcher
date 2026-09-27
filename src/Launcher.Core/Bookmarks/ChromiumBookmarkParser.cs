using System.Text.Json;
using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Bookmarks;

/// <summary>
/// Parses a Chromium-family <c>Bookmarks</c> file (plain JSON, no reflection serialization - spec §1.2)
/// into a <see cref="FolderNode"/> named <paramref name="folderName"/> holding up to three top-level
/// folders: "Bookmarks bar", "Other bookmarks", "Mobile bookmarks" (from <c>roots.bookmark_bar</c>/
/// <c>roots.other</c>/<c>roots.synced</c> respectively - their own <c>name</c> field in the JSON is
/// ignored, these three names are always fixed). Every node gets a fresh id (<see cref="TreeOps.NewId"/>);
/// "javascript:" bookmarklets and any node without a URL are skipped; an empty title becomes the URL's
/// host; an empty folder ends up dropped entirely, recursively, including one of the three roots above.
/// </summary>
public static class ChromiumBookmarkParser
{
    public static FolderNode Parse(string json, string folderName) => Parse(new[] { json }, folderName);

    /// <summary>
    /// Several files of one profile merged into one tree - signed-in Chrome keeps the Google-account
    /// bookmarks in <c>AccountBookmarks</c> and only local ones in <c>Bookmarks</c>, and shows both merged
    /// under the same three roots. Children are appended in file order.
    /// </summary>
    public static FolderNode Parse(IReadOnlyList<string> jsons, string folderName)
    {
        var result = BookmarkTreeHelpers.NewFolder(folderName);
        var bar = BookmarkTreeHelpers.NewFolder("Bookmarks bar");
        var other = BookmarkTreeHelpers.NewFolder("Other bookmarks");
        var synced = BookmarkTreeHelpers.NewFolder("Mobile bookmarks");
        result.Children.Add(bar);
        result.Children.Add(other);
        result.Children.Add(synced);

        foreach (var json in jsons)
        {
            AddFile(json, bar, other, synced);
        }

        BookmarkTreeHelpers.PruneEmptyFolders(result); // drops any of the three roots that ended up empty.
        return result;
    }

    private static void AddFile(string json, FolderNode bar, FolderNode other, FolderNode synced)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new BookmarkImportException($"Could not parse bookmarks file: {ex.Message}", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("roots", out var roots) || roots.ValueKind != JsonValueKind.Object)
            {
                throw new BookmarkImportException("Could not parse bookmarks file: missing 'roots'.");
            }

            AddFixedRoot(bar, roots, "bookmark_bar");
            AddFixedRoot(other, roots, "other");
            AddFixedRoot(synced, roots, "synced");
        }
    }

    private static void AddFixedRoot(FolderNode folder, JsonElement roots, string rootKey)
    {
        if (roots.TryGetProperty(rootKey, out var rootElement) && rootElement.ValueKind == JsonValueKind.Object)
        {
            ParseChildrenInto(folder, rootElement);
        }
    }

    private static void ParseChildrenInto(FolderNode folder, JsonElement nodeElement)
    {
        if (!nodeElement.TryGetProperty("children", out var childrenElement) || childrenElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var childElement in childrenElement.EnumerateArray())
        {
            var node = ParseNode(childElement);
            if (node is not null)
            {
                folder.Children.Add(node);
            }
        }
    }

    private static Node? ParseNode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null; // e.g. a stray null/string inside "children" - TryGetProperty would throw on it.
        }

        var type = GetString(element, "type");
        return type switch
        {
            "folder" => ParseFolder(element),
            "url" => ParseUrl(element),
            _ => null, // unknown node type - skip.
        };
    }

    private static FolderNode ParseFolder(JsonElement element)
    {
        var folder = BookmarkTreeHelpers.NewFolder(BookmarkTreeHelpers.FolderNameOrUnnamed(GetString(element, "name")));
        ParseChildrenInto(folder, element);
        return folder;
    }

    private static UrlNode? ParseUrl(JsonElement element)
    {
        var url = GetString(element, "url");
        if (string.IsNullOrEmpty(url) || BookmarkTreeHelpers.IsJavascriptBookmarklet(url))
        {
            return null;
        }

        return new UrlNode
        {
            Id = TreeOps.NewId(),
            Name = BookmarkTreeHelpers.TitleOrUrlHost(GetString(element, "name"), url),
            Target = url,
        };
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
