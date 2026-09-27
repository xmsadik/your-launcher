using YourLauncher.Core.Model;

namespace YourLauncher.Core.Config;

/// <summary>
/// Everything <see cref="ShellLinkResolver"/> (App layer, <c>Interop/ShellLink.cs</c>) reads out of a
/// <c>.lnk</c> file via <c>IShellLinkW</c>: target path (env vars preserved, spec §10 item 6d - no
/// <c>Resolve</c> call, just <c>IPersistFile.Load</c> + <c>GetPath(SLGP_RAWPATH)</c>), arguments, working
/// directory, and icon location/index. A null/empty <see cref="Target"/> means an advertised/MSI shortcut
/// with no resolvable target (spec §6).
/// </summary>
public readonly record struct ShellLinkInfo(string? Target, string? Arguments, string? WorkingDirectory, string? IconLocation, int? IconIndex);

/// <summary>
/// Pure mapping from a dropped file path (Explorer/Start-menu drag & drop, spec §6/AC12) to the <see
/// cref="Node"/> it becomes, once added to the current folder. <see cref="ResolveShellLink"/> and
/// <see cref="FileDescription"/> are injected (same pattern as <see cref="TargetNameHelper"/>) so Core
/// stays free of the Win32 IShellLinkW/FileVersionInfo dependencies those two live in the App layer.
/// </summary>
public static class DropMapper
{
    /// <summary>Extensions that launch as an app, whether dropped directly or found as a .lnk's target (spec §10 item 6f).</summary>
    private static readonly string[] AppExtensions = { ".exe", ".bat", ".cmd", ".com", ".msc" };

    /// <summary>
    /// Maps one dropped path to the node it should become. <paramref name="resolveShellLink"/> is only
    /// invoked for a <c>.lnk</c> path; <paramref name="fileDescription"/> feeds the same exe-description
    /// name lookup <see cref="TargetNameHelper"/> uses elsewhere.
    /// </summary>
    public static Node Map(string path, Func<string, ShellLinkInfo> resolveShellLink, Func<string, string?>? fileDescription)
    {
        var ext = Path.GetExtension(path);

        if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return MapShortcut(path, resolveShellLink(path), fileDescription);
        }

        if (string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase))
        {
            return MapInternetShortcut(path);
        }

        var name = TargetNameHelper.SuggestName(path, fileDescription);
        return IsAppExtension(ext)
            ? new AppNode { Name = name, Target = path }
            : new PathNode { Name = name, Target = path };
    }

    private static Node MapShortcut(string lnkPath, ShellLinkInfo info, Func<string, string?>? fileDescription)
    {
        var name = Path.GetFileNameWithoutExtension(lnkPath);
        var icon = info.IconLocation is { Length: > 0 } location
            ? new IconSpec { Kind = IconKind.Exe, Value = location, Index = info.IconIndex }
            : null;

        if (string.IsNullOrWhiteSpace(info.Target))
        {
            // Advertised/MSI shortcut (spec §6): no resolvable target - ShellExecute the .lnk itself, so
            // it still launches the way double-clicking it in Explorer would.
            return new AppNode { Name = name, Target = lnkPath, Icon = icon };
        }

        var targetExt = Path.GetExtension(info.Target);
        if (IsAppExtension(targetExt))
        {
            return new AppNode
            {
                Name = name,
                Target = info.Target,
                Arguments = string.IsNullOrEmpty(info.Arguments) ? null : info.Arguments,
                WorkingDirectory = string.IsNullOrEmpty(info.WorkingDirectory) ? null : info.WorkingDirectory,
                Icon = icon,
            };
        }

        return new PathNode { Name = name, Target = info.Target, Icon = icon };
    }

    private static Node MapInternetShortcut(string urlPath)
    {
        var name = Path.GetFileNameWithoutExtension(urlPath);
        return new UrlNode { Name = name, Target = ReadUrl(urlPath) ?? "" };
    }

    /// <summary>Reads the <c>URL=</c> line out of a <c>[InternetShortcut]</c> (.url) file. Never throws - an unreadable file just yields an empty target, same as any other node the editor would flag as incomplete.</summary>
    private static string? ReadUrl(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    return line["URL=".Length..].Trim();
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static bool IsAppExtension(string extension) =>
        AppExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}
