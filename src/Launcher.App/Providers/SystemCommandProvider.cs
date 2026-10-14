using System.Diagnostics;
using System.IO;
using YourLauncher.App.Interop;
using YourLauncher.App.Localization;
using YourLauncher.Core.Providers;

namespace YourLauncher.App.Providers;

/// <summary>
/// Built-in system commands (tier <see cref="ResultTier.SystemCommands"/>). The catalogue, keywords and
/// matching live in Core (<see cref="SystemCommandCatalog"/>, unit tested); this class owns the localized
/// titles and the Win32 side effects.
/// <para>
/// Lock, Sleep and Hibernate are deferred by <see cref="SuspendDelayMs"/>: MainViewModel hides the panel
/// only AFTER <c>Execute</c> returns, and a machine that suspends first would wake up with the launcher
/// still on screen. Deferred actions run on a thread-pool thread because SetSuspendState blocks until the
/// machine resumes; the row reports success immediately (failure of these calls is not surfaced).
/// </para>
/// </summary>
public sealed class SystemCommandProvider : IResultProvider
{
    private const int SuspendDelayMs = 300;

    // Hibernation availability does not change while the launcher runs in any way a user would expect us to
    // track (powercfg /h needs admin and a reboot-ish settle); one lookup keeps Query allocation-free.
    private readonly Lazy<bool> _hibernateAllowed = new(() =>
    {
        try
        {
            return Win32.IsPwrHibernateAllowed();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    });

    public IReadOnlyList<ProviderResult> Query(string query)
    {
        var matches = SystemCommandCatalog.Match(query, TitleOf, IsAvailable);
        if (matches.Count == 0)
        {
            return [];
        }

        var results = new List<ProviderResult>(matches.Count);
        foreach (var match in matches)
        {
            var command = match.Command;
            results.Add(new ProviderResult
            {
                Id = command.Id,
                Title = TitleOf(command.Id),
                Subtitle = Strings.SysCmdSubtitle,
                Glyph = command.Glyph,
                Tier = ResultTier.SystemCommands,
                Score = match.Score,
                ConfirmLaunch = command.ConfirmLaunch,
                HighlightPositions = match.HighlightPositions,
                Execute = ActionFor(command.Id),
            });
        }

        return results;
    }

    private bool IsAvailable(string id) => id != SystemCommandCatalog.Hibernate || _hibernateAllowed.Value;

    private static string TitleOf(string id) => id switch
    {
        SystemCommandCatalog.Lock => Strings.SysCmdLock,
        SystemCommandCatalog.Sleep => Strings.SysCmdSleep,
        SystemCommandCatalog.Hibernate => Strings.SysCmdHibernate,
        SystemCommandCatalog.SignOut => Strings.SysCmdSignOut,
        SystemCommandCatalog.Restart => Strings.SysCmdRestart,
        SystemCommandCatalog.ShutDown => Strings.SysCmdShutDown,
        SystemCommandCatalog.EmptyRecycleBin => Strings.SysCmdEmptyRecycleBin,
        _ => id,
    };

    private static Func<bool> ActionFor(string id) => id switch
    {
        SystemCommandCatalog.Lock => () => Deferred(() => Win32.LockWorkStation()),
        SystemCommandCatalog.Sleep => () => Deferred(Sleep),
        SystemCommandCatalog.Hibernate => () => Deferred(() => Win32.SetSuspendState(hibernate: true, forceCritical: false, disableWakeEvent: false)),
        SystemCommandCatalog.SignOut => () => Win32.ExitWindowsEx(Win32.EWX_LOGOFF, 0),
        SystemCommandCatalog.Restart => () => RunShutdown("/r /t 0"),
        SystemCommandCatalog.ShutDown => () => RunShutdown("/s /t 0"),
        SystemCommandCatalog.EmptyRecycleBin => EmptyRecycleBin,
        _ => () => false,
    };

    /// <summary>
    /// SetSuspendState(hibernate: false) only sleeps on S1-S3 machines; on a Modern Standby (S0 low power
    /// idle) laptop it hibernates instead, which is not what "Sleep" means. There, turning the display off is
    /// how Windows itself enters standby, so that is what we do.
    /// </summary>
    private static void Sleep()
    {
        if (HasClassicSleep())
        {
            Win32.SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
        }
        else
        {
            Win32.PostMessage(Win32.HWND_BROADCAST, Win32.WM_SYSCOMMAND, Win32.SC_MONITORPOWER, new IntPtr(2)); // lParam 2 = display off
        }
    }

    private static unsafe bool HasClassicSleep()
    {
        var buffer = stackalloc byte[Win32.SystemPowerCapabilitiesBufferSize];
        try
        {
            if (!Win32.GetPwrCapabilities(buffer))
            {
                return true; // unknown: keep the documented API.
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }

        for (var offset = Win32.PowerCapabilitiesS1Offset; offset <= Win32.PowerCapabilitiesS3Offset; offset++)
        {
            if (buffer[offset] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Deferred(Action action)
    {
        _ = Task.Delay(SuspendDelayMs).ContinueWith(_ => action(), TaskScheduler.Default);
        return true;
    }

    private static bool RunShutdown(string arguments)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        // shutdown.exe returns right after scheduling; a non-zero exit code means it refused (e.g. a
        // shutdown already in progress), which we want on the error line instead of a silent "success".
        return !process.WaitForExit(3000) || process.ExitCode == 0;
    }

    /// <summary>
    /// No-confirmation flags on purpose: the launcher's own confirm bar (ConfirmLaunch) already asked, and
    /// stacking the shell's dialog on top would make it ask twice. E_UNEXPECTED (0x8000FFFF) is what some
    /// Windows builds return for an already-empty bin, which is a success for the user.
    /// </summary>
    private static bool EmptyRecycleBin()
    {
        const int EUnexpected = unchecked((int)0x8000FFFF);
        var hr = Win32.SHEmptyRecycleBin(IntPtr.Zero, null, Win32.SHERB_NOCONFIRMATION | Win32.SHERB_NOPROGRESSUI | Win32.SHERB_NOSOUND);
        return hr >= 0 || hr == EUnexpected;
    }
}
