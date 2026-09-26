using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using YourLauncher.App.Interop;
using YourLauncher.App.Services;
using YourLauncher.App.ViewModels;
using YourLauncher.App.Views;

namespace YourLauncher.App;

/// <summary>
/// Manual composition root (no DI container needed for this size of app). Phase 5 (spec §10) adds: a
/// single-instance guard (must run before anything else - a second instance never gets this far), the
/// tray icon, the "Start with Windows" registry sync, and the config file watcher. ShutdownMode stays
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
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private string _lastHotkeyText = "";

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

        var searchService = new SearchService(configService.Config);
        var iconService = new IconService(configDir, Win32.GetDpiForSystem() / 96.0);

        var viewModel = new MainViewModel(configService, launchService, searchService, iconService, LookUpExeDescription);
        _viewModel = viewModel;
        viewModel.ReloadFailed += OnViewModelReloadFailed;

        _mainWindow = new MainWindow(viewModel);
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
        }

        _startupService = new StartupService();
        _startupService.Sync(configService.Config.Settings.StartWithWindows);

        configService.ConfigReloaded += OnConfigReloadedAppLevel;

        _configWatcher = new ConfigWatcherService(configService);
        _configWatcher.ExternalChangeDetected += () => viewModel.RequestReload();
        _configWatcher.Start();

        // Show once at startup so the user can see the launcher works; afterwards the hotkey toggles it.
        _mainWindow.ShowLauncher();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mainWindow is not null)
        {
            _hotkeyService?.Unregister(_mainWindow);
        }

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
        tray.SettingsRequested += OpenConfigFile; // Phase 6: open the real settings page instead.
        tray.OpenConfigFileRequested += OpenConfigFile;
        tray.OpenConfigFolderRequested += OpenConfigFolder;
        tray.ReloadConfigRequested += () => _viewModel?.RequestReload();
        tray.RecoverFromCorruptionRequested += () => _viewModel?.RecoverFromCorruption();
        tray.ExitRequested += Shutdown;
        tray.BalloonClicked += kind =>
        {
            if (kind == TrayBalloonKind.HotkeyFailure)
            {
                OpenConfigFile(); // Phase 6: open the settings page (hotkey field) instead.
            }
        };
        tray.IsRecoveryAvailable = () => _viewModel?.CanRecoverFromCorruption ?? false;
        tray.RecoveryMenuText = () => _viewModel?.RecoveryMenuText ?? "Restore from backup";
        tray.MenuOpening += () => _mainWindow?.BeginSuppressAutoHide();
        tray.MenuClosed += () => _mainWindow?.EndSuppressAutoHide();
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

    /// <summary>Hotkey/startup-registry side effects of a reload (spec §4: "re-register hotkey if changed, re-sync startup registry") - kept at the App level since MainViewModel deliberately holds no Win32/HotkeyService/StartupService references.</summary>
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
    }

    /// <summary>Spec §10 item 13: unregister the old hotkey first; if the new one fails, try to restore the old one so the app never ends up with no working hotkey at all.</summary>
    private void ReRegisterHotkey(string newHotkeyText)
    {
        _hotkeyService!.Unregister(_mainWindow!);

        if (_hotkeyService.TryRegister(_mainWindow!, newHotkeyText))
        {
            _lastHotkeyText = newHotkeyText;
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

    private void ShowHotkeyFailureBalloon(string message) =>
        _trayService?.ShowBalloon("Your Launcher", message, TrayBalloonKind.HotkeyFailure, warning: true);

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
