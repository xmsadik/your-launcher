using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using YourLauncher.Core.Config;
using YourLauncher.Core.Localization;
using YourLauncher.Core.Model;
using YourLauncher.Core.Plugins;
using YourLauncher.Core.Theming;

namespace YourLauncher.Core.Tests.Localization;

/// <summary>
/// The Turkish resources are only useful if they stay in step with the English ones. The App project cannot be
/// referenced from here, so these tests read the .resx files (and the accessor sources) straight from the
/// repository, found by walking up from the test assembly to the solution file.
/// </summary>
public partial class LocalizationTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "YourLauncher.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("YourLauncher.slnx not found above " + AppContext.BaseDirectory);
        }
    }

    private static string AppDir => Path.Combine(RepoRoot, "src", "Launcher.App", "Localization");

    private static string CoreDir => Path.Combine(RepoRoot, "src", "Launcher.Core", "Localization");

    public static TheoryData<string, string> ResourcePairs() => new()
    {
        { Path.Combine("src", "Launcher.App", "Localization", "Strings.resx"), Path.Combine("src", "Launcher.App", "Localization", "Strings.tr.resx") },
        { Path.Combine("src", "Launcher.Core", "Localization", "CoreStrings.resx"), Path.Combine("src", "Launcher.Core", "Localization", "CoreStrings.tr.resx") },
    };

    private static Dictionary<string, string> ReadResx(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    private static string[] Placeholders(string value) =>
        Placeholder().Matches(value).Select(m => m.Value).Order().ToArray();

    [Theory]
    [MemberData(nameof(ResourcePairs))]
    public void TurkishResx_HasExactlyTheEnglishKeys(string english, string turkish)
    {
        var en = ReadResx(Path.Combine(RepoRoot, english));
        var tr = ReadResx(Path.Combine(RepoRoot, turkish));

        Assert.Empty(en.Keys.Except(tr.Keys));
        Assert.Empty(tr.Keys.Except(en.Keys));
    }

    [Theory]
    [MemberData(nameof(ResourcePairs))]
    public void TurkishResx_KeepsPlaceholdersAndFileFilters(string english, string turkish)
    {
        var en = ReadResx(Path.Combine(RepoRoot, english));
        var tr = ReadResx(Path.Combine(RepoRoot, turkish));

        foreach (var (key, englishValue) in en)
        {
            var turkishValue = tr[key];
            Assert.False(string.IsNullOrWhiteSpace(turkishValue), $"{key}: empty Turkish value");
            Assert.True(
                Placeholders(englishValue).SequenceEqual(Placeholders(turkishValue)),
                $"{key}: placeholders differ ('{englishValue}' vs '{turkishValue}')");

            // OpenFileDialog filters are "label|pattern|label|pattern": a lost '|' would break the dialog.
            if (key.StartsWith("Filter", StringComparison.Ordinal))
            {
                Assert.Equal(englishValue.Count(c => c == '|'), turkishValue.Count(c => c == '|'));
                Assert.Equal(
                    englishValue.Split('|').Where((_, i) => i % 2 == 1),
                    turkishValue.Split('|').Where((_, i) => i % 2 == 1));
            }

            // Both values must also be usable as format strings (an unescaped brace would throw at runtime).
            if (!englishValue.Contains("{query}", StringComparison.Ordinal))
            {
                var args = new object[] { "a", "b", "c", "d" };
                _ = string.Format(CultureInfo.InvariantCulture, englishValue, args);
                _ = string.Format(CultureInfo.InvariantCulture, turkishValue, args);
            }
        }
    }

    [Theory]
    [InlineData("Strings.cs", "Strings.resx", "Glyph")]
    [InlineData("CoreStrings.cs", "CoreStrings.resx", "")]
    public void Accessors_MatchTheResourceKeys(string accessorFile, string resx, string skipPrefix)
    {
        var dir = accessorFile == "Strings.cs" ? AppDir : CoreDir;
        var accessors = Regex.Matches(File.ReadAllText(Path.Combine(dir, accessorFile)), @"Get\(nameof\((\w+)\)\)")
            .Select(m => m.Groups[1].Value).ToList();
        var keys = ReadResx(Path.Combine(dir, resx)).Keys;

        Assert.Empty(accessors.Except(keys));
        Assert.Equal(accessors.Count, accessors.Distinct().Count());
        Assert.Empty(keys.Where(k => skipPrefix.Length == 0 || !k.StartsWith(skipPrefix, StringComparison.Ordinal)).Except(accessors));
    }

    [Fact]
    public void GlyphNames_AllResolveToResourceKeys()
    {
        var keys = ReadResx(Path.Combine(AppDir, "Strings.resx")).Keys.Where(k => k.StartsWith("Glyph", StringComparison.Ordinal)).ToHashSet();
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "Launcher.App", "ViewModels", "IconPickerViewModel.cs"));
        var used = Regex.Matches(source, @"Strings\.GlyphName\(""(\w+)""\)").Select(m => "Glyph" + m.Groups[1].Value).ToList();

        Assert.NotEmpty(used);
        Assert.Empty(used.Except(keys));
        Assert.Empty(keys.Except(used)); // no orphaned glyph names
    }

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.tr.resx")]
    public void TypePickerKeyHints_AreDistinctLetters(string file)
    {
        var values = ReadResx(Path.Combine(AppDir, file));
        var hints = values.Where(kv => kv.Key.StartsWith("Type", StringComparison.Ordinal) && kv.Key.EndsWith("Key", StringComparison.Ordinal))
            .Select(kv => kv.Value).ToList();

        Assert.Equal(6, hints.Count);
        Assert.All(hints, h => Assert.Matches("^[A-Z]$", h));
        Assert.Equal(hints.Count, hints.Distinct().Count());
    }

    // ---- Core strings follow the UI culture ----

    private static T WithUiCulture<T>(string culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void CoreStrings_FollowTheUiCulture()
    {
        Assert.Equal("Windows Light", WithUiCulture("en", () => CoreStrings.ThemeWindowsLight));
        Assert.Equal("Windows Açık", WithUiCulture("tr", () => CoreStrings.ThemeWindowsLight));
        Assert.Equal("Windows Açık", WithUiCulture("tr-TR", () => CoreStrings.ThemeWindowsLight));
        Assert.Equal("Windows Light", WithUiCulture("de-DE", () => CoreStrings.ThemeWindowsLight)); // anything else falls back to English
    }

    [Fact]
    public void ThemeChoices_TranslateOnlySystemAndWindowsNames()
    {
        var en = WithUiCulture("en", () => ThemeCatalog.Choices.ToList());
        var tr = WithUiCulture("tr", () => ThemeCatalog.Choices.ToList());

        Assert.Equal("System (Windows Light/Dark)", en[0].DisplayName);
        Assert.Equal("Sistem (Windows Açık/Koyu)", tr[0].DisplayName);
        Assert.Equal("Windows Koyu", tr.Single(c => c.Theme == Theme.Dark).DisplayName);
        Assert.Equal(en.Count, tr.Count);

        // Everything else is a brand name and must not change.
        foreach (var (theme, name) in en.Where(c => c.Theme is not (Theme.System or Theme.Light or Theme.Dark)))
        {
            Assert.Equal(name, tr.Single(c => c.Theme == theme).DisplayName);
        }
    }

    [Fact]
    public void PluginManifestErrors_AreLocalized()
    {
        var english = WithUiCulture("en", () => PluginManifestParser_Error("{}"));
        var turkish = WithUiCulture("tr", () => PluginManifestParser_Error("{}"));

        Assert.Equal("\"keyword\" is required.", english);
        Assert.Equal("\"keyword\" gerekli.", turkish);
    }

    private static string PluginManifestParser_Error(string json) =>
        PluginManifestParser.Parse(json, "demo").Error!;

    // ---- settings.language ----

    [Fact]
    public void Language_DefaultsToSystem_AndRoundTripsAsCamelCase()
    {
        Assert.Equal(AppLanguage.System, new Settings().Language);

        foreach (var (language, text) in new[] { (AppLanguage.System, "system"), (AppLanguage.En, "en"), (AppLanguage.Tr, "tr") })
        {
            var config = new LauncherConfig { Settings = new Settings { Language = language } };
            var json = ConfigSerializer.Serialize(config);
            Assert.Contains($"\"language\": \"{text}\"", json);

            var loaded = ConfigSerializer.Deserialize(json);
            Assert.True(loaded.Success);
            Assert.Equal(language, loaded.Config!.Settings.Language);
        }
    }

    [Fact]
    public void Language_MissingFromOlderConfigs_IsSystem()
    {
        var loaded = ConfigSerializer.Deserialize("""{ "version": 1, "settings": { "hotkey": "Alt+Space" }, "root": { "id": "root", "type": "folder", "name": "Root", "children": [] } }""");

        Assert.True(loaded.Success);
        Assert.Equal(AppLanguage.System, loaded.Config!.Settings.Language);
    }

    [Theory]
    [InlineData("""{ "settings": { "language": "tr" } }""", AppLanguage.Tr)]
    [InlineData("""{ "settings": { "language": "EN" } }""", AppLanguage.En)]
    [InlineData("""{ "settings": { "language": "system" } }""", AppLanguage.System)]
    [InlineData("""{ "settings": { "language": "klingon" } }""", AppLanguage.System)]
    [InlineData("""{ "settings": { "language": 3 } }""", AppLanguage.System)]
    [InlineData("""{ "settings": {} }""", AppLanguage.System)]
    [InlineData("""[1, 2]""", AppLanguage.System)]
    [InlineData("{ not json", AppLanguage.System)]
    [InlineData("", AppLanguage.System)]
    [InlineData(null, AppLanguage.System)]
    public void LanguagePreference_Peek_IsTolerant(string? json, AppLanguage expected) =>
        Assert.Equal(expected, LanguagePreference.Peek(json));

    [Fact]
    public void LanguagePreference_CultureFor_LeavesSystemAlone()
    {
        Assert.Null(LanguagePreference.CultureFor(AppLanguage.System));
        Assert.Equal("en", LanguagePreference.CultureFor(AppLanguage.En)!.Name);
        Assert.Equal("tr", LanguagePreference.CultureFor(AppLanguage.Tr)!.Name);
    }

    [Fact]
    public void LanguagePreference_Peek_AgreesWithTheRealSerializer()
    {
        var config = new LauncherConfig { Settings = new Settings { Language = AppLanguage.Tr } };
        Assert.Equal(AppLanguage.Tr, LanguagePreference.Peek(ConfigSerializer.Serialize(config)));
        _ = JsonDocument.Parse(ConfigSerializer.Serialize(config)); // still valid JSON
    }
}
