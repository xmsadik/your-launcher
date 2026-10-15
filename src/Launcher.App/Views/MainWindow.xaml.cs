using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using YourLauncher.App.Interop;
using YourLauncher.App.Localization;
using YourLauncher.App.Services;
using YourLauncher.App.ViewModels;
using YourLauncher.Core.Icons;
using YourLauncher.Core.Model;

namespace YourLauncher.App.Views;

/// <summary>
/// The single panel window. Owns positioning, show/hide, and keyboard routing (spec §5, §6.1, §6.3) - the
/// ViewModel holds no WPF/Win32 references so it stays testable, but keyboard focus must always remain
/// in the search TextBox while on the List/ConfirmDelete/ConfirmImport pages (only that TextBox's key
/// handler drives navigation and editing shortcuts); the TypePicker/Editor/IconPicker/Settings pages own
/// their own focus and key handling in their respective Views once
/// <see cref="MainViewModel.CurrentPage"/> switches to them.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ThemeService _themeService;
    private int _suppressAutoHideCount;

    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

    /// <summary>Private DataObject format for the in-list drag (spec §7/§10 item 8) - distinct from <see cref="DataFormats.FileDrop"/> so the two drop paths (Explorer files vs. reordering) never collide.</summary>
    private const string InternalDragFormat = "YourLauncher.InternalNodeDrag";

    /// <summary>Polling timer for the Explorer-drag deactivation deferral (spec §6/§10 item 6a) - null whenever not currently waiting on a mouse-button release.</summary>
    private DispatcherTimer? _dragDeferTimer;

    /// <summary>True from the moment a FileDrop DragEnter reaches the panel until Drop/DragLeave - tells the deferred-hide poll a real drag made it here (spec §6).</summary>
    private bool _externalDragOverPanel;

    /// <summary>Mouse-down anchor for the in-list drag threshold check (spec §7) - set on PreviewMouseLeftButtonDown, cleared on button-up or once a drag actually starts.</summary>
    private Point _listDragStartPoint;
    private ListItemViewModel? _listDragCandidate;

    /// <summary>Device-pixel y of the panel's bottom edge while settings.position is Bottom (re-applied on every height change), else null.</summary>
    private double? _bottomAnchorPx;

    /// <summary>Fixed row height so the list can be capped at exactly settings.maxVisibleItems rows.</summary>
    public const double RowHeight = 36;

    // ---- Docked panel style (settings.panelStyle = docked); everything below is inert while Floating. ----

    /// <summary>Width of the panel itself (the XAML Width); the docked window adds the fillet margins on both sides.</summary>
    private const double PanelWidth = 640;

    /// <summary>Radius of the concave fillets, and the width of the transparent margin each side of the docked panel.</summary>
    private const double FilletRadius = 22;

    /// <summary>Corner radius of the docked panel's rounded corners.</summary>
    private const double PanelCornerRadius = 28;

    /// <summary>Duration of the slide-in when the docked panel opens at the top/bottom edge.</summary>
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(250));

    /// <summary>True when the window was created as a docked (transparent, layered) panel; decided once at startup.</summary>
    private readonly bool _docked;

    /// <summary>Docked only: the content root that holds the panel and the fillets, and carries the slide-in transform.</summary>
    private Grid? _dockedRoot;
    private TranslateTransform? _slide;
    private System.Windows.Shapes.Path? _filletLeft;
    private System.Windows.Shapes.Path? _filletRight;

    /// <summary>Docked only: each row-grid child's row as laid out in XAML, so the vertical order can be changed and restored.</summary>
    private Dictionary<UIElement, int>? _baseRows;

    /// <summary>settings.position/edgeMargin as last applied to the window, so a settings save that leaves them unchanged does not move it.</summary>
    private (PanelPosition Position, int EdgeMargin) _placedWith;
    private bool _rowsReversed;

    public MainWindow(MainViewModel viewModel, ThemeService themeService, PanelStyle panelStyle = PanelStyle.Floating)
    {
        _viewModel = viewModel;
        _themeService = themeService;
        DataContext = viewModel;
        InitializeComponent();
        _placedWith = (viewModel.PanelPosition, viewModel.EdgeMargin);
        if (panelStyle == PanelStyle.Docked)
        {
            // AllowsTransparency can only change before the HWND exists, so the style is fixed at construction.
            _docked = true;
            BuildDockedChrome();
        }

        ItemsList.MaxHeight = RowHeight * Math.Max(1, viewModel.MaxVisibleItems);

        // Keep the selected row visible when moving past maxVisibleItems (↑↓, PgUp/PgDn, Home/End).
        ItemsList.SelectionChanged += (_, _) =>
        {
            if (ItemsList.SelectedItem is { } item)
            {
                ItemsList.ScrollIntoView(item);
            }
        };

        // A prefilled search text ("jira ") leaves the caret at the start; the user types the query after it.
        _viewModel.SearchCaretToEndRequested += () => SearchBox.CaretIndex = SearchBox.Text.Length;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage))
            {
                // Let the Visibility bindings for the new page apply before moving focus into it.
                Dispatcher.BeginInvoke(DispatcherPriority.Input, MoveFocusToCurrentPage);
                if (_docked)
                {
                    ApplyDockedRowOrder();
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.MaxVisibleItems))
            {
                // settings.maxVisibleItems can change via a config reload (spec §10 item 2) - MaxHeight
                // isn't bound (it's a fixed-row-count computation, not a simple value), so re-apply it here.
                ItemsList.MaxHeight = RowHeight * Math.Max(1, _viewModel.MaxVisibleItems);
            }
            else if ((e.PropertyName is nameof(MainViewModel.PanelPosition) or nameof(MainViewModel.EdgeMargin))
                     && (_viewModel.PanelPosition, _viewModel.EdgeMargin) != _placedWith)
            {
                _placedWith = (_viewModel.PanelPosition, _viewModel.EdgeMargin);
                // Every settings save/reload raises these; only an actual change may move a visible panel (otherwise
                // saving any setting would jump it to the cursor's monitor).
                if (IsVisible)
                {
                    PositionOnCursorMonitor();
                }

                if (_docked)
                {
                    ApplyDockedShape();
                }
            }
        };

        // SizeToContent grows/shrinks the window from its top edge; a bottom-docked panel must keep its bottom edge put.
        SizeChanged += (_, e) =>
        {
            if (e.HeightChanged && _bottomAnchorPx is { } bottomPx)
            {
                Top = bottomPx / VisualTreeHelper.GetDpi(this).DpiScaleY - ActualHeight;
            }
        };
    }

    /// <summary>Suppresses the Deactivated auto-hide (spec §5.1) while a Browse… file/folder dialog is open (Phase 3, spec §9).</summary>
    public void BeginSuppressAutoHide() => _suppressAutoHideCount++;

    public void EndSuppressAutoHide() => _suppressAutoHideCount = Math.Max(0, _suppressAutoHideCount - 1);

    /// <summary>Hotkey path: always calls <see cref="MainViewModel.ResetToRoot"/> first - root, unless
    /// settings.rememberLastLocation reopens the last-hidden folder instead (spec §1.2).</summary>
    public void ShowLauncher()
    {
        var stopwatch = Stopwatch.StartNew();

        _viewModel.ClearErrorMessage();
        _viewModel.ResetToRoot();
        PositionOnCursorMonitor();

        var slideIn = _docked && PrepareSlideIn();
        Show();
        Activate();
        ForceForeground();

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        if (slideIn)
        {
            BeginSlideIn();
        }

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
        _viewModel.RememberCurrentLocation();
        _viewModel.ClearCutState();
        _viewModel.EndHotkeyCaptures();
        _viewModel.FlushPendingReloadIfAny();
        if (_docked)
        {
            StopSlide(); // hidden mid-slide: the next show starts from a clean transform.
        }

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
            // While a Browse… dialog is open Windows activates that owned modal dialog instead - not a failure, and
            // hiding here would leave the panel hidden once the dialog closes.
            if (_suppressAutoHideCount == 0)
            {
                Hide();
            }

            return;
        }

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    /// <summary>
    /// Spec §6/§10 item 6a: an ordinary click on another window (e.g. Explorer) deactivates the panel the
    /// instant the mouse button goes down there - exactly the same signal a *press-and-drag* toward the
    /// panel starts with. If the left button is still down right now, defer the hide and poll
    /// (<see cref="BeginDragDeferPoll"/>) until it's released, so a real drag has time to reach
    /// <see cref="Panel_OnDragEnter"/>/<see cref="Panel_OnDrop"/> before the panel disappears. A plain
    /// click (no drag) still ends up hiding the panel once the poll sees the button come back up with no
    /// drag having reached us - just delayed by up to one poll interval (documented limitation, spec §10
    /// item 6a: "only a press-and-drag in one motion from Explorer survives").
    /// </summary>
    private void MainWindow_OnDeactivated(object? sender, EventArgs e)
    {
        if (_suppressAutoHideCount > 0)
        {
            return;
        }

        if ((Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) != 0)
        {
            BeginDragDeferPoll();
            return;
        }

        HideLauncher();
    }

    private void BeginDragDeferPoll()
    {
        if (_dragDeferTimer is not null)
        {
            return; // already polling from an earlier Deactivated.
        }

        DateTime? releasedAt = null;
        _dragDeferTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _dragDeferTimer.Tick += (_, _) =>
        {
            if ((Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) != 0)
            {
                return; // still held down - keep waiting.
            }

            // Button released while a drag is still over the panel: the source's Drop/DragLeave reaches us
            // cross-process a few ms after the release, so keep waiting for it to clear the flag (capped,
            // in case neither ever arrives) - deciding now would race a rejected drop's DragLeave and could
            // leave a visible, deactivated Topmost panel (spec §10 item 6b).
            releasedAt ??= DateTime.UtcNow;
            if (_externalDragOverPanel && DateTime.UtcNow - releasedAt < TimeSpan.FromSeconds(2))
            {
                return;
            }

            _dragDeferTimer!.Stop();
            _dragDeferTimer = null;
            _externalDragOverPanel = false;

            // A drop that landed on the panel re-foregrounded it (Panel_OnDrop), so IsActive is true.
            // Otherwise, spec §6/§10 item 6a: no drag over the panel and not active -> hide as before.
            if (!IsActive)
            {
                HideLauncher();
            }
        };
        _dragDeferTimer.Start();
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

            case PanelPage.Settings:
                SettingsViewHost.FocusFirstField();
                break;

            case PanelPage.StartMenuPicker:
                StartMenuPickerViewHost.FocusFirstField();
                break;

            case PanelPage.List:
            case PanelPage.ConfirmDelete:
            case PanelPage.ConfirmLaunch:
            case PanelPage.ConfirmImport:
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
    /// centered correctly once WPF renders it at whichever DPI the window ends up on. The vertical placement
    /// follows settings.position: a third of the way down (Center), or flush with the top/bottom edge of the
    /// work area minus settings.edgeMargin. Bottom also records <see cref="_bottomAnchorPx"/> so later height
    /// changes keep the bottom edge in place.
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
        var effectiveHeight = ActualHeight * targetScale / currentScale;
        var margin = Math.Clamp(_viewModel.EdgeMargin, 0, Settings.MaxEdgeMargin);

        Left = workLeft + (workWidth - effectiveWidth) / 2.0;
        _bottomAnchorPx = null;
        switch (_viewModel.PanelPosition)
        {
            case PanelPosition.Top:
                Top = workTop + margin;
                break;

            case PanelPosition.Bottom:
                Top = workTop + workHeight - margin - effectiveHeight;
                _bottomAnchorPx = (workTop + workHeight - margin) * targetScale;
                break;

            default:
                Top = workTop + workHeight / 3.0;
                break;
        }
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
    /// solid background (theme brush), square corners, <see cref="AllowsTransparency"/> stays false
    /// throughout (a layered/transparent window breaks the DWM backdrop entirely). Also wires up the
    /// WM_SETTINGCHANGE hook (spec §10 item 4) that keeps Theme.System following the OS live.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            HwndSource.FromHwnd(hwnd)?.AddHook(SettingChangeWndProc);
        }

        if (_docked)
        {
            ApplyDockedWindowAttributes();
        }
        else
        {
            ApplyWindowChromeAndBackdrop();
        }

        ApplyTheme(_themeService.IsDark);
    }

    /// <summary>
    /// Docked style, called once from the constructor (before the HWND exists): turns the window into a transparent
    /// layered one and wraps the panel (<see cref="RootBorder"/>) in a root grid together with the two concave fillets.
    /// The panel keeps its opaque theme background (PanelBackgroundBrush) - there is no Mica and no WindowChrome here -
    /// and the window gets a transparent margin of <see cref="FilletRadius"/> on each side to draw the fillets in.
    /// </summary>
    private void BuildDockedChrome()
    {
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = PanelWidth + 2 * FilletRadius;

        var grid = (Grid)RootBorder.Child;
        _baseRows = grid.Children.Cast<UIElement>().ToDictionary(child => child, Grid.GetRow);
        grid.SizeChanged += (_, _) => UpdateDockedClip();

        Content = null;
        RootBorder.BorderThickness = new Thickness(0);
        RootBorder.Margin = new Thickness(FilletRadius, 0, FilletRadius, 0);
        RenderOptions.SetClearTypeHint(RootBorder, ClearTypeHint.Enabled); // ClearType is only honoured on opaque pixels.

        _slide = new TranslateTransform();
        _filletLeft = CreateFillet(mirrorX: false);
        _filletRight = CreateFillet(mirrorX: true);
        _dockedRoot = new Grid { RenderTransform = _slide };
        _dockedRoot.Children.Add(RootBorder);
        _dockedRoot.Children.Add(_filletLeft);
        _dockedRoot.Children.Add(_filletRight);
        Content = _dockedRoot;

        ApplyDockedShape();
    }

    private static System.Windows.Shapes.Path CreateFillet(bool mirrorX)
    {
        var fillet = new System.Windows.Shapes.Path
        {
            Width = FilletRadius + 1,
            Height = FilletRadius,
            HorizontalAlignment = mirrorX ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
        fillet.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "PanelBackgroundBrush");
        return fillet;
    }

    /// <summary>
    /// Docked style: shapes the panel after settings.position (re-run live when it changes). Bottom/Top square the
    /// corners on the docked edge, round the other two and add the fillets there; Center rounds all four with no
    /// fillets. At the bottom the search box also moves to the bottom, with the list above it.
    /// </summary>
    private void ApplyDockedShape()
    {
        var position = _viewModel.PanelPosition;
        RootBorder.CornerRadius = position switch
        {
            PanelPosition.Bottom => new CornerRadius(PanelCornerRadius, PanelCornerRadius, 0, 0),
            PanelPosition.Top => new CornerRadius(0, 0, PanelCornerRadius, PanelCornerRadius),
            _ => new CornerRadius(PanelCornerRadius),
        };

        var top = position == PanelPosition.Top;
        foreach (var (fillet, mirrorX) in new[] { (_filletLeft!, false), (_filletRight!, true) })
        {
            fillet.Visibility = position == PanelPosition.Center ? Visibility.Collapsed : Visibility.Visible;
            fillet.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            fillet.Data = CreateFilletGeometry(mirrorX, top);
        }

        ApplyDockedRowOrder();
        UpdateDockedClip();
    }

    /// <summary>
    /// One fillet: a (r + 1) x r box beside the panel, filled except for a quarter circle of radius r centered on the box's
    /// outer corner on the open side (the top-outer corner for a bottom-docked panel), so the panel's side curves
    /// outwards into the bar it is docked to. The box runs 1 DIP under the panel to avoid an anti-aliasing seam.
    /// Drawn for the left side with the panel to its right; <paramref name="mirrorX"/> flips it for the right side and
    /// <paramref name="top"/> for a top-docked panel (a flip reverses the arc's sweep direction).
    /// </summary>
    private static Geometry CreateFilletGeometry(bool mirrorX, bool top)
    {
        const double r = FilletRadius;
        const double w = r + 1;
        double X(double x) => mirrorX ? w - x : x;
        double Y(double y) => top ? r - y : y;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(X(r), Y(0)), isFilled: true, isClosed: true);
            context.LineTo(new Point(X(w), Y(0)), isStroked: false, isSmoothJoin: false);
            context.LineTo(new Point(X(w), Y(r)), isStroked: false, isSmoothJoin: false);
            context.LineTo(new Point(X(0), Y(r)), isStroked: false, isSmoothJoin: false);
            context.ArcTo(
                new Point(X(r), Y(0)),
                new Size(r, r),
                rotationAngle: 0,
                isLargeArc: false,
                mirrorX ^ top ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                isStroked: false,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Docked + Bottom, list pages only: moves the search row next to the bar it is docked to. Top to bottom the rows
    /// become list, error line, hint/confirm bar, separator, search - so the hint/confirm bars keep their top separator
    /// line and sit right above the search box. Other pages (settings, editor, pickers) are forms and keep the XAML
    /// order, header on top. Re-evaluated when the page or settings.position changes.
    /// </summary>
    private void ApplyDockedRowOrder()
    {
        var searchAtBottom = _viewModel.PanelPosition == PanelPosition.Bottom && _viewModel.IsListPage;
        if (searchAtBottom == _rowsReversed || _baseRows is null)
        {
            return;
        }

        _rowsReversed = searchAtBottom;
        foreach (var (child, row) in _baseRows)
        {
            Grid.SetRow(child, searchAtBottom ? SearchAtBottomRows[row] : row);
        }
    }

    /// <summary>XAML row (0 search/header, 1 separator, 2 list/page, 3 error line, 4 hint/confirm bars) -> row with the search at the bottom.</summary>
    private static readonly int[] SearchAtBottomRows = [4, 3, 0, 1, 2];

    /// <summary>
    /// Docked: clips the panel content to the panel's own outline. A Border does not clip its child to its CornerRadius;
    /// in the floating style DWM's rounded window corners hide that, but a layered window shows a full-width selected
    /// row or bar poking out past the rounded corners. The docked edge stays square (the half-height rectangle unioned
    /// with the rounded one squares those two corners).
    /// </summary>
    private void UpdateDockedClip()
    {
        var content = (FrameworkElement)RootBorder.Child;
        var width = content.ActualWidth;
        var height = content.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Geometry clip = new RectangleGeometry(new Rect(0, 0, width, height), PanelCornerRadius, PanelCornerRadius);
        var position = _viewModel.PanelPosition;
        if (position != PanelPosition.Center)
        {
            var squareHalf = position == PanelPosition.Bottom
                ? new Rect(0, height / 2, width, height / 2)
                : new Rect(0, 0, width, height / 2);
            clip = new CombinedGeometry(GeometryCombineMode.Union, clip, new RectangleGeometry(squareHalf));
        }

        clip.Freeze();
        content.Clip = clip;
    }

    /// <summary>
    /// Docked + Top/Bottom: clears any running slide and, when the slide will play, parks the content one panel height
    /// beyond its edge so the first frame after Show() is already off-screen. Returns whether to animate.
    /// </summary>
    private bool PrepareSlideIn()
    {
        StopSlide();
        var height = _dockedRoot!.ActualHeight;
        if (_viewModel.PanelPosition == PanelPosition.Center || !SystemParameters.ClientAreaAnimation || height <= 0)
        {
            return false;
        }

        _slide!.Y = SlideOffset(height);
        return true;
    }

    /// <summary>Slides the content (panel and fillets) from beyond the docked edge to its place. The window itself never moves: its Top is rewritten by the bottom anchor on every height change.</summary>
    private void BeginSlideIn()
    {
        var height = _dockedRoot!.ActualHeight;
        if (height <= 0)
        {
            StopSlide();
            return;
        }

        var animation = new DoubleAnimation(SlideOffset(height), 0, SlideDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        _slide!.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Starts below the panel when docked at the bottom, above it at the top.</summary>
    private double SlideOffset(double height) => _viewModel.PanelPosition == PanelPosition.Bottom ? height : -height;

    private void StopSlide()
    {
        _slide!.BeginAnimation(TranslateTransform.YProperty, null);
        _slide.Y = 0;
    }

    /// <summary>Docked style: DWM must not round the (transparent) window's corners itself; the panel draws its own shape.</summary>
    private void ApplyDockedWindowAttributes()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || Environment.OSVersion.Version.Build < 22000)
            {
                return;
            }

            var corner = Win32.DWMWCP_DONOTROUND;
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Debug.WriteLine($"[YourLauncher] Could not disable the DWM corner rounding: {ex.Message}");
        }
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

            // A live resource reference, set once: from here on, any theme change (ThemeService.Apply
            // swapping the merged dictionary) re-tints this background automatically with zero extra code
            // - see ApplyTheme below, which therefore only needs to deal with the DWM-level dark flag.
            RootBorder.SetResourceReference(BackgroundProperty, "PanelBackgroundTintBrush");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Debug.WriteLine($"[YourLauncher] Mica/Acrylic setup failed, keeping the solid fallback look: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-applies the DWM immersive-dark-mode flag for <paramref name="dark"/> (spec §10 item 2-3). Called
    /// once at startup (after <see cref="ApplyWindowChromeAndBackdrop"/> determines whether the backdrop is
    /// active) and again whenever the effective theme changes afterwards (a settings save, an external
    /// config reload that changes <c>settings.theme</c>, or a live Theme.System OS change). Every other
    /// theme-dependent color updates itself via the live <c>DynamicResource</c>/<c>SetResourceReference</c>
    /// bindings already in place once <see cref="ThemeService.Apply"/> swaps the merged dictionary - this
    /// method only needs to poke the one thing DWM itself needs told explicitly.
    /// </summary>
    public void ApplyTheme(bool dark)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || Environment.OSVersion.Version.Build < 22000)
        {
            return;
        }

        var darkMode = dark ? 1 : 0;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
    }

    /// <summary>
    /// Spec §10 item 4: WM_SETTINGCHANGE with lParam "ImmersiveColorSet" fires whenever the user flips
    /// Windows' light/dark app mode, and WM_DWMCOLORIZATIONCOLORCHANGED when the accent color changes -
    /// re-applies what is currently applied, but only while that depends on Windows (Theme.System or the
    /// Windows accent; Light/Dark with the palette accent are pinned regardless of what the OS does).
    /// Re-applying the *last applied* values rather than the saved config also keeps a live settings preview intact.
    /// </summary>
    private IntPtr SettingChangeWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var windowsColorsChanged = msg == WM_DWMCOLORIZATIONCOLORCHANGED
            || (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet");
        if (windowsColorsChanged && _themeService.FollowsWindowsColors)
        {
            ApplyTheme(_themeService.Reapply());
        }

        return IntPtr.Zero;
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

        if (_viewModel.CurrentPage == PanelPage.ConfirmLaunch)
        {
            // Same "Enter confirms, any other key cancels" rule as ConfirmDelete above.
            if (e.Key == Key.Enter)
            {
                _viewModel.ConfirmPendingLaunch();
            }
            else
            {
                _viewModel.CancelPendingLaunch();
            }

            e.Handled = true;
            return;
        }

        if (_viewModel.CurrentPage == PanelPage.ConfirmImport)
        {
            // M merges, R replaces; anything else (including Esc) cancels (spec §10 revision item 9,
            // same "any other key cancels" pattern as ConfirmDelete above).
            switch (e.Key)
            {
                case Key.M:
                    _viewModel.ConfirmImportMerge();
                    break;
                case Key.R:
                    _viewModel.ConfirmImportReplace();
                    break;
                default:
                    _viewModel.CancelPendingImport();
                    break;
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        {
            _viewModel.BeginSettings();
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
        // Esc never navigates up a folder (user request 2026-09-27) - Backspace on an empty search does that.
        if (!string.IsNullOrEmpty(_viewModel.SearchText))
        {
            _viewModel.SearchText = "";
        }
        else
        {
            HideLauncher();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Mouse prerequisite (spec §10 item 5): ListBoxItem is Focusable=False (see MainWindow.xaml), so these
    // handlers are the only way a mouse click affects selection - left-click selects, double-click opens,
    // right-click selects then opens the context menu (spec §5).
    // ---------------------------------------------------------------------------------------------

    private void ListItem_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // List page only - ConfirmDelete/ConfirmImport keep the list visible, but a pending confirmation
        // must not have its selection changed underneath it.
        if (_viewModel.CurrentPage != PanelPage.List || sender is not ListBoxItem { DataContext: ListItemViewModel item } container)
        {
            return;
        }

        SelectContainer(container);

        // Anchor for the in-list drag threshold (spec §7) - a plain click never crosses it, so this never
        // fires DoDragDrop by itself; ListItem_OnPreviewMouseMove/Up below decide that.
        _listDragStartPoint = e.GetPosition(null);
        _listDragCandidate = item;
    }

    private void ListItem_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _listDragCandidate = null;

    private void ListItem_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Clear the drag anchor first: entering a folder replaces every row while the button may still be
        // down, and a drag from that stale candidate would move the folder we just entered.
        _listDragCandidate = null;
        if (_viewModel.CurrentPage == PanelPage.List && sender is ListBoxItem { DataContext: ListItemViewModel })
        {
            _viewModel.EnterSelected();
        }
    }

    private void ListItem_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.CurrentPage != PanelPage.List || sender is not ListBoxItem { DataContext: ListItemViewModel item } container)
        {
            e.Handled = true;
            return;
        }

        SelectContainer(container);
        e.Handled = true; // stop this from also reaching ItemsList_OnMouseRightButtonUp (empty-area menu).
        ShowItemContextMenu(container, item);
    }

    /// <summary>Right-click on empty list space, or on the empty-folder message - spec §10 item 7's "Paste here / New item / New folder" menu.</summary>
    private void ItemsList_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_viewModel.CurrentPage == PanelPage.List)
        {
            ShowEmptyAreaContextMenu((UIElement)sender);
        }
    }

    private void SelectContainer(ListBoxItem container)
    {
        var index = ItemsList.ItemContainerGenerator.IndexFromContainer(container);
        if (index >= 0)
        {
            _viewModel.SelectedIndex = index;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Context menu (spec §5, revised §10 item 7): each item calls the exact same MainViewModel method the
    // matching keyboard shortcut does - no duplicated logic. Built fresh from ThemedMenuFactory (shared
    // with TrayService) on every open so it always reflects the current theme and read-only state.
    // ---------------------------------------------------------------------------------------------

    private void ShowItemContextMenu(ListBoxItem placementTarget, ListItemViewModel item)
    {
        var readOnly = _viewModel.IsReadOnly;
        var style = ThemedMenuFactory.CreateMenuItemStyle();
        var menu = new ContextMenu { Style = ThemedMenuFactory.CreateMenuStyle(), OverridesDefaultStyle = true, PlacementTarget = placementTarget, Placement = PlacementMode.MousePoint };

        // Provider rows (calculator, system commands, ...) are not tree nodes: only Open applies to them.
        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuOpen, style, () => _viewModel.EnterSelected(), enabled: item.CanLaunch));
        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuEdit, style, () => _viewModel.BeginEditSelected(), "F2", enabled: !readOnly && item.CanEdit));
        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuChangeIcon, style, () => _viewModel.BeginChangeIcon(), "Ctrl+I", enabled: !readOnly && item.CanEdit));
        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuCut, style, () => _viewModel.CutSelected(), "Ctrl+X", enabled: !readOnly && item.IsNode));
        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuPasteHere, style, () => _viewModel.PasteIntoCurrentFolder(), "Ctrl+V", enabled: !readOnly && _viewModel.CutNode is not null));
        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuDuplicate, style, () => _viewModel.DuplicateSelected(), "Ctrl+D", enabled: !readOnly && item.IsNode));
        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuDelete, style, () => _viewModel.BeginDelete(), "Del", enabled: !readOnly && item.IsNode));
        menu.Items.Add(ThemedMenuFactory.CreateSeparator(ThemedMenuFactory.CreateSeparatorStyle()));

        var canOpenLocation = TryResolveOpenLocationPath(item.Node, out var locationPath);
        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuOpenFileLocation, style, () => OpenFileLocation(locationPath), enabled: canOpenLocation));

        OpenContextMenu(menu);
    }

    private void ShowEmptyAreaContextMenu(UIElement placementTarget)
    {
        var readOnly = _viewModel.IsReadOnly;
        var style = ThemedMenuFactory.CreateMenuItemStyle();
        var menu = new ContextMenu { Style = ThemedMenuFactory.CreateMenuStyle(), OverridesDefaultStyle = true, PlacementTarget = placementTarget, Placement = PlacementMode.MousePoint };

        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuPasteHere, style, () => _viewModel.PasteIntoCurrentFolder(), "Ctrl+V", enabled: !readOnly && _viewModel.CutNode is not null));
        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuNewItem, style, () => _viewModel.BeginAddNode(), "Ctrl+N", enabled: !readOnly));
        menu.Items.Add(ThemedMenuFactory.CreateItem(Strings.MenuNewFolder, style, () => _viewModel.BeginAddFolderDirect(), "Ctrl+Shift+N", enabled: !readOnly));

        OpenContextMenu(menu);
    }

    /// <summary>Spec §5: "Opening the menu must not trigger auto-hide" - same suppression counter the Browse…/tray menus already use.</summary>
    private void OpenContextMenu(ContextMenu menu)
    {
        BeginSuppressAutoHide();
        menu.Closed += (_, _) => EndSuppressAutoHide();
        menu.IsOpen = true;
    }

    /// <summary>"Open file location" (spec §10 item 7): app/path targets only, resolved with the same EnvExpander + PathResolver/TargetCheck chain the missing-target badge uses, so both always agree on what's actually there.</summary>
    private static bool TryResolveOpenLocationPath(Node? node, out string path)
    {
        var target = node switch
        {
            AppNode app => app.Target,
            PathNode pathNode => pathNode.Target,
            _ => null,
        };

        var resolved = target is null ? null : TargetCheck.ResolveExistingPath(target);
        path = resolved ?? "";
        return resolved is not null;
    }

    /// <summary>Explorer /select highlights the item in its parent folder - the same call works whether the resolved path is itself a file or a directory (spec: "for a directory target open its parent with it selected").</summary>
    private static void OpenFileLocation(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return; // menu item is disabled in this case; defensive only.
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Debug.WriteLine($"[YourLauncher] Could not open file location for '{path}': {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Drag & drop from Explorer/Start menu (spec §6/AC12, revised §10 item 6): accepted only on the List
    // page, only DataFormats.FileDrop - the private in-list-drag format below is handled at the
    // ListBoxItem level and marks the event Handled so it never reaches these Window-level handlers.
    // ---------------------------------------------------------------------------------------------

    private void Panel_OnDragEnter(object sender, DragEventArgs e)
    {
        // FileDrop only: an in-list drag also bubbles DragEnter up here, but its Drop is handled at the row
        // and never reaches Panel_OnDrop to clear the flag - a stale true would keep a later plain click on
        // another window from hiding the panel (spec §10 item 6b).
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            _externalDragOverPanel = true;

            // Spec §6: read-only rejects with the error line - a rejected drag never gets a Drop, so say it now.
            if (_viewModel.CurrentPage == PanelPage.List && _viewModel.IsReadOnly)
            {
                _viewModel.ShowReadOnlyError();
            }
        }

        ApplyFileDropEffect(e);
    }

    private void Panel_OnDragOver(object sender, DragEventArgs e) => ApplyFileDropEffect(e);

    private void Panel_OnDragLeave(object sender, DragEventArgs e) => _externalDragOverPanel = false;

    private void ApplyFileDropEffect(DragEventArgs e)
    {
        if (_viewModel.CurrentPage != PanelPage.List || _viewModel.IsReadOnly || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey
            ? DragDropEffects.Link
            : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Panel_OnDrop(object sender, DragEventArgs e)
    {
        _externalDragOverPanel = false;

        if (_viewModel.CurrentPage != PanelPage.List || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>();
        _viewModel.AddNodesFromDrop(paths);

        // Spec §10 item 6b: after a drop the panel must end up foreground or hidden - never a visible
        // deactivated Topmost panel. The Explorer drag deactivated us to get here; take the foreground back
        // now that the drop is handled (same trick RequestShow/ForceForeground already use).
        ForceForeground();
    }

    // ---------------------------------------------------------------------------------------------
    // In-list drag (spec §7, revised §10 item 8): nav mode only, List page only, disabled read-only -
    // DragDrop.DoDragDrop with a private format (InternalDragFormat) so it never collides with the
    // Explorer FileDrop path above. TreeOps.MoveToGroupIndex/MoveTo do the actual tree edit (MainViewModel.
    // MoveDraggedNode); this class only tracks the drag gesture and the per-row visual indicator.
    // ---------------------------------------------------------------------------------------------

    private void ListItem_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _listDragCandidate is null ||
            !ReferenceEquals((sender as FrameworkElement)?.DataContext, _listDragCandidate))
        {
            return;
        }

        if (_viewModel.IsSearchMode || _viewModel.CurrentPage != PanelPage.List || _viewModel.IsReadOnly)
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _listDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _listDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _listDragCandidate;
        _listDragCandidate = null; // DoDragDrop below runs its own message loop - one drag per press.

        if (dragged.Node is not { } draggedNode)
        {
            return; // provider rows are not tree members - nothing to move.
        }

        var data = new DataObject(InternalDragFormat, draggedNode.Id);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        ClearDropIndicators();
    }

    private void ListItem_OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(InternalDragFormat) || sender is not ListBoxItem { DataContext: ListItemViewModel targetItem } container)
        {
            return; // not our format - let it bubble to Panel_OnDragOver untouched.
        }

        e.Handled = true;

        var sourceId = (string)e.Data.GetData(InternalDragFormat)!;
        if (targetItem.Node is not { } targetNode || sourceId == targetNode.Id)
        {
            e.Effects = DragDropEffects.None;
            ClearDropIndicators();
            return;
        }

        e.Effects = DragDropEffects.Move;

        var indicator = IndicatorAt(e, container, targetItem);
        ClearDropIndicators();
        targetItem.DropIndicator = indicator;
    }

    private static DropIndicator IndicatorAt(DragEventArgs e, ListBoxItem container, ListItemViewModel targetItem)
    {
        var verticalFraction = e.GetPosition(container).Y / container.ActualHeight;
        return targetItem.IsFolder
            ? verticalFraction switch
            {
                <= 0.25 => DropIndicator.Above,
                >= 0.75 => DropIndicator.Below,
                _ => DropIndicator.Into, // spec §7: a folder's middle 50% = "move into".
            }
            : verticalFraction < 0.5 ? DropIndicator.Above : DropIndicator.Below;
    }

    private void ListItem_OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ListItemViewModel item })
        {
            item.DropIndicator = DropIndicator.None;
        }
    }

    private void ListItem_OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(InternalDragFormat) || sender is not ListBoxItem { DataContext: ListItemViewModel targetItem } container)
        {
            return;
        }

        e.Handled = true;
        ClearDropIndicators();

        // Recomputed from the drop point, not read from the row: DragLeave between the row's own child
        // elements resets DropIndicator until the next DragOver, so the stored value can be stale here.
        var indicator = IndicatorAt(e, container, targetItem);

        if (targetItem.Node is not { } targetNode)
        {
            return;
        }

        var sourceId = (string)e.Data.GetData(InternalDragFormat)!;
        _viewModel.MoveDraggedNode(sourceId, targetNode.Id, indicator);
    }

    private void ClearDropIndicators()
    {
        foreach (var item in _viewModel.Items)
        {
            item.DropIndicator = DropIndicator.None;
        }
    }
}
