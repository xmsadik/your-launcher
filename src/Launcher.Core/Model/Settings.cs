using System.Text.Json.Serialization;
using YourLauncher.Core.Json;

namespace YourLauncher.Core.Model;

/// <summary>
/// Panel color theme. System/Light/Dark keep their original order and config names; the rest are popular
/// editor palettes (config names are the camelCase member names, e.g. "catppuccinMocha"). Colors live in
/// <see cref="Theming.ThemeCatalog"/>.
/// </summary>
public enum Theme
{
    System,
    Light,
    Dark,
    CatppuccinMocha,
    CatppuccinMacchiato,
    CatppuccinFrappe,
    CatppuccinLatte,
    TokyoNight,
    TokyoNightStorm,
    TokyoNightDay,
    Dracula,
    Nord,
    GruvboxDark,
    GruvboxLight,
    OneDark,
    SolarizedDark,
    SolarizedLight,
    Monokai,
    GithubDark,
    GithubLight,
    RosePine,
    CaelestiaDarkGlass,
}

/// <summary>
/// UI language. <see cref="System"/> follows the Windows display language (Turkish when that is Turkish, English
/// otherwise); the others pin one. Config names are the camelCase member names: "system", "en", "tr".
/// </summary>
public enum AppLanguage
{
    System,
    En,
    Tr,
}

/// <summary>
/// Where the panel opens on the cursor's monitor; always horizontally centered in the work area. Config names:
/// "center" (top edge at a third of the work area - the original placement), "top", "bottom" (flush with that
/// edge of the work area, minus <see cref="Settings.EdgeMargin"/>).
/// </summary>
public enum PanelPosition
{
    Center,
    Top,
    Bottom,
}

/// <summary>
/// How the panel window is drawn. <see cref="Floating"/> (default) is the original rounded window over the Mica/Acrylic
/// backdrop. <see cref="Docked"/> is an opaque panel with transparent side margins, shaped by <see cref="Settings.Position"/>
/// so it fuses with a screen-edge bar. Config names: "floating", "docked". Decided once at startup (a layered window
/// cannot be switched later), so a change takes effect after the next start.
/// </summary>
public enum PanelStyle
{
    Floating,
    Docked,
}

/// <summary>Application settings, spec §4.5 / §11.</summary>
public sealed class Settings
{
    public string Hotkey { get; set; } = "Alt+Space";

    [JsonConverter(typeof(JsonCamelCaseEnumConverter<Theme>))]
    public Theme Theme { get; set; } = Theme.System;

    /// <summary>UI language; a change takes effect after the next start (the language is applied before any UI text is created).</summary>
    [JsonConverter(typeof(JsonCamelCaseEnumConverter<AppLanguage>))]
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Panel background opacity in percent (<see cref="MinOpacity"/>-100) over the Windows 11 Mica/Acrylic backdrop; text stays opaque. No visible effect where the backdrop is unavailable (Windows 10).</summary>
    public int Opacity { get; set; } = 90;

    public const int MinOpacity = 40;

    /// <summary>When true the panel's accent color follows the Windows accent color instead of the palette's own accent (the rest of the palette is unchanged).</summary>
    public bool UseWindowsAccent { get; set; } = false;

    [JsonConverter(typeof(JsonCamelCaseEnumConverter<PanelPosition>))]
    public PanelPosition Position { get; set; } = PanelPosition.Center;

    /// <summary>Gap in DIPs between the work-area edge and the panel when <see cref="Position"/> is Top or Bottom (0-<see cref="MaxEdgeMargin"/>); unused for Center.</summary>
    public int EdgeMargin { get; set; } = 0;

    public const int MaxEdgeMargin = 200;

    /// <summary>Window style of the panel; applied at the next start. <see cref="Opacity"/> has no effect while Docked (the panel is opaque).</summary>
    [JsonConverter(typeof(JsonCamelCaseEnumConverter<PanelStyle>))]
    public PanelStyle PanelStyle { get; set; } = PanelStyle.Floating;

    /// <summary>
    /// When true the window switcher also lists windows that are cloaked because they sit on another virtual
    /// desktop or on a hidden tiling-WM workspace (GlazeWM's default <c>hide_method: cloak</c>). Off by default
    /// to match Alt+Tab; suspended Store-app "ghost" windows are filtered out either way.
    /// </summary>
    public bool IncludeOtherDesktopWindows { get; set; } = false;

    public bool StartWithWindows { get; set; } = true;

    public bool CloseAfterLaunch { get; set; } = true;

    public int MaxVisibleItems { get; set; } = 8;

    [JsonConverter(typeof(JsonCamelCaseEnumConverter<ShellKind>))]
    public ShellKind DefaultShell { get; set; } = ShellKind.Pwsh;

    public bool RememberLastLocation { get; set; } = false;

    public bool ShowHintBar { get; set; } = true;

    /// <summary>
    /// Typing "&lt;prefix&gt; &lt;text&gt;" (default "f report") switches the list to file search. Empty disables file
    /// search. Matched case-insensitively and only when followed by a space, so "fox" never triggers it.
    /// </summary>
    public string FileSearchPrefix { get; set; } = DefaultFileSearchPrefix;

    public const string DefaultFileSearchPrefix = "f";
}
