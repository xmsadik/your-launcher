namespace YourLauncher.Core.Icons;

/// <summary>
/// Resolves a bare executable name (no directory separators, e.g. "notepad.exe" or "git") against
/// <c>%PATH%</c> × <c>%PATHEXT%</c>, the same lookup Windows itself performs for <c>app</c>/<c>path</c>
/// nodes whose target isn't a full path (spec §7.2). Used by both <see cref="TargetCheck.IsMissing"/> and
/// <see cref="Services.IconService"/> (App layer) so the missing-target badge and the icon shown for a
/// bare-name target agree on what it resolves to.
/// </summary>
public static class PathResolver
{
    /// <summary>Full path of the first match found, or null if <paramref name="name"/> isn't found anywhere on <c>%PATH%</c>.</summary>
    public static string? FindOnPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var directories = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var candidates = Path.HasExtension(name) ? new[] { name } : PathExtCandidates(name).ToArray();

        foreach (var dir in directories)
        {
            foreach (var candidate in candidates)
            {
                string fullPath;
                try
                {
                    fullPath = Path.Combine(dir, candidate);
                }
                catch (ArgumentException)
                {
                    continue; // a malformed %PATH% entry - skip it rather than throw.
                }

                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> PathExtCandidates(string name)
    {
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        foreach (var ext in pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return name + ext;
        }
    }
}
