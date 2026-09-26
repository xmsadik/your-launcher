using System.ComponentModel;
using System.Diagnostics;
using YourLauncher.Core.Launch;
using YourLauncher.Core.Model;

namespace YourLauncher.App.Services;

/// <summary>
/// Turns a <see cref="Node"/> into a <see cref="LaunchPlan"/> (via Core's CommandLineBuilder) and
/// executes it with Process.Start. Never throws out of <see cref="Launch"/>; errors are reported via
/// <see cref="ErrorOccurred"/> (spec §8.1: launch failures keep the launcher open).
/// </summary>
public sealed class LaunchService
{
    /// <summary>Panel-visible launch failures only (spec §8.1). Hidden-command non-zero exit codes go through <see cref="HiddenCommandExited"/> instead (spec §10 item 8) so they surface as a tray balloon rather than a panel error line the user may never see.</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>Fired from a background thread when a <c>window: hidden</c> command finishes with a non-zero exit code (spec §8.2/§10 item 8); subscribers must marshal to the UI thread themselves.</summary>
    public event Action<string, int>? HiddenCommandExited;

    /// <summary>Returns true if a process was started. Folders are not launchable and return false without side effects.</summary>
    public bool Launch(Node node, Settings settings)
    {
        LaunchPlan plan;
        var isHiddenCommand = false;

        switch (node)
        {
            case AppNode app:
                plan = CommandLineBuilder.BuildForApp(app);
                break;
            case PathNode path:
                plan = CommandLineBuilder.BuildForPath(path);
                break;
            case UrlNode url:
                plan = CommandLineBuilder.BuildForUrl(url);
                break;
            case CommandNode command:
                plan = CommandLineBuilder.BuildForCommand(command, settings);
                isHiddenCommand = command.Window == WindowMode.Hidden;
                break;
            default:
                return false;
        }

        return Execute(plan, node.Name, isHiddenCommand);
    }

    private bool Execute(LaunchPlan plan, string displayName, bool isHiddenCommand)
    {
        var psi = new ProcessStartInfo
        {
            FileName = plan.FileName,
            UseShellExecute = plan.UseShellExecute,
            CreateNoWindow = plan.CreateNoWindow,
            WindowStyle = plan.WindowStyle == LaunchWindowStyle.Hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
        };

        if (!string.IsNullOrEmpty(plan.WorkingDirectory))
        {
            psi.WorkingDirectory = plan.WorkingDirectory;
        }

        if (plan.Verb is not null)
        {
            psi.Verb = plan.Verb;
        }

        if (plan.ArgumentList is not null)
        {
            foreach (var arg in plan.ArgumentList)
            {
                psi.ArgumentList.Add(arg);
            }
        }
        else if (plan.Arguments is not null)
        {
            psi.Arguments = plan.Arguments;
        }

        try
        {
            var process = Process.Start(psi);
            if (isHiddenCommand && process is not null)
            {
                _ = MonitorHiddenProcessAsync(process, displayName);
            }
            else
            {
                process?.Dispose();
            }

            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user dismissed the UAC prompt. Ignore silently per spec §8.1/§8.2.
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Could not open '{displayName}': {ex.Message}");
            return false;
        }
    }

    private async Task MonitorHiddenProcessAsync(Process process, string displayName)
    {
        using var _ = process;
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            Debug.WriteLine($"Hidden command '{displayName}' exited with code {process.ExitCode}.");
            HiddenCommandExited?.Invoke(displayName, process.ExitCode);
        }
    }
}
