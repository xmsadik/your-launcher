using System.Diagnostics;
using System.IO;
using YourLauncher.App.Interop;
using YourLauncher.App.Localization;
using YourLauncher.Core.Providers;

namespace YourLauncher.App.Providers;

/// <summary>
/// The window switcher (tier <see cref="ResultTier.Windows"/>): open top-level windows that match the query,
/// mixed into the normal results below the user's items and the system commands. Filtering and matching live
/// in Core (<see cref="WindowMatcher"/>, unit tested); this class owns the Win32 enumeration and the
/// foreground switch.
/// <para>
/// Snapshot: <see cref="Query"/> runs on the UI thread for every keystroke, and each enumeration touches a
/// few hundred top-level windows plus a process lookup per listed one. A snapshot is therefore reused for
/// <see cref="SnapshotLifetimeMs"/> - long enough to cover one burst of typing, short enough that a
/// reopened panel always sees the current windows (the provider has no hide notification to invalidate on).
/// A window that closed inside that gap is caught by the <c>IsWindow</c> check in <c>Execute</c>.
/// </para>
/// <para>
/// Ids are unique per window ("win:chrome:1a2b", ResultMerger drops duplicate ids) while the usage key is per
/// app ("win:chrome"), so using any Chrome window ranks all of them higher next time.
/// </para>
/// </summary>
public sealed class WindowSwitcherProvider : IResultProvider
{
    private const int SnapshotLifetimeMs = 750;
    private const int MaxTitleLength = 512;
    private const string UwpHostProcess = "applicationframehost";
    private const string UwpCoreWindowClass = "Windows.UI.Core.CoreWindow";

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    private IReadOnlyList<WindowInfo> _snapshot = [];
    private long _snapshotTicks;

    /// <summary>
    /// Runs one throwaway enumeration on a pool thread at startup: the very first one costs ~100 ms of JIT and
    /// P/Invoke stub generation, which would otherwise land on the user's first keystroke (steady state is 1-2 ms).
    /// </summary>
    public WindowSwitcherProvider()
    {
        _ = Task.Run(() =>
        {
            try
            {
                Enumerate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[YourLauncher] Window switcher warm-up failed: {ex.Message}");
            }
        });
    }

    public IReadOnlyList<ProviderResult> Query(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return []; // nothing to match; also skips the enumeration on the empty-search path.
        }

        var matches = WindowMatcher.Match(query, GetSnapshot());
        if (matches.Count == 0)
        {
            return [];
        }

        var results = new List<ProviderResult>(matches.Count);
        foreach (var match in matches)
        {
            var window = match.Window;
            var app = window.ProcessName.ToLowerInvariant();
            var hwnd = new IntPtr(window.Handle);
            results.Add(new ProviderResult
            {
                Id = $"win:{app}:{window.Handle:x}",
                UsageKey = $"win:{(app.Length == 0 ? "unknown" : app)}",
                Title = window.Title,
                Subtitle = window.ProcessName.Length == 0
                    ? Strings.WindowSubtitleNoApp
                    : string.Format(Strings.WindowSubtitle, window.ProcessName),
                Glyph = "",
                IconPath = window.ExePath,
                Tier = ResultTier.Windows,
                Score = match.Score,
                HighlightPositions = match.HighlightPositions,
                Execute = () => SwitchTo(hwnd),
            });
        }

        return results;
    }

    private IReadOnlyList<WindowInfo> GetSnapshot()
    {
        var now = Environment.TickCount64;
        if (_snapshotTicks != 0 && now - _snapshotTicks < SnapshotLifetimeMs)
        {
            return _snapshot;
        }

        try
        {
            _snapshot = Enumerate();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _snapshot = [];
        }

        _snapshotTicks = now;
        return _snapshot;
    }

    private List<WindowInfo> Enumerate()
    {
        var found = new List<WindowInfo>();
        var ownPid = _ownProcessId;

        Win32.EnumWindows(
            (hwnd, _) =>
            {
                // Cheapest rejections first: most top-level windows are invisible or owned.
                if (!Win32.IsWindowVisible(hwnd) || Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero)
                {
                    return true;
                }

                var title = TitleOf(hwnd);
                if (title.Length == 0)
                {
                    return true;
                }

                var exStyle = (uint)Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
                var cloaked = Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_CLOAKED, out var cloakedValue, sizeof(int)) == 0 && cloakedValue != 0;
                if (!WindowMatcher.IsSwitchable(true, cloaked, title, exStyle, false, ClassOf(hwnd)))
                {
                    return true;
                }

                Win32.GetWindowProcessId(hwnd, out var pid);
                if (pid == ownPid)
                {
                    return true; // our own panel (and anything else this process shows).
                }

                var exePath = ProcessImagePath(pid);
                var processName = exePath is null ? "" : Path.GetFileNameWithoutExtension(exePath);

                // UWP windows are hosted by ApplicationFrameHost.exe; the real app is the process that owns
                // the CoreWindow child. When it cannot be found the host's name would only mislead, so the
                // row falls back to the title alone.
                if (string.Equals(processName, UwpHostProcess, StringComparison.OrdinalIgnoreCase))
                {
                    exePath = ResolveUwpAppPath(hwnd, pid);
                    processName = exePath is null ? "" : Path.GetFileNameWithoutExtension(exePath);
                }

                found.Add(new WindowInfo(hwnd.ToInt64(), title, processName, exePath));
                return true;
            },
            IntPtr.Zero);

        return found;
    }

    private static unsafe string TitleOf(IntPtr hwnd)
    {
        var buffer = stackalloc char[MaxTitleLength];
        var length = Win32.GetWindowText(hwnd, buffer, MaxTitleLength);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    private static unsafe string ClassOf(IntPtr hwnd)
    {
        var buffer = stackalloc char[256];
        var length = Win32.GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    /// <summary>The executable behind the CoreWindow child of an ApplicationFrameHost frame, or null when it cannot be found.</summary>
    private static string? ResolveUwpAppPath(IntPtr frameWindow, uint hostPid)
    {
        string? result = null;

        Win32.EnumChildWindows(
            frameWindow,
            (child, _) =>
            {
                if (ClassOf(child) != UwpCoreWindowClass)
                {
                    return true;
                }

                Win32.GetWindowProcessId(child, out var pid);
                if (pid == hostPid)
                {
                    return true;
                }

                result = ProcessImagePath(pid);
                return result is null; // stop at the first CoreWindow whose process we can read.
            },
            IntPtr.Zero);

        return result;
    }

    /// <summary>Full exe path via the limited-rights query that also works for most other users' processes; null for elevated/protected ones.</summary>
    private static unsafe string? ProcessImagePath(uint pid)
    {
        var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[1024];
            var size = 1024;
            return Win32.QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0 ? new string(buffer, 0, size) : null;
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Brings the window to the foreground. Runs while the panel is still the foreground window, which is what
    /// lets Windows allow SetForegroundWindow (MainViewModel hides the panel only after this returns). The
    /// panel's Deactivated handler fires as a result and hides it first; the hide that follows is a no-op, and
    /// it hides regardless of settings.closeAfterLaunch, so the panel can never pull focus back.
    /// </summary>
    private static bool SwitchTo(IntPtr hwnd)
    {
        if (!Win32.IsWindow(hwnd))
        {
            return false;
        }

        if (Win32.IsIconic(hwnd))
        {
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        }

        if (Win32.SetForegroundWindow(hwnd) || Win32.GetForegroundWindow() == hwnd)
        {
            return true;
        }

        // Same Alt press/release trick MainWindow.ForceForeground uses against the foreground lock.
        Win32.keybd_event(Win32.VK_MENU, 0, 0, UIntPtr.Zero);
        Win32.keybd_event(Win32.VK_MENU, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Win32.SetForegroundWindow(hwnd);
        if (Win32.GetForegroundWindow() == hwnd)
        {
            return true;
        }

        Debug.WriteLine("[YourLauncher] Window switch refused by the foreground lock.");
        return false;
    }
}
