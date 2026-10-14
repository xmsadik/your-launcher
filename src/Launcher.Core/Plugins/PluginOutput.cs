namespace YourLauncher.Core.Plugins;

/// <summary>What happens when a plugin row is chosen.</summary>
public enum PluginActionKind
{
    /// <summary>Shell-open <see cref="PluginAction.Target"/> (URL, file or folder).</summary>
    Open,

    /// <summary>Put <see cref="PluginAction.Text"/> on the clipboard.</summary>
    Copy,

    /// <summary>Start <see cref="PluginAction.Command"/> with <see cref="PluginAction.Args"/>, no shell.</summary>
    Run,
}

public sealed record PluginAction(
    PluginActionKind Kind,
    string Target,
    string Text,
    string Command,
    IReadOnlyList<string> Args);

public sealed record PluginItem(string Title, string Subtitle, string? Icon, PluginAction Action);
