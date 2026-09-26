using System.Windows;
using Microsoft.Win32;
using YourLauncher.Core.Model;

namespace YourLauncher.App.Services;

/// <summary>
/// Resolves <see cref="Theme"/> to an actual dark/light choice and swaps which of
/// <c>Themes/Dark.xaml</c>/<c>Themes/Light.xaml</c> is merged into <see cref="Application.Resources"/>
/// (spec §11/§10 item 2-4). Every color in the app is looked up through one of those two dictionaries'
/// shared keys (XAML <c>{DynamicResource}</c>, or <c>SetResourceReference</c> in code-behind for a value
/// decided at runtime), so this swap alone re-themes the whole panel - no other code needs to touch
/// individual brushes when the theme changes.
/// </summary>
public sealed class ThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private ResourceDictionary? _activeDictionary;

    /// <summary>The last-resolved effective theme (true = dark), kept for callers that need it outside the Apply call itself (e.g. MainWindow re-applying the DWM dark-mode flag after a later HWND-level change).</summary>
    public bool IsDark { get; private set; } = true;

    /// <summary>
    /// Resolves <paramref name="theme"/> (System reads the registry) and swaps the merged theme
    /// dictionary accordingly. Returns the resolved dark/light choice.
    /// </summary>
    public bool Apply(Theme theme)
    {
        var dark = Resolve(theme);
        IsDark = dark;

        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = uri };

        var merged = Application.Current!.Resources.MergedDictionaries;
        if (_activeDictionary is not null)
        {
            merged.Remove(_activeDictionary);
        }

        merged.Add(dictionary);
        _activeDictionary = dictionary;
        return dark;
    }

    /// <summary>Theme.System reads the live registry value each call (no caching) - callers decide when re-resolving is worthwhile (e.g. a WM_SETTINGCHANGE notification).</summary>
    public static bool Resolve(Theme theme) => theme switch
    {
        Theme.Dark => true,
        Theme.Light => false,
        _ => IsSystemThemeDark(),
    };

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
