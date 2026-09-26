using System.Text.Json.Serialization;

namespace YourLauncher.Core.Model;

/// <summary>A file or directory path, opened with the default associated application. Spec §4.3.</summary>
public sealed class PathNode : Node
{
    [JsonPropertyName("type")]
    [JsonPropertyOrder(-2)]
    public string Type => "path";

    public string Target { get; set; } = "";
}
