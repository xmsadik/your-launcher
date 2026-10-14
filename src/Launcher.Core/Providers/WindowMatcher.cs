using YourLauncher.Core.Search;

namespace YourLauncher.Core.Providers;

/// <summary>One top-level window that passed <see cref="WindowMatcher.IsSwitchable"/>, as the window switcher sees it.</summary>
/// <param name="Handle">The HWND as a plain number (Core has no Win32 types).</param>
/// <param name="Title">Window caption.</param>
/// <param name="ProcessName">Executable name without extension and path ("chrome"); empty when it could not be resolved.</param>
/// <param name="ExePath">Full path of the executable for the row icon; null when the process could not be opened (elevated).</param>
/// <param name="IsCloaked">True for a window on another virtual desktop / hidden workspace (only listed when settings.includeOtherDesktopWindows is on).</param>
public sealed record WindowInfo(long Handle, string Title, string ProcessName, string? ExePath, bool IsCloaked = false);

/// <summary>A window that matched a query: the window, the score and the highlight indices into its title.</summary>
public sealed record WindowMatch(WindowInfo Window, int Score, IReadOnlyList<int> HighlightPositions);

/// <summary>
/// The pure half of the window switcher (no Win32, so it is unit-testable): which top-level windows count
/// as "real" windows, and how a query matches them. Matching reuses <see cref="FuzzyScorer"/> and
/// <see cref="TextNormalizer"/> like node search and system commands, so Turkish casing and diacritics
/// behave the same everywhere.
/// </summary>
public static class WindowMatcher
{
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsExAppWindow = 0x00040000;
    public const uint WsExNoActivate = 0x08000000;

    /// <summary>Shell windows that are visible, titled and owner-less but are not something to switch to.</summary>
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public const string UwpCoreWindowClass = "Windows.UI.Core.CoreWindow";
    public const string UwpFrameClass = "ApplicationFrameWindow";

    /// <summary>Title matches count fully, the process name a bit less, so a window titled "code" ranks above an unrelated window of code.exe.</summary>
    private const double TitleWeight = 1.0;
    private const double ProcessWeight = 0.7;

    /// <summary>
    /// The Alt+Tab-like filter: visible, not cloaked (a window on another virtual desktop, or a suspended
    /// UWP app, is "visible" but cloaked), has a title, no owner, and not a tool/no-activate window unless it
    /// explicitly asks to be on the taskbar (<c>WS_EX_APPWINDOW</c>). Shell windows (desktop, taskbar) are out.
    /// </summary>
    /// <remarks>
    /// With <paramref name="includeCloaked"/> a cloaked window is still listed, unless it is one of the two
    /// "ghost" shapes Windows keeps cloaked for suspended Store apps: a top-level <see cref="UwpCoreWindowClass"/>
    /// window (TextInputHost, a closed Settings app), or an <see cref="UwpFrameClass"/> frame with no CoreWindow
    /// inside (<paramref name="isEmptyUwpFrame"/>). Both carry the same cloak reason (shell) as a window that a
    /// virtual desktop or GlazeWM hides, so the window class is what tells them apart.
    /// </remarks>
    public static bool IsSwitchable(bool isVisible, bool isCloaked, string title, uint exStyle, bool hasOwner, string className,
        bool includeCloaked = false, bool isEmptyUwpFrame = false)
    {
        if (!isVisible || string.IsNullOrWhiteSpace(title) || hasOwner)
        {
            return false;
        }

        if (isCloaked && (!includeCloaked || IsCloakedGhost(className, isEmptyUwpFrame)))
        {
            return false;
        }

        if (ShellClasses.Contains(className, StringComparer.Ordinal))
        {
            return false;
        }

        var wantsTaskbar = (exStyle & WsExAppWindow) != 0;
        return wantsTaskbar || (exStyle & (WsExToolWindow | WsExNoActivate)) == 0;
    }

    private static bool IsCloakedGhost(string className, bool isEmptyUwpFrame) =>
        string.Equals(className, UwpCoreWindowClass, StringComparison.Ordinal)
        || (isEmptyUwpFrame && string.Equals(className, UwpFrameClass, StringComparison.Ordinal));

    /// <summary>
    /// Windows matching <paramref name="query"/>, best first. A blank query returns nothing (the list must not
    /// be flooded with every open window before the user typed anything). The scattered Fuzzy tier is ignored
    /// for queries shorter than 3 chars so "ab" does not offer half the desktop.
    /// </summary>
    public static IReadOnlyList<WindowMatch> Match(string query, IEnumerable<WindowInfo> windows)
    {
        var normalizedQuery = TextNormalizer.Normalize(query).Trim();
        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        var allowFuzzy = normalizedQuery.Length >= 3;
        var matches = new List<WindowMatch>();
        foreach (var window in windows)
        {
            var title = Score(normalizedQuery, window.Title, TitleWeight, allowFuzzy);
            var process = window.ProcessName.Length == 0 ? null : Score(normalizedQuery, window.ProcessName, ProcessWeight, allowFuzzy);

            if (title is null && process is null)
            {
                continue;
            }

            // Highlight only when the title itself is what matched best (positions index into the title).
            var useTitle = title is not null && (process is null || title.Value.Score >= process.Value.Score);
            matches.Add(new WindowMatch(
                window,
                useTitle ? title!.Value.Score : process!.Value.Score,
                useTitle ? title!.Value.Positions : []));
        }

        matches.Sort((a, b) => b.Score.CompareTo(a.Score));
        return matches;
    }

    private static (int Score, int[] Positions)? Score(string normalizedQuery, string text, double weight, bool allowFuzzy)
    {
        var result = FuzzyScorer.Score(normalizedQuery, TextNormalizer.Normalize(text));
        if (result is null || (result.Value.Tier == MatchTier.Fuzzy && !allowFuzzy))
        {
            return null;
        }

        return ((int)(result.Value.Score * weight), result.Value.Positions);
    }
}
