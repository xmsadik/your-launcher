using System.Text.Json.Serialization;

namespace YourLauncher.Core.Model;

/// <summary>A web address, opened in the default browser. Spec §4.3.</summary>
public sealed class UrlNode : Node
{
    [JsonPropertyName("type")]
    [JsonPropertyOrder(-2)]
    public string Type => "url";

    public string Target { get; set; } = "";
}
