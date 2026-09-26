namespace YourLauncher.Core.Launch;

/// <summary>Requested window visibility for the launched process. Deliberately not System.Diagnostics.ProcessWindowStyle so Core stays UI/BCL-process-free.</summary>
public enum LaunchWindowStyle
{
    Normal,
    Hidden,
}

/// <summary>
/// UI-agnostic equivalent of the subset of ProcessStartInfo the app layer needs. Core computes this;
/// Launcher.App's LaunchService is responsible for turning it into an actual Process.Start call.
/// Exactly one of <see cref="Arguments"/> / <see cref="ArgumentList"/> is set: ArgumentList is used
/// whenever per-argument quoting is safe to delegate to the runtime, Arguments (a raw string) is used
/// when the caller needs exact control over quoting (see the cmd.exe "/s" case in CommandLineBuilder).
/// </summary>
public sealed record LaunchPlan
{
    public required string FileName { get; init; }
    public string? Arguments { get; init; }
    public IReadOnlyList<string>? ArgumentList { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool UseShellExecute { get; init; }
    public bool CreateNoWindow { get; init; }
    public string? Verb { get; init; }
    public LaunchWindowStyle WindowStyle { get; init; } = LaunchWindowStyle.Normal;
}
