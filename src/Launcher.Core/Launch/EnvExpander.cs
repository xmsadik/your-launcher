namespace YourLauncher.Core.Launch;

/// <summary>Null-safe wrapper around <see cref="Environment.ExpandEnvironmentVariables"/> (spec §8.3).</summary>
public static class EnvExpander
{
    public static string? Expand(string? input) =>
        input is null ? null : Environment.ExpandEnvironmentVariables(input);
}
