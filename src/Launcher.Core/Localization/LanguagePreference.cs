using System.Globalization;
using System.Text.Json;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Localization;

/// <summary>
/// Resolves <c>settings.language</c> to a UI culture. The app needs the language before the config is loaded
/// (the first error line may already be text), so <see cref="Peek"/> reads just that one value from the raw
/// config.json and never fails: anything unreadable or unknown means <see cref="AppLanguage.System"/>.
/// </summary>
public static class LanguagePreference
{
    /// <summary>The UI culture to force, or null to leave the operating system's UI culture alone.</summary>
    public static CultureInfo? CultureFor(AppLanguage language) => language switch
    {
        AppLanguage.En => CultureInfo.GetCultureInfo("en"),
        AppLanguage.Tr => CultureInfo.GetCultureInfo("tr"),
        _ => null,
    };

    /// <summary>Reads <c>settings.language</c> from config.json text; <see cref="AppLanguage.System"/> for anything else.</summary>
    public static AppLanguage Peek(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AppLanguage.System;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("settings", out var settings)
                && settings.ValueKind == JsonValueKind.Object
                && settings.TryGetProperty("language", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                foreach (var candidate in Enum.GetValues<AppLanguage>())
                {
                    if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return AppLanguage.System;
    }
}
