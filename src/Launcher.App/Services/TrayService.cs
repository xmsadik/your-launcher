using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
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

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Style = DarkMenuStyle, OverridesDefaultStyle = true };

        menu.Items.Add(MakeItem("Show", () => ShowRequested?.Invoke()));
        menu.Items.Add(MakeItem("Settings…", () => SettingsRequested?.Invoke())); // Phase 6: open the real settings page instead.
        menu.Items.Add(MakeItem("Open config file", () => OpenConfigFileRequested?.Invoke()));
        menu.Items.Add(MakeItem("Open config folder", () => OpenConfigFolderRequested?.Invoke()));
        menu.Items.Add(MakeItem("Reload config", () => ReloadConfigRequested?.Invoke()));

        if (IsRecoveryAvailable())
        {
            menu.Items.Add(MakeItem(RecoveryMenuText(), () => RecoverFromCorruptionRequested?.Invoke()));
        }

        menu.Items.Add(new Separator { Style = DarkSeparatorStyle });
        menu.Items.Add(MakeItem("Exit", () => ExitRequested?.Invoke()));

        return menu;
    }

    private static MenuItem MakeItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header, Style = DarkMenuItemStyle, OverridesDefaultStyle = true };
        item.Click += (_, _) => onClick();
        return item;
    }

    // ------------------------------------------------------------------------------------------------
    // Dark styling built entirely in code (spec §10 item 6: "styled dark") rather than via App.xaml
    // implicit TargetType styles: a ContextMenu opened standalone (IsOpen=true, no PlacementTarget/owner
    // in the logical tree) doesn't reliably pick up Application-level implicit styles for its own chrome
    // in this WPF version - confirmed by hands-on testing (the menu rendered with the default light
    // Aero2/Fluent look even with a matching implicit style present in App.xaml). Assigning the Style
    // explicitly on each element sidesteps that resource-lookup ambiguity entirely. Also mirrors the
    // ComboBox lesson from Phase 3 (EditorView.xaml): a themed control's own chrome ignores plain
    // Background/BorderBrush Setters and needs a full ControlTemplate.
    // ------------------------------------------------------------------------------------------------

    private static readonly Style DarkMenuStyle = CreateMenuStyle();
    private static readonly Style DarkMenuItemStyle = CreateMenuItemStyle();
    private static readonly Style DarkSeparatorStyle = CreateSeparatorStyle();

    private static Style CreateMenuStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x3B, 0x40, 0x48)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(2));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));

        var itemsHost = new FrameworkElementFactory(typeof(StackPanel));
        itemsHost.SetValue(Panel.IsItemsHostProperty, true);
        border.AppendChild(itemsHost);

        var template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
        var style = new Style(typeof(ContextMenu));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI Variable, Segoe UI")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E))));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6))));
        style.Setters.Add(new Setter(FrameworkElement.SnapsToDevicePixelsProperty, true));
        return style;
    }

    private static Style CreateMenuItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
        var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)), "Bd"));
        template.Triggers.Add(highlighted);

        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6))));
        return style;
    }

    private static Style CreateSeparatorStyle()
    {
        var style = new Style(typeof(Separator));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x3B, 0x40, 0x48))));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4)));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 1.0));
        return style;
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
