using System.Text.Json.Serialization;

namespace YourLauncher.Core.Model;

/// <summary>An executable file (.exe, .lnk, .bat, .cmd, .msc, ...). Spec §4.3.</summary>
public sealed class AppNode : Node
{
    [JsonPropertyName("type")]
    [JsonPropertyOrder(-2)]
    public string Type => "app";

    public string Target { get; set; } = "";
    public string? Arguments { get; set; }

    /// <summary>Null means "directory of <see cref="Target"/>" at launch time.</summary>
    public string? WorkingDirectory { get; set; }

    public bool RunAsAdmin { get; set; }
}
