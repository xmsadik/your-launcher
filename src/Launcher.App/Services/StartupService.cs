using System.Diagnostics;
using Microsoft.Win32;
using Windows.ApplicationModel;
using YourLauncher.Core.Startup;

namespace YourLauncher.App.Services;

/// <summary>
/// Syncs <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> to <c>settings.startWithWindows</c>
/// (spec §3, revised §10 item 9). The actual write/delete/none decision is Core's <see cref="StartupSync"/>
/// (unit tested); this class only does the registry IO and the "should we even touch it right now" gate.
///
/// The Run value name is a ctor parameter (default "Your Launcher") specifically so a throwaway name can
/// be used for manual/live verification without ever touching the real value (hard rule, spec §10 item 9).
///
/// The MSIX (Store) build can't use the Run value - its HKCU writes are virtualized - so there it drives the
/// manifest's StartupTask (<see cref="PackagedTaskId"/>) instead, and never touches the Run value at all.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Must match the <c>desktop:StartupTask TaskId</c> in packaging/AppxManifest.xml.</summary>
    private const string PackagedTaskId = "YourLauncherStartup";

    private readonly string _valueName;

    public StartupService(string valueName = "Your Launcher")
    {
        _valueName = valueName;
    }

    /// <summary>
    /// Set when Windows won't let the app start with it even though the setting is on (packaged build only:
    /// the user turned it off in Task Manager / Settings, or policy did) - the settings page shows it, since
    /// only the user can turn it back on there. Null otherwise.
    /// </summary>
    public string? StatusNote { get; private set; }

    /// <summary>Applies <paramref name="desiredEnabled"/> to the registry (or the packaged StartupTask), unless <see cref="ShouldSkip"/> says this run shouldn't touch it at all.</summary>
    public void Sync(bool desiredEnabled)
    {
        if (ShouldSkip(out var reason))
        {
            Debug.WriteLine($"[YourLauncher] Startup registry sync skipped: {reason}");
            return;
        }

        if (PackageContext.IsPackaged)
        {
            _ = SyncPackagedAsync(desiredEnabled);
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

    private async Task SyncPackagedAsync(bool desiredEnabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(PackagedTaskId);
            var state = task.State;

            if (desiredEnabled && state == StartupTaskState.Disabled)
            {
                state = await task.RequestEnableAsync();
            }
            else if (!desiredEnabled && state is StartupTaskState.Enabled)
            {
                task.Disable();
                state = task.State;
            }

            StatusNote = desiredEnabled switch
            {
                true when state == StartupTaskState.DisabledByUser =>
                    "Turned off in Task Manager (Startup apps). Turn it back on there to start with Windows.",
                true when state == StartupTaskState.DisabledByPolicy =>
                    "Starting with Windows is blocked by a system policy.",
                false when state == StartupTaskState.EnabledByPolicy =>
                    "A system policy still starts the app with Windows.",
                _ => null,
            };
            Debug.WriteLine($"[YourLauncher] StartupTask '{PackagedTaskId}': desired={desiredEnabled}, state={state}.");
        }
        catch (Exception ex)
        {
            // Best effort by design: CsWinRT surfaces HRESULTs as many exception types (FileNotFound, Unauthorized-
            // Access, ...), e.g. for a TaskId that doesn't match the manifest - never take the app down over this.
            Debug.WriteLine($"[YourLauncher] StartupTask sync failed: {ex.Message}");
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
