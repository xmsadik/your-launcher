namespace YourLauncher.Core.Startup;

/// <summary>What <see cref="StartupSync.Decide"/> says the caller should do to the registry Run value.</summary>
public enum StartupAction
{
    None,
    Write,
    Delete,
}

/// <summary>
/// Pure decision logic for Phase 5's "Start with Windows" registry sync (spec §10 item 9): given the
/// desired setting, the current <c>HKCU\...\Run</c> value (or null if absent), and the exe's own path,
/// decides whether to write, delete, or leave the value alone. Kept UI/registry-free so it's unit
/// testable without ever touching a real registry key.
/// </summary>
public static class StartupSync
{
    /// <summary>Command-line flag the Run value passes so a Windows sign-in start stays in the tray instead of showing the panel.</summary>
    public const string SilentArg = "--silent";

    /// <summary>The Run value's data: the quoted exe path (spec §10 item 3) followed by <see cref="SilentArg"/>.</summary>
    public static string Command(string exePath) => $"\"{exePath}\" {SilentArg}";

    /// <summary>
    /// desired=true: write if the value is missing or differs from <see cref="Command"/> (case-insensitive -
    /// the exe may have moved, drive letters may differ in case, or an older build wrote it without
    /// <see cref="SilentArg"/>). desired=false: delete if a value is present, otherwise do nothing.
    /// </summary>
    public static StartupAction Decide(bool desiredEnabled, string? currentValue, string exePath)
    {
        if (!desiredEnabled)
        {
            return currentValue is null ? StartupAction.None : StartupAction.Delete;
        }

        return string.Equals(currentValue, Command(exePath), StringComparison.OrdinalIgnoreCase)
            ? StartupAction.None
            : StartupAction.Write;
    }
}
