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
}

/// <summary>Application settings, spec §4.5 / §11.</summary>
public sealed class Settings
{
    public string Hotkey { get; set; } = "Alt+Space";

    [JsonConverter(typeof(JsonCamelCaseEnumConverter<Theme>))]
    public Theme Theme { get; set; } = Theme.System;

    /// <summary>Panel background opacity in percent (<see cref="MinOpacity"/>-100) over the Windows 11 Mica/Acrylic backdrop; text stays opaque. No visible effect where the backdrop is unavailable (Windows 10).</summary>
    public int Opacity { get; set; } = 90;

    public const int MinOpacity = 40;

    /// <summary>When true the panel's accent color follows the Windows accent color instead of the palette's own accent (the rest of the palette is unchanged).</summary>
    public bool UseWindowsAccent { get; set; } = false;

    public bool StartWithWindows { get; set; } = true;

    public bool CloseAfterLaunch { get; set; } = true;

    public int MaxVisibleItems { get; set; } = 8;

    [JsonConverter(typeof(JsonCamelCaseEnumConverter<ShellKind>))]
    public ShellKind DefaultShell { get; set; } = ShellKind.Pwsh;

    public bool RememberLastLocation { get; set; } = false;

    public bool ShowHintBar { get; set; } = true;
}
