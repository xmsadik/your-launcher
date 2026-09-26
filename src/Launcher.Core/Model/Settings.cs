using System.Text.Json.Serialization;
using YourLauncher.Core.Json;

namespace YourLauncher.Core.Model;

public enum Theme
{
    System,
    Light,
    Dark,
}

/// <summary>Application settings, spec §4.5 / §11.</summary>
public sealed class Settings
{
    public string Hotkey { get; set; } = "Alt+Space";

    [JsonConverter(typeof(JsonCamelCaseEnumConverter<Theme>))]
    public Theme Theme { get; set; } = Theme.System;

    public bool StartWithWindows { get; set; } = true;

    public bool CloseAfterLaunch { get; set; } = true;

    public int MaxVisibleItems { get; set; } = 8;

    [JsonConverter(typeof(JsonCamelCaseEnumConverter<ShellKind>))]
    public ShellKind DefaultShell { get; set; } = ShellKind.Pwsh;

    public bool RememberLastLocation { get; set; } = false;

    public bool ShowHintBar { get; set; } = true;
}
