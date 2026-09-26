using System.Text.Json.Serialization;
using YourLauncher.Core.Json;

namespace YourLauncher.Core.Model;

public enum ShellKind
{
    Pwsh,
    Powershell,
    Cmd,
}

public enum WindowMode
{
    Visible,
    Hidden,
}

/// <summary>A shell command, run via pwsh, powershell or cmd. Spec §4.3, §8.2.</summary>
public sealed class CommandNode : Node
{
    [JsonPropertyName("type")]
    [JsonPropertyOrder(-2)]
    public string Type => "command";

    public string Command { get; set; } = "";

    /// <summary>Null means "resolve from settings.defaultShell" (spec §4.3) at launch time.</summary>
    [JsonConverter(typeof(JsonCamelCaseEnumConverter<ShellKind>))]
    public ShellKind? Shell { get; set; }

    public string? WorkingDirectory { get; set; }

    [JsonConverter(typeof(JsonCamelCaseEnumConverter<WindowMode>))]
    public WindowMode Window { get; set; } = WindowMode.Visible;

    /// <summary>Only meaningful when <see cref="Window"/> is <see cref="WindowMode.Visible"/>.</summary>
    public bool KeepOpen { get; set; } = true;

    public bool RunAsAdmin { get; set; }
}
