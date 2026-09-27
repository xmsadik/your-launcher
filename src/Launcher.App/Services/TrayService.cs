using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using YourLauncher.App.Interop;

namespace YourLauncher.App.Services;

/// <summary>What the most recently shown balloon was about, so a click on it (NIN_BALLOONUSERCLICK) can be routed to the right action.</summary>
public enum TrayBalloonKind
{
    None,
    HotkeyFailure,
}

/// <summary>
/// Tray icon built on a hand-written <c>Shell_NotifyIcon</c> interop (spec §10 item 6, decision D4 change
/// from the original WinForms-<c>NotifyIcon</c> plan) rather than <c>UseWindowsForms</c> - so the App
/// project never pulls in ambiguous <c>System.Windows.Forms</c> types alongside WPF's. Reuses the panel
/// window's own HWND/HwndSource (no separate hidden message-only window); the context menu is a normal
/// WPF <see cref="ContextMenu"/> (dark-styled via the app-wide implicit style in App.xaml) opened at the
/// cursor.
/// </summary>
public sealed class TrayService : IDisposable
{
    private const uint IconId = 1;

    private readonly uint _callbackMessage = Win32.WM_APP + 1;
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly IntPtr _hIcon;
    private readonly uint _taskbarCreatedMessage;
    private string _tooltip;
    private TrayBalloonKind _lastBalloonKind;
    private bool _disposed;

    public event Action? ShowRequested;
    public event Action? SettingsRequested;
    public event Action? OpenConfigFileRequested;
    public event Action? OpenConfigFolderRequested;
    public event Action? ReloadConfigRequested;
    public event Action? RecoverFromCorruptionRequested;
    public event Action? ExitRequested;
    public event Action<TrayBalloonKind>? BalloonClicked;

    /// <summary>So the panel's Deactivated auto-hide doesn't fire while the tray's own context menu is open (spec §10 item 6).</summary>
    public event Action? MenuOpening;
    public event Action? MenuClosed;

    /// <summary>Queried fresh every time the menu is (re)built, rather than kept in sync eagerly.</summary>
    public Func<bool> IsRecoveryAvailable { get; set; } = () => false;

    public Func<string> RecoveryMenuText { get; set; } = () => "Restore from backup";

    public TrayService(Window window, string tooltip)
    {
        _tooltip = tooltip;
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("Window has no HWND yet.");
        _source.AddHook(WndProc);
        _taskbarCreatedMessage = Win32.RegisterWindowMessage("TaskbarCreated");
        _hIcon = LoadTrayIcon();
        AddIcon();
    }

    /// <summary>Small icon (SM_CXSMICON-sized) straight out of the exe's own embedded resource (app.ico, spec §10 item 7) - nothing extra to ship or go missing at runtime.</summary>
    private static IntPtr LoadTrayIcon()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return IntPtr.Zero;
        }

        var small = new IntPtr[1];
        return Win32.ExtractIconEx(exePath, 0, null, small, 1) > 0 ? small[0] : IntPtr.Zero;
    }

    private void AddIcon()
    {
        var data = CreateData();
        data.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP;
        Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);
    }

    public void UpdateTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = CreateData();
        data.uFlags = Win32.NIF_TIP;
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
    }

    public void ShowBalloon(string title, string message, TrayBalloonKind kind = TrayBalloonKind.None, bool warning = false)
    {
        _lastBalloonKind = kind;
        var data = CreateData();
        data.uFlags = Win32.NIF_INFO;
        data.szInfo = message;
        data.szInfoTitle = title;
        data.dwInfoFlags = warning ? Win32.NIIF_WARNING : Win32.NIIF_INFO;
        var ok = Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);

        // Balloons can be suppressed by Windows (focus-assist, session policy, ...) with no error - this
        // trace is how §9/§10.15 verification confirms the call itself succeeded either way.
        Debug.WriteLine($"[YourLauncher] Tray balloon '{title}': \"{message}\" (Shell_NotifyIcon NIM_MODIFY ok={ok})");
    }

    private Win32.NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconId,
        uCallbackMessage = _callbackMessage,
        hIcon = _hIcon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)_callbackMessage)
        {
            var mouseMsg = unchecked((uint)lParam.ToInt64());
            if (mouseMsg is Win32.WM_LBUTTONUP or Win32.WM_LBUTTONDBLCLK)
            {
                ShowRequested?.Invoke();
                handled = true;
            }
            else if (mouseMsg == Win32.WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
            else if (mouseMsg == Win32.NIN_BALLOONUSERCLICK)
            {
                BalloonClicked?.Invoke(_lastBalloonKind);
                handled = true;
            }
        }
        else if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage)
        {
            // Explorer restarted - our icon slot is gone, re-add it (spec §10 item 6).
            AddIcon();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        Win32.GetCursorPos(out var cursor);
        Win32.SetForegroundWindow(_hwnd);

        MenuOpening?.Invoke();

        var menu = BuildMenu();
        menu.Placement = PlacementMode.Absolute;
        menu.HorizontalOffset = cursor.X;
        menu.VerticalOffset = cursor.Y;
        menu.Closed += (_, _) =>
        {
            MenuClosed?.Invoke();

            // Classic Shell_NotifyIcon tray-menu trick: a WM_NULL nudge after the menu closes makes sure
            // the click that dismissed it doesn't leak through to whatever's underneath (spec §10 item 6).
            Win32.PostMessage(_hwnd, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        };
        menu.IsOpen = true;
    }

    // Theme-aware styling (spec §10 item 3: "a shared theme-aware menu style factory, rebuilt per
    // BuildMenu() call rather than cached once") lives in ThemedMenuFactory, reused by the panel's own
    // right-click context menu (spec §5) - see that class for why it's built fresh from code on every open.
    private ContextMenu BuildMenu()
    {
        var menuItemStyle = ThemedMenuFactory.CreateMenuItemStyle();
        var menu = new ContextMenu { Style = ThemedMenuFactory.CreateMenuStyle(), OverridesDefaultStyle = true };

        menu.Items.Add(ThemedMenuFactory.CreateItem("Show", menuItemStyle, () => ShowRequested?.Invoke()));
        menu.Items.Add(ThemedMenuFactory.CreateItem("Settings…", menuItemStyle, () => SettingsRequested?.Invoke()));
        menu.Items.Add(ThemedMenuFactory.CreateItem("Open config file", menuItemStyle, () => OpenConfigFileRequested?.Invoke()));
        menu.Items.Add(ThemedMenuFactory.CreateItem("Open config folder", menuItemStyle, () => OpenConfigFolderRequested?.Invoke()));
        menu.Items.Add(ThemedMenuFactory.CreateItem("Reload config", menuItemStyle, () => ReloadConfigRequested?.Invoke()));

        if (IsRecoveryAvailable())
        {
            menu.Items.Add(ThemedMenuFactory.CreateItem(RecoveryMenuText(), menuItemStyle, () => RecoverFromCorruptionRequested?.Invoke()));
        }

        menu.Items.Add(ThemedMenuFactory.CreateSeparator(ThemedMenuFactory.CreateSeparatorStyle()));
        menu.Items.Add(ThemedMenuFactory.CreateItem("Exit", menuItemStyle, () => ExitRequested?.Invoke()));

        return menu;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var data = CreateData();
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
        _source.RemoveHook(WndProc);

        if (_hIcon != IntPtr.Zero)
        {
            Win32.DestroyIcon(_hIcon);
        }
    }
}
