using YourLauncher.Core.Model;

namespace YourLauncher.App.Services;

/// <summary>
/// Per-type default Segoe MDL2 glyphs (spec §5, decision D6) and the curated custom-glyph validity check
/// (spec §7.12: only PUA code points actually present in Segoe MDL2 Assets, the Windows-10-safe E700-E9FF
/// range, are accepted as a custom glyph value - anything else, e.g. a hand-edited config with a stray
/// character, falls back to the node's type default).
/// </summary>
public static class IconGlyphs
{
    public const string Folder = "";
    public const string App = "";
    public const string Path = "";
    public const string Command = "";
    public const string Url = "";

    public static string DefaultFor(Node node) => node switch
    {
        FolderNode => Folder,
        AppNode => App,
        PathNode => Path,
        CommandNode => Command,
        UrlNode => Url,
        _ => Folder,
    };

    public static bool IsValidCustomGlyph(string value) =>
        value.Length == 1 && value[0] >= 0xE700 && value[0] <= 0xE9FF;
}
