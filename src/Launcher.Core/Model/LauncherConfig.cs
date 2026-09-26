namespace YourLauncher.Core.Model;

/// <summary>Root of config.json, spec §4.5.</summary>
public sealed class LauncherConfig
{
    /// <summary>Schema version, reserved for future migrations.</summary>
    public int Version { get; set; } = 1;

    public Settings Settings { get; set; } = new();

    public FolderNode Root { get; set; } = new() { Id = "root", Name = "Root" };
}
