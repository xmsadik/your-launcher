using System.Text.Json.Serialization;

namespace YourLauncher.Core.Model;

/// <summary>Contains other nodes; may be nested to arbitrary depth (spec §4.1).</summary>
public sealed class FolderNode : Node
{
    /// <summary>Written on serialize; read via <see cref="Json.NodeJsonConverter"/>, not this property (which has no setter).</summary>
    [JsonPropertyName("type")]
    [JsonPropertyOrder(-2)]
    public string Type => "folder";

    [JsonPropertyOrder(20)]
    public List<Node> Children { get; set; } = new();
}
