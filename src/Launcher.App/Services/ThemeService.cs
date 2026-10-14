using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using YourLauncher.Core.Model;
using YourLauncher.Core.Theming;

namespace YourLauncher.App.Services;

/// <summary>
/// Resolves <see cref="Theme"/> to a <see cref="ThemePalette"/> and swaps the palette dictionary merged
/// into <see cref="Application.Resources"/> (spec §11/§10 item 2-4). Every color in the app is looked up
/// through one of the palette's shared keys (XAML <c>{DynamicResource}</c>, or <c>SetResourceReference</c>
/// in code-behind for a value decided at runtime; the shared control chrome in <c>Themes/Controls.xaml</c>
/// does the same), so this swap alone re-themes the whole panel - no other code needs to touch individual
/// brushes when the theme changes.
/// </summary>
public sealed class ThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private ResourceDictionary? _activeDictionary;

    /// <summary>The last-resolved effective theme (true = dark), kept for callers that need it outside the Apply call itself (e.g. MainWindow re-applying the DWM dark-mode flag after a later HWND-level change).</summary>
    public bool IsDark { get; private set; } = true;

    /// <summary>
    /// Resolves <paramref name="theme"/> (System reads the registry) and swaps the merged palette
    /// dictionary accordingly; <paramref name="opacity"/> (percent, clamped to
    /// <see cref="Settings.MinOpacity"/>-100) becomes the alpha of <c>PanelBackgroundTintBrush</c>, the
    /// panel background used over the Mica/Acrylic backdrop. Returns whether the palette is dark.
    /// </summary>
    public bool Apply(Theme theme, int opacity)
    {
        var palette = ThemeCatalog.Get(theme == Theme.System ? (IsSystemThemeDark() ? Theme.Dark : Theme.Light) : theme);
        IsDark = palette.IsDark;

        var alpha = (byte)Math.Round(Math.Clamp(opacity, Settings.MinOpacity, 100) * 255 / 100.0);
        var background = Parse(palette.Background);

        var dictionary = new ResourceDictionary
        {
            ["PanelBackgroundBrush"] = Brush(background),
            ["PanelBackgroundTintBrush"] = Brush(Color.FromArgb(alpha, background.R, background.G, background.B)),
            ["PanelBorderBrush"] = Brush(Parse(palette.Border)),
            ["ForegroundBrush"] = Brush(Parse(palette.Foreground)),
            ["SecondaryTextBrush"] = Brush(Parse(palette.SecondaryText)),
            ["AccentBrush"] = Brush(Parse(palette.Accent)),
            ["SelectionBackgroundBrush"] = Brush(Parse(palette.Selection)),
            ["HoverBackgroundBrush"] = Brush(Parse(palette.Hover)),
            ["ErrorBrush"] = Brush(Parse(palette.Error)),
            ["WarningBrush"] = Brush(Parse(palette.Warning)),
            ["MissingBadgeBrush"] = Brush(Parse(palette.MissingBadge)),
            ["InputBackgroundBrush"] = Brush(Parse(palette.InputBackground)),
        };

        var merged = Application.Current!.Resources.MergedDictionaries;
        if (_activeDictionary is not null)
        {
            merged.Remove(_activeDictionary);
        }

        merged.Add(dictionary);
        _activeDictionary = dictionary;
        return palette.IsDark;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>AppsUseLightTheme: 1 = light apps (default), 0 = dark apps. A missing value means light - the Windows default - not dark (spec §10 item 2/4).</summary>
    private static bool IsSystemThemeDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            return key?.GetValue(AppsUseLightThemeValue) is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or System.IO.IOException)
        {
            return false; // treat an unreadable registry the same as "missing" - light.
        }
    }
}
