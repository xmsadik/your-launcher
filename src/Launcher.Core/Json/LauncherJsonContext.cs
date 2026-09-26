using System.Text.Json;
using System.Text.Json.Serialization;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Json;

/// <summary>
/// Source-generated serializer context for config.json (spec §4.5). CamelCase property names,
/// tolerant reading (comments + trailing commas allowed, per decision D5), indented writing.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(LauncherConfig))]
[JsonSerializable(typeof(Node))]
[JsonSerializable(typeof(FolderNode))]
[JsonSerializable(typeof(AppNode))]
[JsonSerializable(typeof(PathNode))]
[JsonSerializable(typeof(CommandNode))]
[JsonSerializable(typeof(UrlNode))]
public partial class LauncherJsonContext : JsonSerializerContext
{
}
