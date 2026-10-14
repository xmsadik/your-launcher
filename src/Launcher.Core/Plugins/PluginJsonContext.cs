using System.Text.Json;
using System.Text.Json.Serialization;

namespace YourLauncher.Core.Plugins;

/// <summary>
/// Source-generated (trim/AOT-safe) reader for plugin.json and plugin output, same pattern as
/// <c>LauncherJsonContext</c>. Tolerant on purpose - plugin authors hand-write JSON: comments and trailing
/// commas are accepted, names are case-insensitive, and unknown fields are ignored so a newer plugin
/// does not break on an older launcher. Reading goes into nullable DTOs; the real validation (and the
/// readable error messages) live in <see cref="PluginManifestParser"/> and <see cref="PluginOutputParser"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(PluginManifestDto))]
[JsonSerializable(typeof(PluginOutputDto))]
[JsonSerializable(typeof(PluginRequestDto))]
internal partial class PluginJsonContext : JsonSerializerContext
{
}

internal sealed class PluginManifestDto
{
    public string? Name { get; set; }
    public string? Keyword { get; set; }
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
    public string? WorkingDirectory { get; set; }
    public int? TimeoutMs { get; set; }
    public int? DebounceMs { get; set; }
    public string? Glyph { get; set; }
    public string? Icon { get; set; }
}

internal sealed class PluginOutputDto
{
    public List<PluginItemDto?>? Items { get; set; }
}

internal sealed class PluginItemDto
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Icon { get; set; }
    public PluginActionDto? Action { get; set; }
}

internal sealed class PluginActionDto
{
    public string? Type { get; set; }
    public string? Target { get; set; }
    public string? Text { get; set; }
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
}

/// <summary>What the launcher writes to the plugin's stdin (one line).</summary>
internal sealed class PluginRequestDto
{
    public string Query { get; set; } = "";
    public int Version { get; set; } = PluginProtocol.Version;
}
