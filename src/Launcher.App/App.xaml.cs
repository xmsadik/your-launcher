using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using YourLauncher.App.Services;
using YourLauncher.App.ViewModels;
using YourLauncher.App.Views;

namespace YourLauncher.App;

/// <summary>
/// Manual composition root (no DI container needed for this size of app). Keeps the process alive via
/// ShutdownMode.OnExplicitShutdown since there's no tray icon yet (Phase 5) - Ctrl+Q in the panel is the
/// exit path until then.
/// </summary>
public partial class App : Application
{
    private HotkeyService? _hotkeyService;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var configService = new ConfigService();
        var launchService = new LaunchService();
        var searchService = new SearchService(configService.Config);
        var viewModel = new MainViewModel(configService, launchService, searchService, LookUpExeDescription);

        _mainWindow = new MainWindow(viewModel);
        viewModel.RequestHide += () => _mainWindow?.HideLauncher();

        // Force HWND creation without showing the window yet, so RegisterHotKey has a handle to bind to.
        _ = new WindowInteropHelper(_mainWindow).EnsureHandle();

        _hotkeyService = new HotkeyService();
        var hotkeyText = configService.Config.Settings.Hotkey;
        if (!_hotkeyService.TryRegister(_mainWindow, hotkeyText))
        {
            viewModel.ErrorMessage =
                $"Hotkey {hotkeyText} is in use by another app. Change it in config.json (settings.hotkey).";
        }
        else
        {
            _hotkeyService.HotkeyPressed += () => _mainWindow?.ToggleLauncher();
        }

        // Show once at startup so the user can see the launcher works; afterwards the hotkey toggles it.
        _mainWindow.ShowLauncher();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mainWindow is not null)
        {
            _hotkeyService?.Unregister(_mainWindow);
        }

        base.OnExit(e);
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
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or System.IO.IOException)
        {
            return null;
        }
    }
}
