using System.Net;
using System.Text.RegularExpressions;
using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Bookmarks;

/// <summary>
/// Parses a Netscape-format bookmarks HTML export (spec §1.3) - the common export format shared by
/// Firefox, Chrome, Edge and Safari's "export bookmarks to HTML" feature. The format is old, informal HTML
/// where <c>&lt;DT&gt;</c>/<c>&lt;p&gt;</c> tags are never closed; a tolerant regex tokenizer (rather than
/// an actual HTML parser) is exactly what the format calls for. Recognizes, case-insensitively, in
/// document order:
/// <list type="bullet">
/// <item><c>&lt;H3&gt;name&lt;/H3&gt;</c> immediately followed by a <c>&lt;DL&gt;</c> = a folder,</item>
/// <item><c>&lt;A HREF="..."&gt;title&lt;/A&gt;</c> = a bookmark,</item>
/// <item><c>&lt;/DL&gt;</c> closes the folder currently being built.</item>
/// </list>
/// The top-level <c>&lt;H1&gt;</c> is ignored (never matched); top-level folders/links (inside the
/// outermost <c>&lt;DL&gt;</c>, which has no preceding <c>&lt;H3&gt;</c>) go directly into the returned
/// folder. Same javascript:/empty-title/empty-folder rules as <see cref="ChromiumBookmarkParser"/>; names
/// and hrefs are HTML-entity-decoded. Throws <see cref="BookmarkImportException"/> if no bookmarks are
/// found at all.
/// </summary>
public static class NetscapeBookmarkParser
{
    // One alternation per token this tokenizer cares about; everything else (DOCTYPE, META, TITLE, DT, P,
    // ...) is simply never matched and therefore ignored, which is exactly what "tolerant" means here.
    private static readonly Regex Tokens = new(
        """<H3\b[^>]*>(?<h3name>.*?)</H3\s*>|<A\s+(?<aattrs>[^>]*?)>(?<aname>.*?)</A\s*>|(?<dlopen><DL\b[^>]*>)|(?<dlclose></DL\s*>)""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HrefAttribute = new(
        "HREF\\s*=\\s*\"(?<href>[^\"]*)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static FolderNode Parse(string html, string folderName)
    {
        var root = BookmarkTreeHelpers.NewFolder(folderName);
        var stack = new Stack<FolderNode>();
        stack.Push(root);
        string? pendingFolderName = null;

        foreach (Match match in Tokens.Matches(html))
        {
            if (match.Groups["dlclose"].Success)
            {
                // Every <DL> push (real folder or the "virtual" duplicate for the outermost wrapper) has a
                // matching pop; tolerate an extra stray </DL> by never popping past the root frame.
                if (stack.Count > 1)
                {
                    stack.Pop();
                }
            }
            else if (match.Groups["dlopen"].Success)
            {
                if (pendingFolderName is { } name)
                {
                    var folder = BookmarkTreeHelpers.NewFolder(name);
                    stack.Peek().Children.Add(folder);
                    stack.Push(folder);
                    pendingFolderName = null;
                }
                else
                {
                    // The outermost <DL> (no preceding <H3>) - top-level items go straight into the current
                    // folder, so push a duplicate reference rather than a new folder (spec: "top-level
                    // folders/links go directly into the result folder").
                    stack.Push(stack.Peek());
                }
            }
            else if (match.Groups["h3name"].Success)
            {
                // A stray <H3> that's never followed by a <DL> is malformed input; just drop the
                // previous pending name rather than ever creating a folder for it.
                var decoded = WebUtility.HtmlDecode(match.Groups["h3name"].Value);
                pendingFolderName = BookmarkTreeHelpers.FolderNameOrUnnamed(decoded);
            }
            else if (match.Groups["aattrs"].Success)
            {
                pendingFolderName = null;
                AddBookmark(stack.Peek(), match.Groups["aattrs"].Value, match.Groups["aname"].Value);
            }
        }

        BookmarkTreeHelpers.PruneEmptyFolders(root);

        if (root.Children.Count == 0)
        {
            throw new BookmarkImportException($"No bookmarks found in {folderName}.");
        }

        return root;
    }

    private static void AddBookmark(FolderNode folder, string attributes, string rawTitle)
    {
        var hrefMatch = HrefAttribute.Match(attributes);
        if (!hrefMatch.Success)
        {
            return; // no HREF at all - not a real bookmark, skip (spec: "any node without a URL").
        }

        var url = WebUtility.HtmlDecode(hrefMatch.Groups["href"].Value);
        if (string.IsNullOrEmpty(url) || BookmarkTreeHelpers.IsJavascriptBookmarklet(url))
        {
            return;
        }

        var title = WebUtility.HtmlDecode(rawTitle);
        folder.Children.Add(new UrlNode
        {
            Id = TreeOps.NewId(),
            Name = BookmarkTreeHelpers.TitleOrUrlHost(title, url),
            Target = url,
        });
    }
}
