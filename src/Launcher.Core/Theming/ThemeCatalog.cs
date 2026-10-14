using YourLauncher.Core.Localization;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Theming;

/// <summary>
/// One theme's colors, one per shared brush key the app looks up (<c>PanelBackgroundBrush</c>,
/// <c>ForegroundBrush</c>, ...). Values are <c>#RRGGBB</c>; the App layer turns them into brushes, and
/// derives <c>PanelBackgroundTintBrush</c> from <see cref="Background"/> plus <see cref="Settings.Opacity"/>.
/// </summary>
public sealed record ThemePalette
{
    public required Theme Theme { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Drives the DWM immersive-dark-mode flag (dark/light Acrylic base under the tint).</summary>
    public required bool IsDark { get; init; }

    public required string Background { get; init; }
    public required string Border { get; init; }
    public required string Foreground { get; init; }
    public required string SecondaryText { get; init; }
    public required string Accent { get; init; }
    public required string Selection { get; init; }
    public required string Hover { get; init; }
    public required string Error { get; init; }
    public required string Warning { get; init; }
    public required string MissingBadge { get; init; }
    public required string InputBackground { get; init; }
}

/// <summary>
/// Every selectable theme. Light/Dark are the original Windows-style palettes, unchanged; the others
/// follow each project's published palette. Where a palette's own comment/status color is too faint on
/// its background (see ThemeCatalogTests' contrast floor) the nearest readable shade is used instead.
/// </summary>
public static class ThemeCatalog
{
    public static IReadOnlyList<ThemePalette> Palettes { get; } =
    [
        new()
        {
            Theme = Theme.Light, DisplayName = "Windows Light", IsDark = false,
            Background = "#F3F3F3", Border = "#CFCFCF", Foreground = "#1B1B1B", SecondaryText = "#5B5B5B",
            Accent = "#1D4ED8", Selection = "#E1E1E1", Hover = "#E9E9E9", Error = "#C6291B",
            Warning = "#946200", MissingBadge = "#B5651D", InputBackground = "#FFFFFF",
        },
        new()
        {
            Theme = Theme.Dark, DisplayName = "Windows Dark", IsDark = true,
            Background = "#1E1E1E", Border = "#3B4048", Foreground = "#E6E6E6", SecondaryText = "#8A8A8A",
            Accent = "#3B82F6", Selection = "#2D2D30", Hover = "#2D2D30", Error = "#E06C75",
            Warning = "#E5C07B", MissingBadge = "#E5A050", InputBackground = "#2A2A2E",
        },
        new()
        {
            Theme = Theme.CatppuccinMocha, DisplayName = "Catppuccin Mocha", IsDark = true,
            Background = "#1E1E2E", Border = "#45475A", Foreground = "#CDD6F4", SecondaryText = "#9399B2",
            Accent = "#CBA6F7", Selection = "#313244", Hover = "#313244", Error = "#F38BA8",
            Warning = "#F9E2AF", MissingBadge = "#FAB387", InputBackground = "#181825",
        },
        new()
        {
            Theme = Theme.CatppuccinMacchiato, DisplayName = "Catppuccin Macchiato", IsDark = true,
            Background = "#24273A", Border = "#494D64", Foreground = "#CAD3F5", SecondaryText = "#939AB7",
            Accent = "#C6A0F6", Selection = "#363A4F", Hover = "#363A4F", Error = "#ED8796",
            Warning = "#EED49F", MissingBadge = "#F5A97F", InputBackground = "#1E2030",
        },
        new()
        {
            Theme = Theme.CatppuccinFrappe, DisplayName = "Catppuccin Frappé", IsDark = true,
            Background = "#303446", Border = "#51576D", Foreground = "#C6D0F5", SecondaryText = "#949CBB",
            Accent = "#CA9EE6", Selection = "#414559", Hover = "#414559", Error = "#E78284",
            Warning = "#E5C890", MissingBadge = "#EF9F76", InputBackground = "#292C3C",
        },
        new()
        {
            Theme = Theme.CatppuccinLatte, DisplayName = "Catppuccin Latte", IsDark = false,
            Background = "#EFF1F5", Border = "#BCC0CC", Foreground = "#4C4F69", SecondaryText = "#6C6F85",
            Accent = "#8839EF", Selection = "#DCE0E8", Hover = "#E6E9EF", Error = "#D20F39",
            Warning = "#A86A16", MissingBadge = "#C2500A", InputBackground = "#E6E9EF",
        },
        new()
        {
            Theme = Theme.TokyoNight, DisplayName = "Tokyo Night", IsDark = true,
            Background = "#1A1B26", Border = "#414868", Foreground = "#C0CAF5", SecondaryText = "#787C99",
            Accent = "#7AA2F7", Selection = "#292E42", Hover = "#292E42", Error = "#F7768E",
            Warning = "#E0AF68", MissingBadge = "#FF9E64", InputBackground = "#16161E",
        },
        new()
        {
            Theme = Theme.TokyoNightStorm, DisplayName = "Tokyo Night Storm", IsDark = true,
            Background = "#24283B", Border = "#414868", Foreground = "#C0CAF5", SecondaryText = "#8089B3",
            Accent = "#7AA2F7", Selection = "#2F3549", Hover = "#2F3549", Error = "#F7768E",
            Warning = "#E0AF68", MissingBadge = "#FF9E64", InputBackground = "#1F2335",
        },
        new()
        {
            Theme = Theme.TokyoNightDay, DisplayName = "Tokyo Night Day", IsDark = false,
            Background = "#E1E2E7", Border = "#A8AECB", Foreground = "#2E54AC", SecondaryText = "#5A607D",
            Accent = "#2069CF", Selection = "#D3D6E2", Hover = "#DADBE2", Error = "#C2185B",
            Warning = "#7F6136", MissingBadge = "#9E5200", InputBackground = "#E9E9EC",
        },
        new()
        {
            Theme = Theme.Dracula, DisplayName = "Dracula", IsDark = true,
            Background = "#282A36", Border = "#44475A", Foreground = "#F8F8F2", SecondaryText = "#7D8BC0",
            Accent = "#BD93F9", Selection = "#3A3D4E", Hover = "#323442", Error = "#FF5555",
            Warning = "#F1FA8C", MissingBadge = "#FFB86C", InputBackground = "#21222C",
        },
        new()
        {
            Theme = Theme.Nord, DisplayName = "Nord", IsDark = true,
            Background = "#2E3440", Border = "#4C566A", Foreground = "#D8DEE9", SecondaryText = "#8D98AD",
            Accent = "#88C0D0", Selection = "#3B4252", Hover = "#3B4252", Error = "#D57780",
            Warning = "#EBCB8B", MissingBadge = "#D08770", InputBackground = "#272C36",
        },
        new()
        {
            Theme = Theme.GruvboxDark, DisplayName = "Gruvbox Dark", IsDark = true,
            Background = "#282828", Border = "#504945", Foreground = "#EBDBB2", SecondaryText = "#928374",
            Accent = "#83A598", Selection = "#3C3836", Hover = "#3C3836", Error = "#FB4934",
            Warning = "#FABD2F", MissingBadge = "#FE8019", InputBackground = "#1D2021",
        },
        new()
        {
            Theme = Theme.GruvboxLight, DisplayName = "Gruvbox Light", IsDark = false,
            Background = "#FBF1C7", Border = "#D5C4A1", Foreground = "#3C3836", SecondaryText = "#7C6F64",
            Accent = "#076678", Selection = "#F2E5BC", Hover = "#F6EBC4", Error = "#9D0006",
            Warning = "#B57614", MissingBadge = "#AF3A03", InputBackground = "#F9F5D7",
        },
        new()
        {
            Theme = Theme.OneDark, DisplayName = "One Dark", IsDark = true,
            Background = "#282C34", Border = "#3B4048", Foreground = "#ABB2BF", SecondaryText = "#7F848E",
            Accent = "#61AFEF", Selection = "#323842", Hover = "#2C313A", Error = "#E06C75",
            Warning = "#E5C07B", MissingBadge = "#D19A66", InputBackground = "#21252B",
        },
        new()
        {
            Theme = Theme.SolarizedDark, DisplayName = "Solarized Dark", IsDark = true,
            Background = "#002B36", Border = "#586E75", Foreground = "#93A1A1", SecondaryText = "#839496",
            Accent = "#268BD2", Selection = "#073642", Hover = "#073642", Error = "#E0504D",
            Warning = "#B58900", MissingBadge = "#DB5E26", InputBackground = "#00212B",
        },
        new()
        {
            Theme = Theme.SolarizedLight, DisplayName = "Solarized Light", IsDark = false,
            Background = "#FDF6E3", Border = "#93A1A1", Foreground = "#586E75", SecondaryText = "#657B83",
            Accent = "#1F75B3", Selection = "#F4EEDB", Hover = "#F4EEDB", Error = "#C42B28",
            Warning = "#8A6900", MissingBadge = "#B04212", InputBackground = "#FFFDF5",
        },
        new()
        {
            Theme = Theme.Monokai, DisplayName = "Monokai", IsDark = true,
            Background = "#272822", Border = "#49483E", Foreground = "#F8F8F2", SecondaryText = "#90908A",
            Accent = "#66D9EF", Selection = "#37382F", Hover = "#30312A", Error = "#F92672",
            Warning = "#E6DB74", MissingBadge = "#FD971F", InputBackground = "#1E1F1C",
        },
        new()
        {
            Theme = Theme.GithubDark, DisplayName = "GitHub Dark", IsDark = true,
            Background = "#0D1117", Border = "#30363D", Foreground = "#E6EDF3", SecondaryText = "#8D96A0",
            Accent = "#4493F8", Selection = "#21262D", Hover = "#161B22", Error = "#F85149",
            Warning = "#D29922", MissingBadge = "#DB6D28", InputBackground = "#010409",
        },
        new()
        {
            Theme = Theme.GithubLight, DisplayName = "GitHub Light", IsDark = false,
            Background = "#FFFFFF", Border = "#D0D7DE", Foreground = "#1F2328", SecondaryText = "#656D76",
            Accent = "#0969DA", Selection = "#DDF4FF", Hover = "#F3F4F6", Error = "#D1242F",
            Warning = "#9A6700", MissingBadge = "#BC4C00", InputBackground = "#F6F8FA",
        },
        new()
        {
            Theme = Theme.RosePine, DisplayName = "Rosé Pine", IsDark = true,
            Background = "#191724", Border = "#403D52", Foreground = "#E0DEF4", SecondaryText = "#908CAA",
            Accent = "#C4A7E7", Selection = "#26233A", Hover = "#21202E", Error = "#EB6F92",
            Warning = "#F6C177", MissingBadge = "#EBBCBA", InputBackground = "#1F1D2E",
        },
    ];

    /// <summary>
    /// The settings page's list, in display order: System first, then every palette. Computed per call so the names
    /// follow the UI culture: only System and Windows Light/Dark are translated, the rest are brand names.
    /// </summary>
    public static IReadOnlyList<(Theme Theme, string DisplayName)> Choices =>
        [(Theme.System, CoreStrings.ThemeSystem), .. Palettes.Select(p => (p.Theme, DisplayNameOf(p)))];

    /// <summary>The name shown for a palette in the current UI language.</summary>
    public static string DisplayNameOf(ThemePalette palette) => palette.Theme switch
    {
        Theme.Light => CoreStrings.ThemeWindowsLight,
        Theme.Dark => CoreStrings.ThemeWindowsDark,
        _ => palette.DisplayName,
    };

    /// <summary>The palette for a concrete theme. <see cref="Theme.System"/> is not a palette - resolve it to Light/Dark first.</summary>
    public static ThemePalette Get(Theme theme) =>
        Palettes.FirstOrDefault(p => p.Theme == theme)
        ?? throw new ArgumentOutOfRangeException(nameof(theme), theme, "No palette for this theme.");
}
