using System.Text.Json;

namespace YourLauncher.Core.Plugins;

/// <summary>
/// Parses and validates plugin.json. Pure (no IO) so every rejection reason is unit tested. Numbers outside
/// the sane range are clamped rather than rejected: a typo like <c>timeoutMs: 99999999</c> should still give a
/// working plugin, just not one that can hang for hours.
/// </summary>
public static class PluginManifestParser
{
    public static PluginManifestResult Parse(string json, string folderName)
    {
        PluginManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, PluginJsonContext.Default.PluginManifestDto);
        }
        catch (JsonException ex)
        {
            return new PluginManifestResult(null, $"plugin.json is not valid: {ex.Message}");
        }

        if (dto is null)
        {
            return new PluginManifestResult(null, "plugin.json is empty.");
        }

        var keyword = dto.Keyword?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            return new PluginManifestResult(null, "\"keyword\" is required.");
        }

        if (keyword.Any(char.IsWhiteSpace))
        {
            return new PluginManifestResult(null, "\"keyword\" must not contain spaces.");
        }

        var command = dto.Command?.Trim();
        if (string.IsNullOrEmpty(command))
        {
            return new PluginManifestResult(null, "\"command\" is required.");
        }

        if (dto.Args is not null && dto.Args.Any(a => a is null))
        {
            return new PluginManifestResult(null, "\"args\" must be a list of strings.");
        }

        var name = string.IsNullOrWhiteSpace(dto.Name) ? folderName : dto.Name.Trim();
        return new PluginManifestResult(
            new PluginManifest(
                name,
                keyword,
                command,
                dto.Args?.ToArray() ?? [],
                string.IsNullOrWhiteSpace(dto.WorkingDirectory) ? null : dto.WorkingDirectory.Trim(),
                Math.Clamp(dto.TimeoutMs ?? PluginProtocol.DefaultTimeoutMs, PluginProtocol.MinTimeoutMs, PluginProtocol.MaxTimeoutMs),
                Math.Clamp(dto.DebounceMs ?? PluginProtocol.DefaultDebounceMs, 0, PluginProtocol.MaxDebounceMs),
                string.IsNullOrWhiteSpace(dto.Glyph) ? null : dto.Glyph.Trim(),
                string.IsNullOrWhiteSpace(dto.Icon) ? null : dto.Icon.Trim()),
            null);
    }
}
