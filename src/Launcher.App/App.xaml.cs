using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using YourLauncher.App.Interop;
using YourLauncher.App.Services;
using YourLauncher.App.ViewModels;
using YourLauncher.App.Views;
using YourLauncher.Core.Config;
using YourLauncher.Core.Startup;

namespace YourLauncher.App;

/// <summary>
/// Manual composition root (no DI container needed for this size of app). Phase 5 (spec §10) added: a
/// single-instance guard (must run before anything else - a second instance never gets this far), the
/// tray icon, the "Start with Windows" registry sync, and the config file watcher. Phase 6 adds: the
/// theme service, usage tracking, and the settings page's hotkey-capture/apply seam. ShutdownMode stays
/// explicit (OnExplicitShutdown) even with a tray icon now present, since hiding the panel must never
/// exit the process - only the tray's Exit item, Ctrl+Q, or another OS shutdown does.
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private ConfigService? _configService;
    private ConfigWatcherService? _configWatcher;
    private HotkeyService? _hotkeyService;
    private StartupService? _startupService;
    private TrayService? _trayService;
    private ThemeService? _themeService;
    private UsageService? _usageService;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private string _lastHotkeyText = "";
    private string _lastHotkeyFailureMessage = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var configDir = ConfigService.ResolveConfigDirectory();

        // Single instance (spec §1, revised §10 item 12) must be resolved before any config/window work -
        // a second instance asks the first one to show itself and exits without ever creating a window.
        _singleInstance = new SingleInstanceService(configDir);
        if (!_singleInstance.TryAcquire())
        {
            _singleInstance.NotifyExistingInstanceToShow();
            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        _singleInstance.ShowRequested += () => Dispatcher.BeginInvoke(() => _mainWindow?.RequestShow());
        _singleInstance.StartListening();

        var configService = new ConfigService(configDir);
        _configService = configService;
        var launchService = new LaunchService();
        launchService.HiddenCommandExited += OnHiddenCommandExited;

        // Theme (spec §11/§10 item 2-4): resolved and applied before the window is constructed, so its
        // very first layout already uses the right DynamicResource brushes.
        _themeService = new ThemeService();
        _themeService.Apply(configService.Config.Settings.Theme);

        // Usage (spec §8.4/§10 item 10): constructed before SearchService so its GetScore can be handed
        // straight into the constructor; pruned once here against whatever config.json loaded with.
        var usageService = new UsageService(configDir);
        _usageService = usageService;
        usageService.Prune(ConfigImport.CollectIds(configService.Config.Root).ToHashSet());

        var searchService = new SearchService(configService.Config, usageService.GetScore);
        var iconService = new IconService(configDir, Win32.GetDpiForSystem() / 96.0);

        var viewModel = new MainViewModel(
            configService,
            launchService,
            searchService,
            iconService,
            LookUpExeDescription,
            usageService.RecordLaunch,
            () => usageService.Prune(ConfigImport.CollectIds(configService.Config.Root).ToHashSet()),
            TryApplyHotkey,
            BeginHotkeyCapture,
            EndHotkeyCaptureRestore,
            ShellLinkResolver.Resolve);
        _viewModel = viewModel;
        viewModel.ReloadFailed += OnViewModelReloadFailed;
        viewModel.SettingsApplied += OnSettingsApplied;

        _mainWindow = new MainWindow(viewModel, _themeService, () => _configService!.Config.Settings.Theme);
        viewModel.RequestHide += () => _mainWindow?.HideLauncher();

        // Force HWND creation without showing the window yet, so the hotkey and tray icon have a handle to bind to.
        _ = new WindowInteropHelper(_mainWindow).EnsureHandle();

        _trayService = new TrayService(_mainWindow, TrayTooltip(configService.Config.Settings.Hotkey));
        WireTrayEvents();

        // Subscribe unconditionally (spec §10 item 13) so a hotkey fixed later via a config reload starts
        // working immediately, even if the very first registration attempt at startup failed.
        _hotkeyService = new HotkeyService();
        _hotkeyService.HotkeyPressed += () => _mainWindow?.ToggleLauncher();
        _lastHotkeyText = configService.Config.Settings.Hotkey;
        if (!_hotkeyService.TryRegister(_mainWindow, _lastHotkeyText))
        {
            var message = HotkeyFailureMessage(_lastHotkeyText);
            viewModel.ErrorMessage = message;
            ShowHotkeyFailureBalloon(message);

            // Spec §10 item 13: a startup hotkey failure also opens the panel straight to Settings, with
            // the hotkey box focused and the error already showing - not just a balloon that's easy to miss.
            _mainWindow.ShowLauncher();
            viewModel.OpenSettingsWithHotkeyError(message);
        }

        _startupService = new StartupService();
        _startupService.Sync(configService.Config.Settings.StartWithWindows);

        configService.ConfigReloaded += OnConfigReloadedAppLevel;

        _configWatcher = new ConfigWatcherService(configService);
        _configWatcher.ExternalChangeDetected += () => viewModel.RequestReload();
        _configWatcher.Start();

        // Show once at startup so the user can see the launcher works; afterwards the hotkey toggles it
        // (skipped if the hotkey-failure branch above already showed it on the Settings page, and on a
        // Windows sign-in start, whose Run value passes --silent so the app just sits in the tray).
        var silent = e.Args.Contains(StartupSync.SilentArg, StringComparer.OrdinalIgnoreCase);
        if (!silent && !_mainWindow.IsVisible)
        {
            _mainWindow.ShowLauncher();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mainWindow is not null)
        {
            _hotkeyService?.Unregister(_mainWindow);
        }

        _usageService?.Dispose(); // flushes any pending debounced write (spec §10 item 10).
        _trayService?.Dispose();
        _configWatcher?.Dispose();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }

    private static string TrayTooltip(string hotkeyText) => $"Your Launcher ({hotkeyText})";

    private void WireTrayEvents()
    {
        var tray = _trayService!;
        tray.ShowRequested += () => _mainWindow?.RequestShow();
        tray.SettingsRequested += OpenSettingsPage;
        tray.OpenConfigFileRequested += OpenConfigFile;
        tray.OpenConfigFolderRequested += OpenConfigFolder;
        tray.ReloadConfigRequested += () => _viewModel?.RequestReload();
        tray.RecoverFromCorruptionRequested += () => _viewModel?.RecoverFromCorruption();
        tray.ExitRequested += Shutdown;
        tray.BalloonClicked += kind =>
        {
            if (kind == TrayBalloonKind.HotkeyFailure)
            {
                OpenSettingsPage();
            }
        };
        tray.IsRecoveryAvailable = () => _viewModel?.CanRecoverFromCorruption ?? false;
        tray.RecoveryMenuText = () => _viewModel?.RecoveryMenuText ?? "Restore from backup";
        tray.MenuOpening += () => _mainWindow?.BeginSuppressAutoHide();
        tray.MenuClosed += () => _mainWindow?.EndSuppressAutoHide();
    }

    /// <summary>Tray's Settings… item, and a click on the hotkey-failure balloon (spec §11: "opening from tray/balloon = ShowLauncher() then navigate to Settings").</summary>
    private void OpenSettingsPage()
    {
        _mainWindow?.ShowLauncher();
        if (string.IsNullOrEmpty(_lastHotkeyFailureMessage))
        {
            _viewModel?.BeginSettings();
        }
        else
        {
            _viewModel?.OpenSettingsWithHotkeyError(_lastHotkeyFailureMessage);
        }
    }

    private void OpenConfigFile()
    {
        if (_configService is null)
        {
            return;
        }

        TryShellOpen(_configService.ConfigPath, "config.json");
    }

    private void OpenConfigFolder()
    {
        if (_configService is null)
        {
            return;
        }

        TryShellOpen(_configService.Directory, "the config folder");
    }

    private static void TryShellOpen(string path, string what)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Debug.WriteLine($"[YourLauncher] Could not open {what}: {ex.Message}");
        }
    }

    private void OnHiddenCommandExited(string name, int exitCode)
    {
        // Raised from LaunchService's background exit-code monitor (spec §10 item 8) - marshal to the UI/tray thread.
        Dispatcher.BeginInvoke(() =>
            _trayService?.ShowBalloon("Your Launcher", $"'{name}' exited with code {exitCode}.", warning: true));
    }

    private void OnViewModelReloadFailed(string error) =>
        _trayService?.ShowBalloon(
            "Your Launcher",
            "config.json has errors — kept the previous version.",
            warning: true);

    /// <summary>
    /// Hotkey/startup-registry/theme side effects of a reload (spec §4: "re-register hotkey if changed,
    /// re-sync startup registry"; §10 revision item 2: "an external reload that changes theme also calls
    /// ThemeService.Apply") - kept at the App level since MainViewModel deliberately holds no Win32/
    /// HotkeyService/StartupService/WPF-resource references.
    /// </summary>
    private void OnConfigReloadedAppLevel()
    {
        if (_configService is null || _mainWindow is null)
        {
            return;
        }

        var newHotkey = _configService.Config.Settings.Hotkey;
        if (!string.Equals(newHotkey, _lastHotkeyText, StringComparison.Ordinal))
        {
            ReRegisterHotkey(newHotkey);
        }

        _startupService?.Sync(_configService.Config.Settings.StartWithWindows);

        var dark = _themeService!.Apply(_configService.Config.Settings.Theme);
        _mainWindow.ApplyTheme(dark);
    }

    /// <summary>Settings-page save (spec §10 item 2): the hotkey itself is already applied by <see cref="TryApplyHotkey"/> before this fires - only the Win32/WPF-resource side effects MainViewModel can't reach directly are left.</summary>
    private void OnSettingsApplied()
    {
        if (_configService is null || _mainWindow is null)
        {
            return;
        }

        _startupService?.Sync(_configService.Config.Settings.StartWithWindows);
        var dark = _themeService!.Apply(_configService.Config.Settings.Theme);
        _mainWindow.ApplyTheme(dark);
    }

    /// <summary>Spec §10 item 13: unregister the old hotkey first; if the new one fails, try to restore the old one so the app never ends up with no working hotkey at all.</summary>
    private void ReRegisterHotkey(string newHotkeyText)
    {
        _hotkeyService!.Unregister(_mainWindow!);

        if (_hotkeyService.TryRegister(_mainWindow!, newHotkeyText))
        {
            _lastHotkeyText = newHotkeyText;
            _lastHotkeyFailureMessage = "";
            _trayService?.UpdateTooltip(TrayTooltip(newHotkeyText));
            return;
        }

        var restored = !string.IsNullOrEmpty(_lastHotkeyText) && _hotkeyService.TryRegister(_mainWindow!, _lastHotkeyText);
        var message = HotkeyFailureMessage(newHotkeyText);
        if (!restored)
        {
            message += " The previous hotkey could not be restored either.";
        }

        _viewModel!.ErrorMessage = message;
        ShowHotkeyFailureBalloon(message);
    }

    private static string HotkeyFailureMessage(string hotkeyText) =>
        HotkeyService.TryParse(hotkeyText, out _, out _)
            ? $"Hotkey {hotkeyText} is already in use. Open settings to choose another."
            : $"Hotkey '{hotkeyText}' is not a valid key combination.";

    private void ShowHotkeyFailureBalloon(string message)
    {
        _lastHotkeyFailureMessage = message;
        _trayService?.ShowBalloon("Your Launcher", message, TrayBalloonKind.HotkeyFailure, warning: true);
    }

    // ---------------------------------------------------------------------------------------------
    // Settings page seam (spec §10 item 1-2): SettingsViewModel/MainViewModel stay free of
    // HotkeyService/Win32 references, so these three delegates are the only bridge.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The hotkey capture box's GotFocus: unregister the live hotkey so WM_HOTKEY doesn't eat the combination being typed.</summary>
    private void BeginHotkeyCapture() => _hotkeyService?.Unregister(_mainWindow!);

    /// <summary>The hotkey capture box's LostFocus (blur without a save that changed it): re-register whatever is still the current hotkey.</summary>
    private void EndHotkeyCaptureRestore()
    {
        if (_hotkeyService is null || _mainWindow is null || string.IsNullOrEmpty(_lastHotkeyText))
        {
            return;
        }

        _hotkeyService.TryRegister(_mainWindow, _lastHotkeyText);
    }

    /// <summary>Settings page Save (spec §10 item 2): unregisters the old hotkey (a no-op if capture already did), tries the new one, restores the old on failure. Returns an error string on failure, null on success.</summary>
    private string? TryApplyHotkey(string newHotkeyText)
    {
        _hotkeyService!.Unregister(_mainWindow!);

        if (_hotkeyService.TryRegister(_mainWindow!, newHotkeyText))
        {
            _lastHotkeyText = newHotkeyText;
            _lastHotkeyFailureMessage = "";
            _trayService?.UpdateTooltip(TrayTooltip(newHotkeyText));
            return null;
        }

        var restored = !string.IsNullOrEmpty(_lastHotkeyText) && _hotkeyService.TryRegister(_mainWindow!, _lastHotkeyText);
        var message = HotkeyFailureMessage(newHotkeyText);
        if (!restored)
        {
            message += " The previous hotkey could not be restored either.";
        }

        return message;
    }

    /// <summary>
    /// Real exe-description lookup passed into Core's <see cref="YourLauncher.Core.Config.TargetNameHelper"/>
    /// (spec §9 step 3): FileDescription, falling back to ProductName, so Core itself has no
    /// System.Diagnostics.FileVersionInfo dependency. Never throws - a missing/inaccessible/non-PE file
    /// just falls back to the file name (handled by TargetNameHelper itself).
    /// </summary>
    private static string? LookUpExeDescription(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
            {
                return info.FileDescription;
            }

            return string.IsNullOrWhiteSpace(info.ProductName) ? null : info.ProductName;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
