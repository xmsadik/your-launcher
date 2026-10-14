using System.Globalization;
using YourLauncher.Core.Config;
using YourLauncher.Core.Model;
using YourLauncher.Core.Theming;

namespace YourLauncher.Core.Tests;

public class ThemeCatalogTests
{
    public static TheoryData<Theme> PaletteThemes() => [.. ThemeCatalog.Palettes.Select(p => p.Theme)];

    [Fact]
    public void EveryThemeExceptSystem_HasExactlyOnePalette()
    {
        var expected = Enum.GetValues<Theme>().Where(t => t != Theme.System).Order();
        Assert.Equal(expected, ThemeCatalog.Palettes.Select(p => p.Theme).Order());
    }

    [Fact]
    public void Choices_StartWithSystem_ThenWindowsLightAndDark_AndNamesAreUnique()
    {
        Assert.Equal([Theme.System, Theme.Light, Theme.Dark], ThemeCatalog.Choices.Take(3).Select(c => c.Theme));
        Assert.Equal(ThemeCatalog.Choices.Count, ThemeCatalog.Choices.Select(c => c.DisplayName).Distinct().Count());
    }

    [Fact]
    public void Get_System_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ThemeCatalog.Get(Theme.System));
    }

    [Theory]
    [MemberData(nameof(PaletteThemes))]
    public void Palette_ColorsAreRgbHex(Theme theme)
    {
        foreach (var (name, hex) in Colors(ThemeCatalog.Get(theme)))
        {
            Assert.Matches("^#[0-9A-F]{6}$", hex);
            Assert.False(string.IsNullOrEmpty(name));
        }
    }

    /// <summary>Body text must meet WCAG AA (4.5:1); secondary/accent/status text at least 3:1 - on both the panel background and the selected row.</summary>
    [Theory]
    [MemberData(nameof(PaletteThemes))]
    public void Palette_TextIsReadable_OnBackgroundAndSelection(Theme theme)
    {
        var p = ThemeCatalog.Get(theme);
        foreach (var surface in new[] { p.Background, p.Selection })
        {
            AssertContrast(p.Foreground, surface, 4.5, theme, nameof(p.Foreground));
            AssertContrast(p.SecondaryText, surface, 3.0, theme, nameof(p.SecondaryText));
            AssertContrast(p.Accent, surface, 3.0, theme, nameof(p.Accent));
            AssertContrast(p.Error, surface, 3.0, theme, nameof(p.Error));
            AssertContrast(p.Warning, surface, 3.0, theme, nameof(p.Warning));
            AssertContrast(p.MissingBadge, surface, 3.0, theme, nameof(p.MissingBadge));
        }
    }

    [Theory]
    [MemberData(nameof(PaletteThemes))]
    public void Palette_IsDarkMatchesBackground(Theme theme)
    {
        var p = ThemeCatalog.Get(theme);
        Assert.Equal(p.IsDark, Luminance(p.Background) < 0.2);
    }

    [Fact]
    public void Settings_NewThemeAndOpacity_RoundTripThroughConfig()
    {
        var config = new LauncherConfig();
        config.Settings.Theme = Theme.CatppuccinMocha;
        config.Settings.Opacity = 75;

        var json = ConfigSerializer.Serialize(config);
        Assert.Contains("\"theme\": \"catppuccinMocha\"", json);
        Assert.Contains("\"opacity\": 75", json);

        var reloaded = ConfigSerializer.Deserialize(json);
        Assert.True(reloaded.Success);
        Assert.Equal(Theme.CatppuccinMocha, reloaded.Config!.Settings.Theme);
        Assert.Equal(75, reloaded.Config.Settings.Opacity);
    }

    [Fact]
    public void Settings_IncludeOtherDesktopWindows_DefaultsOffAndRoundTrips()
    {
        var missing = ConfigSerializer.Deserialize("""{ "version": 1, "settings": {}, "root": { "id": "root", "type": "folder", "name": "Root", "children": [] } }""");
        Assert.True(missing.Success);
        Assert.False(missing.Config!.Settings.IncludeOtherDesktopWindows);

        var config = new LauncherConfig();
        config.Settings.IncludeOtherDesktopWindows = true;
        var json = ConfigSerializer.Serialize(config);
        Assert.Contains("\"includeOtherDesktopWindows\": true", json);
        Assert.True(ConfigSerializer.Deserialize(json).Config!.Settings.IncludeOtherDesktopWindows);
    }

    [Fact]
    public void Settings_ConfigWithoutOpacity_DefaultsTo90()
    {
        var result = ConfigSerializer.Deserialize("""{ "version": 1, "settings": { "theme": "tokyoNight" }, "root": { "id": "root", "type": "folder", "name": "Root", "children": [] } }""");

        Assert.True(result.Success);
        Assert.Equal(Theme.TokyoNight, result.Config!.Settings.Theme);
        Assert.Equal(90, result.Config.Settings.Opacity);
    }

    [Fact]
    public void Settings_UseWindowsAccent_DefaultsToFalseAndRoundTrips()
    {
        Assert.False(new Settings().UseWindowsAccent);

        var missing = ConfigSerializer.Deserialize("""{ "version": 1, "settings": { "opacity": 80 }, "root": { "id": "root", "type": "folder", "name": "Root", "children": [] } }""");
        Assert.True(missing.Success);
        Assert.False(missing.Config!.Settings.UseWindowsAccent);

        var config = new LauncherConfig();
        config.Settings.UseWindowsAccent = true;
        var json = ConfigSerializer.Serialize(config);
        Assert.Contains("\"useWindowsAccent\": true", json);
        Assert.True(ConfigSerializer.Deserialize(json).Config!.Settings.UseWindowsAccent);
    }

    private static IEnumerable<(string Name, string Hex)> Colors(ThemePalette p) =>
    [
        (nameof(p.Background), p.Background), (nameof(p.Border), p.Border), (nameof(p.Foreground), p.Foreground),
        (nameof(p.SecondaryText), p.SecondaryText), (nameof(p.Accent), p.Accent), (nameof(p.Selection), p.Selection),
        (nameof(p.Hover), p.Hover), (nameof(p.Error), p.Error), (nameof(p.Warning), p.Warning),
        (nameof(p.MissingBadge), p.MissingBadge), (nameof(p.InputBackground), p.InputBackground),
    ];

    private static void AssertContrast(string foreground, string background, double minimum, Theme theme, string role)
    {
        var l1 = Luminance(foreground);
        var l2 = Luminance(background);
        var ratio = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        Assert.True(ratio >= minimum, $"{theme}.{role} {foreground} on {background}: {ratio:F2} < {minimum}");
    }

    /// <summary>WCAG relative luminance.</summary>
    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            var c = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
