using System.Globalization;
using System.IO;
using YourLauncher.Core.Localization;
using YourLauncher.Core.Model;

namespace YourLauncher.App.Services;

/// <summary>
/// Applies <c>settings.language</c> as the process UI culture. It runs once, first thing in <c>OnStartup</c>
/// and before anything creates a string, so a language change saved on the settings page takes effect on the
/// next start (<see cref="Applied"/> is what the running process was started with). "System" leaves the
/// operating system's UI culture alone: the Turkish resources are picked only when Windows itself is Turkish,
/// every other UI language falls back to the neutral (English) resources.
/// </summary>
public static class LanguageService
{
    /// <summary>The language this process was started with.</summary>
    public static AppLanguage Applied { get; private set; } = AppLanguage.System;

    /// <summary>Reads the language from <c>config.json</c> in <paramref name="configDirectory"/> (without loading the config) and applies it.</summary>
    public static void Apply(string configDirectory)
    {
        string? json = null;
        try
        {
            json = File.ReadAllText(Path.Combine(configDirectory, "config.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No file yet (first run) or unreadable: the default language applies; ConfigService reports the real problem.
        }

        Apply(LanguagePreference.Peek(json));
    }

    public static void Apply(AppLanguage language)
    {
        Applied = language;
        if (LanguagePreference.CultureFor(language) is { } culture)
        {
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
