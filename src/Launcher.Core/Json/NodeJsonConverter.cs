using System.Text.Json;
using System.Text.Json.Serialization;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Json;

/// <summary>
/// Hand-written polymorphic converter for <see cref="Node"/>, used instead of System.Text.Json's
/// built-in [JsonPolymorphic]/[JsonDerivedType] discriminator mechanism because that mechanism's
/// source-generated implementation only recognizes the discriminator when "type" is the *first*
/// property in the JSON object. config.json is hand-edited (spec §4.5) and the spec's own examples put
/// "id" before "type" (§4.2), so parsing must not depend on property order. This converter buffers each
/// node as a <see cref="JsonDocument"/>, reads "type" from wherever it appears, then deserializes with
/// the matching concrete type's source-generated <see cref="JsonTypeInfo{T}"/>.
/// </summary>
public sealed class NodeJsonConverter : JsonConverter<Node>
{
    public override Node? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;

        if (!element.TryGetProperty("type", out var typeProperty) || typeProperty.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Node is missing a \"type\" discriminator.");
        }

        var raw = element.GetRawText();
        return typeProperty.GetString() switch
        {
            "folder" => JsonSerializer.Deserialize(raw, LauncherJsonContext.Default.FolderNode),
            "app" => JsonSerializer.Deserialize(raw, LauncherJsonContext.Default.AppNode),
            "path" => JsonSerializer.Deserialize(raw, LauncherJsonContext.Default.PathNode),
            "command" => JsonSerializer.Deserialize(raw, LauncherJsonContext.Default.CommandNode),
            "url" => JsonSerializer.Deserialize(raw, LauncherJsonContext.Default.UrlNode),
            "separator" => JsonSerializer.Deserialize(raw, LauncherJsonContext.Default.SeparatorNode),
            var other => throw new JsonException($"Unknown node type '{other}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, Node value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case FolderNode folder:
                JsonSerializer.Serialize(writer, folder, LauncherJsonContext.Default.FolderNode);
                break;
            case AppNode app:
                JsonSerializer.Serialize(writer, app, LauncherJsonContext.Default.AppNode);
                break;
            case PathNode path:
                JsonSerializer.Serialize(writer, path, LauncherJsonContext.Default.PathNode);
                break;
            case CommandNode command:
                JsonSerializer.Serialize(writer, command, LauncherJsonContext.Default.CommandNode);
                break;
            case UrlNode url:
                JsonSerializer.Serialize(writer, url, LauncherJsonContext.Default.UrlNode);
                break;
            case SeparatorNode separator:
                JsonSerializer.Serialize(writer, separator, LauncherJsonContext.Default.SeparatorNode);
                break;
            default:
                throw new JsonException($"Unknown node type '{value.GetType()}'.");
        }
    }
}
