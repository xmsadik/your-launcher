using System.Globalization;
using System.Resources;

namespace YourLauncher.Core.Localization;

/// <summary>
/// User-visible text produced inside Core (bookmark-import and plugin-manifest errors, theme names). Same
/// pattern as the app's <c>Strings</c>: a hand-written accessor over <see cref="ResourceManager"/> following
/// <see cref="CultureInfo.CurrentUICulture"/>, so Core stays free of WPF and the Turkish
/// <c>CoreStrings.tr.resx</c> needs no code. Add a resx entry and a property here together.
/// </summary>
public static class CoreStrings
{
    private static readonly ResourceManager Manager = new("YourLauncher.Core.Localization.CoreStrings", typeof(CoreStrings).Assembly);

    private static string Get(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    /// <summary>{0} = parser message.</summary>
    public static string BookmarkParseFailed => Get(nameof(BookmarkParseFailed));

    /// <summary>Chromium bookmarks file without roots.</summary>
    public static string BookmarkMissingRoots => Get(nameof(BookmarkMissingRoots));

    /// <summary>{0} = source label.</summary>
    public static string BookmarkNoneFound => Get(nameof(BookmarkNoneFound));

    /// <summary>Config load error.</summary>
    public static string ConfigEmptyDocument => Get(nameof(ConfigEmptyDocument));

    /// <summary>{0} = parser message.</summary>
    public static string PluginJsonInvalid => Get(nameof(PluginJsonInvalid));

    /// <summary>Plugin problem.</summary>
    public static string PluginJsonEmpty => Get(nameof(PluginJsonEmpty));

    /// <summary>Plugin problem; the JSON property name stays.</summary>
    public static string PluginKeywordRequired => Get(nameof(PluginKeywordRequired));

    /// <summary>Plugin problem.</summary>
    public static string PluginKeywordNoSpaces => Get(nameof(PluginKeywordNoSpaces));

    /// <summary>Plugin problem.</summary>
    public static string PluginCommandRequired => Get(nameof(PluginCommandRequired));

    /// <summary>Plugin problem.</summary>
    public static string PluginArgsList => Get(nameof(PluginArgsList));

    /// <summary>Theme combo option.</summary>
    public static string ThemeSystem => Get(nameof(ThemeSystem));

    /// <summary>Theme combo option.</summary>
    public static string ThemeWindowsLight => Get(nameof(ThemeWindowsLight));

    /// <summary>Theme combo option.</summary>
    public static string ThemeWindowsDark => Get(nameof(ThemeWindowsDark));
}
