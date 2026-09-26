using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
        };
    }

    /// <summary>Suppresses the Deactivated auto-hide (spec §5.1) while a Browse… file/folder dialog is open (Phase 3, spec §9).</summary>
    public void BeginSuppressAutoHide() => _suppressAutoHideCount++;

    public void EndSuppressAutoHide() => _suppressAutoHideCount = Math.Max(0, _suppressAutoHideCount - 1);

    public void ShowLauncher()
    {
        var stopwatch = Stopwatch.StartNew();

        _viewModel.ClearErrorMessage();
        _viewModel.ResetToRoot();
        PositionOnCursorMonitor();

        Show();
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            Win32.SetForegroundWindow(hwnd);
        }

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        stopwatch.Stop();
        Debug.WriteLine($"[YourLauncher] Show latency: {stopwatch.ElapsedMilliseconds} ms");
    }

    public void HideLauncher()
    {
        _viewModel.ClearCutState();
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

        var dpi = VisualTreeHelper.GetDpi(this);

        var workLeft = info.rcWork.Left / dpi.DpiScaleX;
        var workTop = info.rcWork.Top / dpi.DpiScaleY;
        var workWidth = (info.rcWork.Right - info.rcWork.Left) / dpi.DpiScaleX;
        var workHeight = (info.rcWork.Bottom - info.rcWork.Top) / dpi.DpiScaleY;

        Left = workLeft + (workWidth - Width) / 2.0;
        Top = workTop + workHeight / 3.0;
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
