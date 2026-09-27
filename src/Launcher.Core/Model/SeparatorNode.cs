using System.Text.Json.Serialization;

namespace YourLauncher.Core.Model;

/// <summary>A horizontal divider line between items in a folder. Not launchable, editable or searchable; Name is normally empty.</summary>
public sealed class SeparatorNode : Node
{
    [JsonPropertyName("type")]
    [JsonPropertyOrder(-2)]
    public string Type => "separator";
}
