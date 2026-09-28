using System.Diagnostics;
using Microsoft.Win32;
using YourLauncher.Core.Startup;

namespace YourLauncher.App.Services;

/// <summary>
/// Syncs <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> to <c>settings.startWithWindows</c>
/// (spec §3, revised §10 item 9). The actual write/delete/none decision is Core's <see cref="StartupSync"/>
/// (unit tested); this class only does the registry IO and the "should we even touch it right now" gate.
///
/// The Run value name is a ctor parameter (default "Your Launcher") specifically so a throwaway name can
/// be used for manual/live verification without ever touching the real value (hard rule, spec §10 item 9).
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;

    public StartupService(string valueName = "Your Launcher")
    {
        _valueName = valueName;
    }

    /// <summary>Applies <paramref name="desiredEnabled"/> to the registry, unless <see cref="ShouldSkip"/> says this run shouldn't touch it at all.</summary>
    public void Sync(bool desiredEnabled)
    {
        if (ShouldSkip(out var reason))
        {
            Debug.WriteLine($"[YourLauncher] Startup registry sync skipped: {reason}");
            return;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Debug.WriteLine("[YourLauncher] Startup registry sync skipped: Environment.ProcessPath is empty.");
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        var current = key.GetValue(_valueName) as string;
        var action = StartupSync.Decide(desiredEnabled, current, exePath);

        switch (action)
        {
            case StartupAction.Write:
                key.SetValue(_valueName, StartupSync.Command(exePath));
                Debug.WriteLine($"[YourLauncher] Startup registry: wrote '{_valueName}'.");
                break;

            case StartupAction.Delete:
                key.DeleteValue(_valueName, throwOnMissingValue: false);
                Debug.WriteLine($"[YourLauncher] Startup registry: deleted '{_valueName}'.");
                break;

            case StartupAction.None:
                break;
        }
    }

    /// <summary>Phase 6 settings toggle calls straight into this.</summary>
    public void SetEnabled(bool enabled) => Sync(enabled);

    private static bool ShouldSkip(out string reason)
    {
#if DEBUG
        reason = "Debug build";
        return true;
#else
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("YOURLAUNCHER_CONFIG_DIR")))
        {
            reason = "YOURLAUNCHER_CONFIG_DIR is set (test/scratch mode)";
            return true;
        }

        if (Environment.GetEnvironmentVariable("YOURLAUNCHER_NO_STARTUP_REG") == "1")
        {
            reason = "YOURLAUNCHER_NO_STARTUP_REG=1";
            return true;
        }

        reason = "";
        return false;
#endif
    }
}
