using System.Text.Json;

namespace YourLauncher.Core.Bookmarks;

/// <summary>
/// Finds every Chromium-family (and Opera/Opera GX) browser profile with a <c>Bookmarks</c> file, without
/// ever reading the file itself - just the paths, so the Settings page can list them and read the chosen
/// one on demand (spec §1.1). The two roots are injected (rather than read from
/// <see cref="Environment.SpecialFolder"/> here) so tests can point this at a temp directory tree instead
/// of the real profile; the App layer passes the real <c>%LOCALAPPDATA%</c>/<c>%APPDATA%</c>.
///
/// Never throws for a missing/unreadable user-data dir or a malformed <c>Local State</c> - either is
/// simply skipped (falling back to the profile's directory name when <c>Local State</c> can't supply a
/// nicer one), since a browser that isn't installed, or whose profile folder briefly can't be read, is a
/// completely ordinary situation here, not an error.
/// </summary>
public static class ChromiumBookmarkLocator
{
    /// <summary>Chromium-family browsers whose user-data dir lives under <c>%LOCALAPPDATA%</c>, each with one or more <c>Default</c>/<c>Profile *</c> subfolders.</summary>
    private static readonly (string Browser, string RelativeUserDataDir)[] LocalAppDataBrowsers =
    {
        ("Chrome", @"Google\Chrome\User Data"),
        ("Edge", @"Microsoft\Edge\User Data"),
        ("Brave", @"BraveSoftware\Brave-Browser\User Data"),
        ("Vivaldi", @"Vivaldi\User Data"),
        ("Chromium", @"Chromium\User Data"),
    };

    /// <summary>Opera/Opera GX keep their single profile's <c>Bookmarks</c> file directly under <c>%APPDATA%</c> - no <c>Default</c>/<c>Profile *</c> subfolder, treated as one profile (spec §1.1).</summary>
    private static readonly (string Browser, string RelativeDir)[] RoamingAppDataBrowsers =
    {
        ("Opera", @"Opera Software\Opera Stable"),
        ("Opera GX", @"Opera Software\Opera GX Stable"),
    };

    public static IReadOnlyList<BookmarkSource> Discover(string localAppData, string roamingAppData)
    {
        var sources = new List<BookmarkSource>();

        foreach (var (browser, relativeUserDataDir) in LocalAppDataBrowsers)
        {
            sources.AddRange(DiscoverUserDataProfiles(browser, Path.Combine(localAppData, relativeUserDataDir)));
        }

        foreach (var (browser, relativeDir) in RoamingAppDataBrowsers)
        {
            var bookmarksPath = Path.Combine(roamingAppData, relativeDir, "Bookmarks");
            if (File.Exists(bookmarksPath))
            {
                sources.Add(new BookmarkSource(browser, ProfileDir: "", ProfileName: "", bookmarksPath)
                {
                    SourceKey = $"{browser.ToLowerInvariant()}/",
                    DisplayName = browser,
                });
            }
        }

        return sources;
    }

    private static List<BookmarkSource> DiscoverUserDataProfiles(string browser, string userDataDir)
    {
        var profiles = new List<(string Dir, string Name, string BookmarksPath)>();

        try
        {
            if (!Directory.Exists(userDataDir))
            {
                return new List<BookmarkSource>();
            }

            var profileNames = ReadProfileNames(userDataDir);

            foreach (var dir in Directory.EnumerateDirectories(userDataDir))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName != "Default" && !dirName.StartsWith("Profile ", StringComparison.Ordinal))
                {
                    continue;
                }

                var bookmarksPath = Path.Combine(dir, "Bookmarks");
                if (!File.Exists(bookmarksPath))
                {
                    continue;
                }

                var name = profileNames.TryGetValue(dirName, out var n) && !string.IsNullOrWhiteSpace(n) ? n : dirName;
                profiles.Add((dirName, name, bookmarksPath));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new List<BookmarkSource>();
        }

        // Only distinguish profiles by name once there's more than one - a single-profile browser is just
        // "Chrome", not "Chrome (Default)" (spec §1.1's "DisplayName" example: "Chrome (Work)").
        var multipleProfiles = profiles.Count > 1;

        var result = new List<BookmarkSource>(profiles.Count);
        foreach (var (dir, name, bookmarksPath) in profiles)
        {
            result.Add(new BookmarkSource(browser, dir, name, bookmarksPath)
            {
                SourceKey = $"{browser.ToLowerInvariant()}/{dir.ToLowerInvariant()}",
                DisplayName = multipleProfiles ? $"{browser} ({name})" : browser,
            });
        }

        return result;
    }

    /// <summary>Reads <c>profile.info_cache.&lt;dirName&gt;.name</c> out of <c>Local State</c> (spec §1.1) - missing file, unreadable JSON, or a missing/non-string name for a given profile all just fall back to that profile's directory name at the call site.</summary>
    private static Dictionary<string, string> ReadProfileNames(string userDataDir)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var localStatePath = Path.Combine(userDataDir, "Local State");
            if (!File.Exists(localStatePath))
            {
                return result;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("profile", out var profile) &&
                profile.ValueKind == JsonValueKind.Object &&
                profile.TryGetProperty("info_cache", out var infoCache) &&
                infoCache.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in infoCache.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.Object && entry.Value.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    {
                        result[entry.Name] = nameEl.GetString() ?? entry.Name;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Malformed/unreadable Local State - skip silently (spec §1.1), profile dir names are used instead.
        }

        return result;
    }
}
