using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using YourLauncher.App.Interop;
using YourLauncher.App.ViewModels;

namespace YourLauncher.App.Views;

/// <summary>
/// The single panel window. Owns positioning, show/hide, and keyboard routing (spec §5, §6.1, §6.3) - the
/// ViewModel holds no WPF/Win32 references so it stays testable, but keyboard focus must always remain
/// in the search TextBox while on the List/ConfirmDelete pages (only that TextBox's key handler drives
/// navigation and editing shortcuts); the TypePicker/Editor pages own their own focus and key handling in
/// their respective Views once <see cref="MainViewModel.CurrentPage"/> switches to them.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private int _suppressAutoHideCount;

    /// <summary>Fixed row height so the list can be capped at exactly settings.maxVisibleItems rows.</summary>
    public const double RowHeight = 36;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        ItemsList.MaxHeight = RowHeight * Math.Max(1, viewModel.MaxVisibleItems);

        // Keep the selected row visible when moving past maxVisibleItems (↑↓, PgUp/PgDn, Home/End).
        ItemsList.SelectionChanged += (_, _) =>
        {
            if (ItemsList.SelectedItem is { } item)
            {
                ItemsList.ScrollIntoView(item);
            }
        };

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage))
            {
                // Let the Visibility bindings for the new page apply before moving focus into it.
                Dispatcher.BeginInvoke(DispatcherPriority.Input, MoveFocusToCurrentPage);
            }
            else if (e.PropertyName == nameof(MainViewModel.MaxVisibleItems))
            {
                // settings.maxVisibleItems can change via a config reload (spec §10 item 2) - MaxHeight
                // isn't bound (it's a fixed-row-count computation, not a simple value), so re-apply it here.
                ItemsList.MaxHeight = RowHeight * Math.Max(1, _viewModel.MaxVisibleItems);
            }
        };
    }

    /// <summary>Suppresses the Deactivated auto-hide (spec §5.1) while a Browse… file/folder dialog is open (Phase 3, spec §9).</summary>
    public void BeginSuppressAutoHide() => _suppressAutoHideCount++;

    public void EndSuppressAutoHide() => _suppressAutoHideCount = Math.Max(0, _suppressAutoHideCount - 1);

    /// <summary>Hotkey path: always resets to root first (spec: launcher always opens at root).</summary>
    public void ShowLauncher()
    {
        var stopwatch = Stopwatch.StartNew();

        _viewModel.ClearErrorMessage();
        _viewModel.ResetToRoot();
        PositionOnCursorMonitor();

        Show();
        Activate();
        ForceForeground();

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        stopwatch.Stop();
        Debug.WriteLine($"[YourLauncher] Show latency (WM_HOTKEY/pipe/tray -> Activated): {stopwatch.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Tray/pipe "show" path (spec §10 item 1): if the panel is already visible (or a Browse… dialog is
    /// open), just re-foreground it - no <see cref="MainViewModel.ResetToRoot"/>, so a request from another
    /// instance/the tray icon never throws away where the user currently is. Only shows-from-hidden goes
    /// through the same reset-to-root path as the hotkey.
    /// </summary>
    public void RequestShow()
    {
        if (IsVisible || _suppressAutoHideCount > 0)
        {
            ForceForeground();
            return;
        }

        ShowLauncher();
    }

    public void HideLauncher()
    {
        _viewModel.ClearCutState();
        _viewModel.FlushPendingReloadIfAny();
        Hide();
    }

    public void ToggleLauncher()
    {
        if (IsVisible)
        {
            HideLauncher();
        }
        else
        {
            ShowLauncher();
        }
    }

    /// <summary>
    /// Spec §10 item 1: after asking for the foreground, if it wasn't granted (foreground-lock timeout -
    /// most likely when a different process, e.g. a second app instance via the pipe, requested the show),
    /// simulate an Alt key press/release (a well-known way to defeat that lock) and try once more. If it's
    /// still refused, hide again rather than leave an unfocused Topmost panel that can't auto-hide.
    /// </summary>
    private void ForceForeground()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Win32.SetForegroundWindow(hwnd);
        if (Win32.GetForegroundWindow() != hwnd)
        {
            Win32.keybd_event(Win32.VK_MENU, 0, 0, UIntPtr.Zero);
            Win32.keybd_event(Win32.VK_MENU, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Win32.SetForegroundWindow(hwnd);
        }

        if (Win32.GetForegroundWindow() != hwnd)
        {
            Hide();
            return;
        }

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void MainWindow_OnDeactivated(object? sender, EventArgs e)
    {
        if (_suppressAutoHideCount > 0)
        {
            return;
        }

        HideLauncher();
    }

    private void MoveFocusToCurrentPage()
    {
        switch (_viewModel.CurrentPage)
        {
            case PanelPage.TypePicker:
                TypePickerViewHost.FocusFirstField();
                break;

            case PanelPage.Editor:
                EditorViewHost.FocusFirstField();
                break;

            case PanelPage.IconPicker:
                IconPickerViewHost.FocusFirstField();
                break;

            case PanelPage.List:
            case PanelPage.ConfirmDelete:
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
                break;
        }
    }

    /// <summary>
    /// Spec §10 item 10 (replaces the original §6 approach): keeps WPF Left/Top (no raw SetWindowPos) and
    /// fixes only the centering term. The original code divided the *target* monitor's work area by the
    /// *window's current* DPI, which is wrong whenever the two differ (e.g. hotkey pressed with the cursor
    /// on a monitor at a different scale than the one the window last lived on) - work-area pixels must be
    /// converted using the target monitor's own DPI (<c>GetDpiForMonitor</c>), while the window's DIP
    /// <see cref="Width"/> is scaled by targetScale/currentScale so its actual on-screen footprint is
    /// centered correctly once WPF renders it at whichever DPI the window ends up on.
    /// </summary>
    private void PositionOnCursorMonitor()
    {
        if (!Win32.GetCursorPos(out var cursor))
        {
            return;
        }

        var hMonitor = Win32.MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(hMonitor, ref info))
        {
            return;
        }

        var currentScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var targetScale = currentScale;
        if (Win32.GetDpiForMonitor(hMonitor, Win32.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
        {
            targetScale = dpiX / 96.0;
        }

        var workLeft = info.rcWork.Left / targetScale;
        var workTop = info.rcWork.Top / targetScale;
        var workWidth = (info.rcWork.Right - info.rcWork.Left) / targetScale;
        var workHeight = (info.rcWork.Bottom - info.rcWork.Top) / targetScale;

        var effectiveWidth = Width * targetScale / currentScale;

        Left = workLeft + (workWidth - effectiveWidth) / 2.0;
        Top = workTop + workHeight / 3.0;
    }

    /// <summary>Spec §10 item 10: re-run the fixed centering whenever the window's own DPI changes (e.g. dragged/moved across monitors of different scale - not expected for this cursor-monitor-only panel, but cheap to keep correct).</summary>
    protected override void OnDpiChanged(DpiScale oldDpiScaleInfo, DpiScale newDpiScaleInfo)
    {
        base.OnDpiChanged(oldDpiScaleInfo, newDpiScaleInfo);
        PositionOnCursorMonitor();
    }

    /// <summary>
    /// Spec §10 item 11: Mica/Acrylic + rounded corners on Windows 11, applied once the HWND exists. Any
    /// failure along the way (Win10, or a DWM call failing) leaves the window exactly as XAML compiled it -
    /// solid #1E1E1E background, square corners, <see cref="AllowsTransparency"/> stays false throughout
    /// (a layered/transparent window breaks the DWM backdrop entirely).
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyWindowChromeAndBackdrop();
    }

    private void ApplyWindowChromeAndBackdrop()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var build = Environment.OSVersion.Version.Build;
            if (build < 22000)
            {
                return; // Win10 or older: keep the compiled solid look.
            }

            var darkMode = 1; // Phase 6: a theme switch flips this.
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            var corner = Win32.DWMWCP_ROUND;
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            if (build < 22621)
            {
                return; // Corners still round on 22000-22620; the backdrop itself needs 22H2+.
            }

            var backdrop = Win32.DWMSBT_TRANSIENTWINDOW;
            var hr = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            if (hr != 0)
            {
                return; // HRESULT != S_OK: keep the solid background (readability over effect).
            }

            // The window itself must go fully transparent (composition-target level, not
            // AllowsTransparency) for the DWM backdrop to actually show through; the visible tint comes
            // from RootBorder's own background instead. ResizeMode must allow resizing for DWM to treat
            // the window as round-corner/backdrop eligible - WindowChrome's zeroed resize border keeps it
            // effectively non-resizable to the user.
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(-1),
                UseAeroCaptionButtons = false,
            });

            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } compositionTarget)
            {
                compositionTarget.BackgroundColor = Colors.Transparent;
            }

            Background = Brushes.Transparent;
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1E, 0x1E));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Debug.WriteLine($"[YourLauncher] Mica/Acrylic setup failed, keeping the solid fallback look: {ex.Message}");
        }
    }

    private void SearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Error line clears on next navigation/keypress (spec §8.1).
        _viewModel.ClearErrorMessage();

        if (_viewModel.CurrentPage == PanelPage.ConfirmDelete)
        {
            // Enter confirms; any other key (including nav keys) cancels (spec §6.3).
            if (e.Key == Key.Enter)
            {
                _viewModel.ConfirmPendingDelete();
            }
            else
            {
                _viewModel.CancelPendingDelete();
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q)
        {
            Application.Current.Shutdown();
            e.Handled = true;
            return;
        }

        // ---- Config reload / recovery (spec §4, revised §10 items 3-4). ----
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.R)
        {
            _viewModel.RecoverFromCorruption();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R)
        {
            _viewModel.RequestReload();
            e.Handled = true;
            return;
        }

        // ---- Editing shortcuts (spec §6.3): valid in both nav and search mode. ----
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N)
        {
            _viewModel.BeginAddFolderDirect();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.N:
                    _viewModel.BeginAddNode();
                    e.Handled = true;
                    return;
                case Key.X:
                    _viewModel.CutSelected();
                    e.Handled = true;
                    return;
                case Key.V:
                    _viewModel.PasteIntoCurrentFolder();
                    e.Handled = true;
                    return;
                case Key.D:
                    _viewModel.DuplicateSelected();
                    e.Handled = true;
                    return;
                case Key.I:
                    _viewModel.BeginChangeIcon();
                    e.Handled = true;
                    return;
                case Key.Up:
                    _viewModel.MoveSelectedUp();
                    e.Handled = true;
                    return;
                case Key.Down:
                    _viewModel.MoveSelectedDown();
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.F2:
                _viewModel.BeginEditSelected();
                e.Handled = true;
                return;

            case Key.Delete:
                _viewModel.BeginDelete();
                e.Handled = true;
                return;
        }

        // ---- Navigation (spec §6.1/§6.2), unchanged from Phase 1/2. ----
        switch (e.Key)
        {
            case Key.Down:
                _viewModel.MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                _viewModel.MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                // Ctrl+Enter (search mode only, spec §6.2): show the result in its containing folder
                // instead of opening/launching it.
                if (Keyboard.Modifiers == ModifierKeys.Control && _viewModel.IsSearchMode)
                {
                    _viewModel.ShowSelectedInFolder();
                }
                else
                {
                    _viewModel.EnterSelected();
                }

                e.Handled = true;
                break;

            case Key.Right:
                // Search mode: ← / → move the caret (normal TextBox behavior), they don't navigate.
                if (!_viewModel.IsSearchMode)
                {
                    _viewModel.EnterFolderIfSelected();
                    e.Handled = true;
                }

                break;

            case Key.Tab:
                // Search mode: Tab does nothing special, but must not move focus out of the box either.
                if (_viewModel.IsSearchMode)
                {
                    e.Handled = true;
                }
                else
                {
                    _viewModel.EnterFolderIfSelected();
                    e.Handled = true;
                }

                break;

            case Key.Left:
                if (!_viewModel.IsSearchMode)
                {
                    _viewModel.NavigateUp();
                    e.Handled = true;
                }

                break;

            case Key.Back:
                if (string.IsNullOrEmpty(_viewModel.SearchText))
                {
                    _viewModel.NavigateUp();
                    e.Handled = true;
                }

                // else: let the TextBox perform its normal backspace-in-text behavior.
                break;

            case Key.Escape:
                HandleEscape();
                e.Handled = true;
                break;

            case Key.Home:
                // Search mode: Home keeps the TextBox's normal "caret to start" behavior.
                if (!_viewModel.IsSearchMode)
                {
                    _viewModel.SelectFirst();
                    e.Handled = true;
                }

                break;

            case Key.End:
                // Search mode: End keeps the TextBox's normal "caret to end" behavior.
                if (!_viewModel.IsSearchMode)
                {
                    _viewModel.SelectLast();
                    e.Handled = true;
                }

                break;

            case Key.PageUp:
                _viewModel.PageMove(-1);
                e.Handled = true;
                break;

            case Key.PageDown:
                _viewModel.PageMove(1);
                e.Handled = true;
                break;
        }
    }

    private void HandleEscape()
    {
        if (!string.IsNullOrEmpty(_viewModel.SearchText))
        {
            _viewModel.SearchText = "";
        }
        else if (_viewModel.CanNavigateUp)
        {
            _viewModel.NavigateUp();
        }
        else
        {
            HideLauncher();
        }
    }
}
