namespace YourLauncher.Core.Bookmarks;

/// <summary>
/// One discovered Chromium-family bookmarks file (<see cref="ChromiumBookmarkLocator.Discover"/>).
/// <see cref="SourceKey"/> and <see cref="DisplayName"/> are computed by the locator once every profile
/// for a browser is known - <see cref="SourceKey"/> needs no such context (it's derived purely from
/// <paramref name="Browser"/>/<paramref name="ProfileDir"/>), but <see cref="DisplayName"/> only shows the
/// profile name in parentheses once the locator has seen that the browser actually has more than one
/// profile - hence <c>required</c> properties set by the locator rather than computed inline in the
/// primary constructor.
/// </summary>
/// <param name="Browser">Display name of the browser itself, e.g. "Chrome", "Edge", "Opera GX".</param>
/// <param name="ProfileDir">The profile's directory name under the browser's user-data dir (e.g. "Default", "Profile 1"), or "" for a single-profile browser (Opera/Opera GX).</param>
/// <param name="ProfileName">The profile's display name from <c>Local State</c>, or the directory name if that couldn't be read.</param>
/// <param name="BookmarksPaths">Full paths of the profile's existing bookmark files: <c>AccountBookmarks</c> (signed-in Chrome keeps Google-account bookmarks there) and/or <c>Bookmarks</c> (local ones) - imported merged.</param>
public sealed record BookmarkSource(string Browser, string ProfileDir, string ProfileName, IReadOnlyList<string> BookmarksPaths)
{
    /// <summary>Stable, lowercase key identifying this source across re-imports, e.g. "chrome/default", "edge/profile 1", "opera/".</summary>
    public required string SourceKey { get; init; }

    /// <summary>What the Settings page's menu shows, e.g. "Chrome", or "Chrome (Work)" once the browser has more than one profile.</summary>
    public required string DisplayName { get; init; }
}
