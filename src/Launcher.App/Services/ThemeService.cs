using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Windows.UI.ViewManagement;
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

    // What the last Apply was called with, so Reapply can redo it when Windows' colors change while the
    // settings page is previewing something other than the saved config (the saved config would be wrong then).
    private Theme _lastTheme = Theme.System;
    private int _lastOpacity = 90;
    private bool _lastUseWindowsAccent;

    /// <summary>True when what is currently applied depends on a Windows setting (System theme or Windows accent), i.e. a Windows color change should trigger <see cref="Reapply"/>.</summary>
    public bool FollowsWindowsColors => _lastTheme == Theme.System || _lastUseWindowsAccent;

    /// <summary>Re-applies the last-applied theme/opacity/accent (re-reading the Windows values) and returns whether the palette is dark.</summary>
    public bool Reapply() => Apply(_lastTheme, _lastOpacity, _lastUseWindowsAccent);

    /// <summary>
    /// Resolves <paramref name="theme"/> (System reads the registry) and swaps the merged palette
    /// dictionary accordingly; <paramref name="opacity"/> (percent, clamped to
    /// <see cref="Settings.MinOpacity"/>-100) becomes the alpha of <c>PanelBackgroundTintBrush</c>, the
    /// panel background used over the Mica/Acrylic backdrop. With <paramref name="useWindowsAccent"/> the
    /// palette's accent is replaced by the Windows accent color (falls back to the palette accent if it can't
    /// be read). Returns whether the palette is dark.
    /// </summary>
    public bool Apply(Theme theme, int opacity, bool useWindowsAccent = false)
    {
        _lastTheme = theme;
        _lastOpacity = opacity;
        _lastUseWindowsAccent = useWindowsAccent;
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
            ["AccentBrush"] = Brush(useWindowsAccent && TryGetWindowsAccent(palette.IsDark) is { } windowsAccent ? windowsAccent : Parse(palette.Accent)),
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

    /// <summary>
    /// The Windows accent color via WinRT UISettings. The raw accent is often too dark or too light against
    /// the panel, so the theme-matched shade is used: AccentDark1 on light palettes and AccentLight2 on
    /// dark ones (the same variants Windows' own apps pick for text/links on each background). Null on any
    /// failure (unsupported OS, no WinRT) so the caller keeps the palette accent.
    /// </summary>
    private static Color? TryGetWindowsAccent(bool darkPalette)
    {
        try
        {
            var c = new UISettings().GetColorValue(darkPalette ? UIColorType.AccentLight2 : UIColorType.AccentDark1);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch (Exception)
        {
            return null;
        }
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
