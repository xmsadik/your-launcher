namespace YourLauncher.Core.Config;

/// <summary>
/// Suggests a display name from a raw target string (spec §9 step 3), used by the Editor to auto-fill
/// Name when a target is browsed/typed and Name is still empty or still equals the previous suggestion
/// (see EditorViewModel in Launcher.App). Order of detection: an existing directory → its name; an
/// existing file → exe file description/product name, else file name without extension; otherwise a
/// URL (with or without an explicit scheme) → its host; otherwise a path-shaped string that doesn't
/// exist on disk → file name without extension; anything else → the raw text.
///
/// The exe-description lookup is injected as <paramref name="fileDescription"/> so Core has no
/// dependency on System.Diagnostics.FileVersionInfo (a Windows/App-layer concern); the App passes the
/// real FileVersionInfo.FileDescription → ProductName fallback chain, tests pass a stub.
/// </summary>
public static class TargetNameHelper
{
    public static string SuggestName(string target, Func<string, string?>? fileDescription = null)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return "";
        }

        var trimmed = target.Trim();

        if (Directory.Exists(trimmed))
        {
            var dirName = new DirectoryInfo(trimmed.TrimEnd('\\', '/')).Name;
            return string.IsNullOrEmpty(dirName) ? trimmed : dirName;
        }

        if (File.Exists(trimmed))
        {
            return NameFromFilePath(trimmed, fileDescription);
        }

        if (TryGetUrlHost(trimmed, out var host))
        {
            return host;
        }

        if (LooksLikeFilePath(trimmed))
        {
            return NameFromFilePath(trimmed, fileDescription);
        }

        var withoutExt = Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrEmpty(withoutExt) ? trimmed : withoutExt;
    }

    private static string NameFromFilePath(string path, Func<string, string?>? fileDescription)
    {
        if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) && fileDescription is not null)
        {
            var description = fileDescription(path);
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description;
            }
        }

        var withoutExt = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrEmpty(withoutExt) ? path : withoutExt;
    }

    private static bool LooksLikeFilePath(string value) =>
        value.Contains('\\') ||
        (value.Length >= 2 && value[1] == ':') || // drive letter, e.g. "C:\..." or bare "C:"
        value.StartsWith(@"\\", StringComparison.Ordinal); // UNC

    private static bool TryGetUrlHost(string value, out string host)
    {
        host = "";

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme is "http" or "https" or "ftp"))
        {
            host = uri.Host;
            return true;
        }

        if (LooksLikeFilePath(value))
        {
            return false;
        }

        // Bare host typed without a scheme, e.g. "github.com" or "github.com/anthropics".
        if (Uri.TryCreate("https://" + value, UriKind.Absolute, out var bareUri) && bareUri.Host.Contains('.'))
        {
            host = bareUri.Host;
            return true;
        }

        return false;
    }
}
