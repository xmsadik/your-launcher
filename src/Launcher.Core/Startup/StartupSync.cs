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
    /// <summary>Wraps a path in double quotes the way the Run value stores it (spec §10 item 3: quoted exe path).</summary>
    public static string Quote(string exePath) => $"\"{exePath}\"";

    /// <summary>
    /// desired=true: write if the value is missing, or its (unquoted) path differs from the current exe
    /// path (case-insensitive - the exe may have moved, or drive letters may differ in case). desired=false:
    /// delete if a value is present, otherwise do nothing.
    /// </summary>
    public static StartupAction Decide(bool desiredEnabled, string? currentValue, string exePath)
    {
        if (!desiredEnabled)
        {
            return currentValue is null ? StartupAction.None : StartupAction.Delete;
        }

        if (currentValue is null)
        {
            return StartupAction.Write;
        }

        return PathsEqual(Unquote(currentValue), exePath) ? StartupAction.None : StartupAction.Write;
    }

    private static bool PathsEqual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
