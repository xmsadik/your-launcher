using YourLauncher.Core.Launch;

namespace YourLauncher.Core.Icons;

/// <summary>
/// Decides whether an <c>app</c>/<c>path</c> node's target is worth a disk existence check (spec §4.4 row
/// dimming / §8.1 missing-target badge, revised §7.2: never block on network paths). Shared by the row's
/// missing badge (<see cref="Services.ListItemViewModel"/>, App layer) and <see cref="EditorViewModel"/>'s
/// "target not found" warning, so both agree on what counts as missing.
/// </summary>
public static class TargetCheck
{
    /// <summary>
    /// True only for a fully-qualified local path (<see cref="Path.IsPathFullyQualified(string)"/>).
    /// False for a UNC path (never touch the network per spec §7.2), a URI/shell target
    /// (<c>shell:...</c>, <c>https://...</c>, ...), or a bare name resolved via <c>%PATH%</c> (e.g.
    /// <c>notepad.exe</c>) - those go through <see cref="PathResolver"/> instead, in <see cref="IsMissing"/>.
    /// </summary>
    public static bool ShouldCheckExistence(string expandedTarget)
    {
        if (string.IsNullOrWhiteSpace(expandedTarget))
        {
            return false;
        }

        if (expandedTarget.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false; // UNC - Path.IsPathFullyQualified would say true; spec explicitly excludes it.
        }

        return Path.IsPathFullyQualified(expandedTarget);
    }

    /// <summary>
    /// True when <paramref name="target"/> (env-expanded) is a local path that doesn't exist, or a bare
    /// name that doesn't resolve on <c>%PATH%</c>. False for UNC/URI/shell targets (never checked - spec
    /// §7.2) and for an empty target (the editor's own "name required"-style validation covers that).
    /// </summary>
    public static bool IsMissing(string target)
    {
        var expanded = EnvExpander.Expand(target) ?? "";
        if (expanded.Length == 0)
        {
            return false;
        }

        if (ShouldCheckExistence(expanded))
        {
            return !File.Exists(expanded) && !Directory.Exists(expanded);
        }

        return LooksLikeBareName(expanded) && PathResolver.FindOnPath(expanded) is null;
    }

    /// <summary>No path separator and no scheme/drive colon - the shape %PATH%-resolution applies to.</summary>
    private static bool LooksLikeBareName(string value) =>
        !value.Contains('\\') && !value.Contains('/') && !value.Contains(':');
}
