using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.StartMenu;

/// <summary>Where a <see cref="StartMenuApp"/> came from; decides how it becomes a node.</summary>
public enum StartMenuAppKind
{
    /// <summary>A <c>.lnk</c> under a Start-menu Programs folder (classic desktop apps); <see cref="StartMenuApp.Source"/> is the .lnk path.</summary>
    Shortcut,

    /// <summary>A packaged (Store/MSIX) app from the shell's AppsFolder; <see cref="StartMenuApp.Source"/> is its AppUserModelID.</summary>
    Packaged,
}

/// <summary>
/// One candidate in the "Import from Start menu" picker. <see cref="Key"/> is the stable identity a re-import
/// finds the node it created earlier by (see <see cref="StartMenuImport.ItemId"/>), so it must not depend on
/// anything the user or an app update can change (display names do; the shortcut's path under Programs and the
/// package's AppUserModelID do not).
/// </summary>
/// <param name="Key">"lnk:&lt;path relative to its Programs folder, lowercase&gt;" or "pkg:&lt;AppUserModelID, lowercase&gt;".</param>
/// <param name="Name">Display name shown in the picker and used as the node name.</param>
/// <param name="Kind">Shortcut or packaged app.</param>
/// <param name="Source">The .lnk path, or the AppUserModelID.</param>
public sealed record StartMenuApp(string Key, string Name, StartMenuAppKind Kind, string Source)
{
    /// <summary>Prefix of the shell URI that launches a packaged app (<c>shell:AppsFolder\&lt;AUMID&gt;</c>).</summary>
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    /// <summary>
    /// Shortcut key from the path relative to its Programs folder. Slash-normalized and lowercased, so the
    /// per-user and the all-users copy of one entry collapse into a single picker row (the user's wins).
    /// </summary>
    public static string ShortcutKey(string relativePath) =>
        "lnk:" + relativePath.Replace('\\', '/').ToLowerInvariant();

    public static string PackagedKey(string appUserModelId) => "pkg:" + appUserModelId.ToLowerInvariant();

    /// <summary>
    /// True for a packaged app's AppUserModelID ("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App": package family
    /// name, '!', app id) as opposed to what the AppsFolder reports for classic desktop apps (a known-folder
    /// path or a bare AUMID without '!'). Desktop apps are imported from their .lnk instead, which gives a real
    /// target, arguments and icon; AppsFolder is used only for what has no shortcut.
    /// </summary>
    public static bool IsPackagedAppId(string parsingName) =>
        parsingName.Contains('!') && parsingName.Contains('_') && !parsingName.Contains('\\') && !parsingName.Contains('/') && !parsingName.Contains(':');

    /// <summary>
    /// The node this entry becomes. A shortcut goes through <see cref="DropMapper"/> exactly like a .lnk dropped
    /// onto the panel (app node with target/arguments/working directory/icon, or the .lnk itself when the
    /// shortcut has no resolvable target). A packaged app becomes a path node that opens
    /// <c>shell:AppsFolder\&lt;AUMID&gt;</c> - ShellExecute starts it, no package-query permission needed, and
    /// a path node (unlike an app node) does not derive a bogus working directory from the URI.
    /// </summary>
    public Node ToNode(Func<string, ShellLinkInfo> resolveShellLink)
    {
        if (Kind == StartMenuAppKind.Packaged)
        {
            return new PathNode { Name = Name, Target = AppsFolderPrefix + Source };
        }

        var node = DropMapper.Map(Source, resolveShellLink, fileDescription: null);
        node.Name = Name;
        return node;
    }
}
