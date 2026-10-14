using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using YourLauncher.App.Interop;
using YourLauncher.App.Localization;
using YourLauncher.App.Plugins;
using YourLauncher.App.Providers;
using YourLauncher.App.Providers.FileSearch;
using YourLauncher.App.Services;
using YourLauncher.App.ViewModels;
using YourLauncher.App.Views;
using YourLauncher.Core.Config;
using YourLauncher.Core.Diagnostics;
using YourLauncher.Core.Hotkeys;
using YourLauncher.Core.Model;
using YourLauncher.Core.Providers;
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
    private PluginProviderHost? _pluginHost;
    private string _lastPluginProblems = "";
    private string _pluginPrefix = "";
    private MainWindow? _mainWindow;
    private string _lastHotkeyText = "";
    private string _lastHotkeyFailureMessage = "";

    /// <summary>True while a hotkey capture box has focus: every global hotkey is unregistered and re-syncs wait for the blur.</summary>
    private bool _hotkeyCaptureActive;

    /// <summary>What the last per-item hotkey balloon said, so the same unchanged problems are reported once, not on every save.</summary>
    private string _lastItemHotkeyProblems = "";
    private ErrorLog? _errorLog;
    private DateTime _lastErrorBalloonUtc = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var configDir = ConfigService.ResolveConfigDirectory();

        // Global safety net, hooked up before anything else can throw: every exception nothing else caught
        // goes to error.log next to config.json. UI-thread ones are then swallowed so the tray app keeps
        // running instead of silently vanishing; a background-thread one can't be stopped, only logged.
        _errorLog = new ErrorLog(configDir);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _errorLog.Write("AppDomain", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()), DateTime.Now);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _errorLog.Write("Task", args.Exception, DateTime.Now);
            args.SetObserved();
        };

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

        // Before ConfigService: it creates config.json on a fresh install, which would then look like an update.
        var updated = VersionMarker.CheckAndRecord(configDir, AppVersion);

        var configService = new ConfigService(configDir);
        _configService = configService;
        var launchService = new LaunchService();
        launchService.HiddenCommandExited += OnHiddenCommandExited;

        // Theme (spec §11/§10 item 2-4): resolved and applied before the window is constructed, so its
        // very first layout already uses the right DynamicResource brushes.
        _themeService = new ThemeService();
        ApplyConfiguredTheme();

        // Usage (spec §8.4/§10 item 10): constructed before SearchService so its GetScore can be handed
        // straight into the constructor; pruned once here against whatever config.json loaded with.
        var usageService = new UsageService(configDir);
        _usageService = usageService;
        usageService.Prune(ConfigImport.CollectIds(configService.Config.Root).ToHashSet());

        var searchService = new SearchService(configService.Config, usageService.GetScore);
        var iconService = new IconService(configDir, Win32.GetDpiForSystem() / 96.0);

        // File search ("f report"): Everything when it is running, otherwise the Windows Search index.
        var fileSearchProvider = new FileSearchProvider(
            [new EverythingBackend(Dispatcher), new WindowsSearchBackend()],
            () => configService.Config.Settings.FileSearchPrefix,
            launchService);

        // Out-of-process plugins (<config dir>\plugins): scanned now, rescanned on every config reload. The host
        // IS the async provider list, so a rescan needs no cooperation from the view model.
        var pluginHost = new PluginProviderHost(configDir, [fileSearchProvider]);
        _pluginHost = pluginHost;
        var pluginProblems = pluginHost.Rescan(configService.Config.Settings.FileSearchPrefix);
        _pluginPrefix = configService.Config.Settings.FileSearchPrefix;

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
            ShellLinkResolver.Resolve,
            () => _startupService?.StatusNote,
            syncProviders: [new CalculatorProvider(), new SystemCommandProvider(), new WindowSwitcherProvider()],
            asyncProviders: pluginHost,
            usageScore: usageService.GetScore,
            previewTheme: PreviewTheme,
            restoreTheme: ApplyConfiguredTheme);
        _viewModel = viewModel;
        viewModel.ReloadFailed += OnViewModelReloadFailed;
        viewModel.SettingsApplied += OnSettingsApplied;

        _mainWindow = new MainWindow(viewModel, _themeService);
        viewModel.RequestHide += () => _mainWindow?.HideLauncher();

        // Force HWND creation without showing the window yet, so the hotkey and tray icon have a handle to bind to.
        _ = new WindowInteropHelper(_mainWindow).EnsureHandle();

        _trayService = new TrayService(_mainWindow, TrayTooltip(configService.Config.Settings.Hotkey));
        WireTrayEvents();

        // Subscribe unconditionally (spec §10 item 13) so a hotkey fixed later via a config reload starts
        // working immediately, even if the very first registration attempt at startup failed.
        _hotkeyService = new HotkeyService();
        _hotkeyService.HotkeyPressed += () => _mainWindow?.ToggleLauncher();
        _hotkeyService.ItemHotkeyPressed += OnItemHotkeyPressed;
        viewModel.ItemHotkeysMayHaveChanged += () => SyncItemHotkeys();
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

        // Per-item hotkeys (after the main one so it wins a clash): registered here, then kept in step by
        // viewModel.ItemHotkeysMayHaveChanged (every tree save), OnConfigReloadedAppLevel, OnSettingsApplied
        // and the capture-box end/restore below.
        SyncItemHotkeys();

        _startupService = new StartupService();
        _startupService.Sync(configService.Config.Settings.StartWithWindows);

        configService.ConfigReloaded += OnConfigReloadedAppLevel;

        _configWatcher = new ConfigWatcherService(configService);
        _configWatcher.ExternalChangeDetected += () => viewModel.RequestReload();
        _configWatcher.Start();

        // Show once at startup so the user can see the launcher works; afterwards the hotkey toggles it
        // (skipped if the hotkey-failure branch above already showed it on the Settings page, and on a
        // Windows sign-in start - the Run value passes --silent, the Store build's StartupTask activation is
        // its equivalent - so the app just sits in the tray).
        var silent = e.Args.Contains(StartupSync.SilentArg, StringComparer.OrdinalIgnoreCase) || PackageContext.LaunchedByStartupTask();
        if (!silent && !_mainWindow.IsVisible)
        {
            _mainWindow.ShowLauncher();
        }

        ReportPluginProblems(pluginProblems);

        // Store updates install silently, so say once what changed (a click opens the release notes).
        if (updated)
        {
            _trayService.ShowBalloon(
                "Your Launcher",
                $"Updated to {AppVersionText}. Click to see what's new.",
                TrayBalloonKind.Updated);
        }
    }

    private static Version AppVersion => typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>Three-part version as tagged on GitHub (v0.1.1), from Directory.Build.props' &lt;Version&gt;.</summary>
    private static string AppVersionText => AppVersion.ToString(3);

    private const string ReleasesUrl = "https://github.com/xmsadik/your-launcher/releases/tag/v";

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mainWindow is not null)
        {
            _hotkeyService?.UnregisterAll(_mainWindow);
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
            else if (kind == TrayBalloonKind.UnhandledError && _errorLog is not null)
            {
                TryShellOpen(_errorLog.FilePath, "error.log");
            }
            else if (kind == TrayBalloonKind.Updated)
            {
                OpenReleaseNotes();
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
            // Explorer/the user's editor run outside the Store package, so they need the physical path.
            Process.Start(new ProcessStartInfo(PackageContext.ToPhysicalPath(path)) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Debug.WriteLine($"[YourLauncher] Could not open {what}: {ex.Message}");
        }
    }

    /// <summary>A URL, so not through <see cref="TryShellOpen"/> - that maps %APPDATA% file paths for the package.</summary>
    private static void OpenReleaseNotes()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ReleasesUrl + AppVersionText) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"[YourLauncher] Could not open the release notes: {ex.Message}");
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logged = _errorLog?.Write("Dispatcher", e.Exception, DateTime.Now) ?? false;

        if (_trayService is null)
        {
            // Failed before startup finished (no tray icon yet): there is no half-working app worth keeping,
            // so say where the details are and exit instead of leaving an invisible process behind.
            MessageBox.Show(
                logged
                    ? $"Your Launcher could not start.\n\nDetails were saved to:\n{_errorLog!.FilePath}"
                    : $"Your Launcher could not start.\n\n{e.Exception.Message}",
                "Your Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
            Shutdown(1);
            return;
        }

        e.Handled = true;

        // One balloon per 30 s at most, so an exception that repeats (e.g. on every render) can't flood the
        // notification area; every occurrence is still logged.
        var now = DateTime.UtcNow;
        if (now - _lastErrorBalloonUtc >= TimeSpan.FromSeconds(30))
        {
            _lastErrorBalloonUtc = now;
            _trayService.ShowBalloon(
                "Your Launcher",
                logged ? "Something went wrong, but the launcher is still running. Click to see the details." : $"Something went wrong: {e.Exception.Message}",
                logged ? TrayBalloonKind.UnhandledError : TrayBalloonKind.None,
                warning: true);
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
    /// One tray balloon (and an error.log entry) per DISTINCT set of skipped plugins: a reload that finds the
    /// same broken plugin again stays quiet, and fixing it resets the memory so a later break is reported anew.
    /// </summary>
    private void RescanPlugins()
    {
        if (_pluginHost is null || _configService is null)
        {
            return;
        }

        _pluginPrefix = _configService.Config.Settings.FileSearchPrefix;
        ReportPluginProblems(_pluginHost.Rescan(_pluginPrefix));
    }

    private void ReportPluginProblems(IReadOnlyList<string> problems)
    {
        var text = string.Join("\n", problems);
        if (text == _lastPluginProblems)
        {
            return;
        }

        _lastPluginProblems = text;
        if (problems.Count == 0)
        {
            return;
        }

        _errorLog?.Write("Plugins", new InvalidDataException(text), DateTime.Now);
        _trayService?.ShowBalloon("Your Launcher", Strings.PluginsSkipped + "\n" + text, warning: true);
    }

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

        SyncItemHotkeys(); // the reloaded tree may carry different (or conflicting) item hotkeys.

        if (_pluginHost is not null)
        {
            RescanPlugins();
        }

        _startupService?.Sync(_configService.Config.Settings.StartWithWindows);

        ApplyConfiguredTheme();
    }

    /// <summary>
    /// Applies the *saved* theme, opacity and Windows-accent setting (and the DWM dark flag, once the window
    /// exists). The one place that turns config into a look: startup, reload, settings save, and the
    /// settings page's "put the saved look back" after a live preview all come through here.
    /// </summary>
    private void ApplyConfiguredTheme()
    {
        var settings = _configService!.Config.Settings;
        var dark = _themeService!.Apply(settings.Theme, settings.Opacity, settings.UseWindowsAccent);
        _mainWindow?.ApplyTheme(dark);
    }

    /// <summary>Live preview from the settings page (not saved): same path as the real thing, just with the page's unsaved values.</summary>
    private void PreviewTheme(Theme theme, int opacity, bool useWindowsAccent)
    {
        var dark = _themeService!.Apply(theme, opacity, useWindowsAccent);
        _mainWindow?.ApplyTheme(dark);
    }

    /// <summary>Settings-page save (spec §10 item 2): the hotkey itself is already applied by <see cref="TryApplyHotkey"/> before this fires - only the Win32/WPF-resource side effects MainViewModel can't reach directly are left.</summary>
    private void OnSettingsApplied()
    {
        if (_configService is null || _mainWindow is null)
        {
            return;
        }

        _startupService?.Sync(_configService.Config.Settings.StartWithWindows);
        ApplyConfiguredTheme();
        if (_configService.Config.Settings.FileSearchPrefix != _pluginPrefix)
        {
            RescanPlugins(); // a plugin keyword may now collide with (or be freed from) the file-search prefix.
        }

        SyncItemHotkeys(); // settings.hotkey is now final: items that clash with it are skipped, ones that no longer do are registered.
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

    /// <summary>
    /// A hotkey capture box's GotFocus (settings page or item editor): unregister EVERY global hotkey, so
    /// WM_HOTKEY doesn't eat the combination being typed - the panel's, the edited item's, and (so a stray press
    /// can't launch something mid-capture) all other items'.
    /// </summary>
    private void BeginHotkeyCapture()
    {
        _hotkeyCaptureActive = true;
        _hotkeyService?.UnregisterAll(_mainWindow!);
    }

    /// <summary>The capture box's LostFocus (blur without a save that changed it): re-register the current main hotkey and the item hotkeys.</summary>
    private void EndHotkeyCaptureRestore()
    {
        _hotkeyCaptureActive = false;
        if (_hotkeyService is null || _mainWindow is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_lastHotkeyText))
        {
            _hotkeyService.TryRegister(_mainWindow, _lastHotkeyText);
        }

        SyncItemHotkeys(_lastHotkeyText);
    }

    /// <summary>Settings page Save (spec §10 item 2): unregisters the old hotkey (a no-op if capture already did), tries the new one, restores the old on failure. Returns an error string on failure, null on success.</summary>
    private string? TryApplyHotkey(string newHotkeyText)
    {
        _hotkeyCaptureActive = false; // this call resolves the capture; every path below ends with the item hotkeys re-synced.

        // An item already owns that combination: say who, instead of RegisterHotKey's bare "in use".
        if (HotkeyConflicts.FindOwner(newHotkeyText, "", HotkeyConflicts.Collect(_configService!.Config.Root)) is { Node: { } owner })
        {
            _hotkeyService!.TryRegister(_mainWindow!, _lastHotkeyText);
            SyncItemHotkeys(_lastHotkeyText);
            return string.Format(Strings.EditorHotkeyConflictItem, newHotkeyText, owner.Name);
        }

        _hotkeyService!.Unregister(_mainWindow!);

        if (_hotkeyService.TryRegister(_mainWindow!, newHotkeyText))
        {
            _lastHotkeyText = newHotkeyText;
            _lastHotkeyFailureMessage = "";
            _trayService?.UpdateTooltip(TrayTooltip(newHotkeyText));
            SyncItemHotkeys(newHotkeyText);
            return null;
        }

        var restored = !string.IsNullOrEmpty(_lastHotkeyText) && _hotkeyService.TryRegister(_mainWindow!, _lastHotkeyText);
        var message = HotkeyFailureMessage(newHotkeyText);
        if (!restored)
        {
            message += " The previous hotkey could not be restored either.";
        }

        SyncItemHotkeys(_lastHotkeyText);
        return message;
    }

    // ---------------------------------------------------------------------------------------------
    // Per-item hotkeys: registrations follow the tree. SyncItemHotkeys is the single place that turns
    // "which nodes have a hotkey" into Windows registrations; it runs at startup, on every tree save
    // (editor save, delete, paste, duplicate, import, bookmark/Start-menu import, drops, ...), on config
    // reload/recover, after a settings save, and when a capture box gives the hotkeys back.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Registers exactly the hotkeys the tree asks for. First come, first served: the main hotkey owns its
    /// combination, then items in tree order; a later duplicate is skipped and reported, as is any hotkey
    /// that is not a valid combination or that another program already owns. Problems are reported once per
    /// distinct set (tray balloon), so re-syncing on every save does not repeat them.
    /// </summary>
    /// <param name="mainHotkey">The main hotkey as actually registered, when it differs from the saved settings (settings page mid-save).</param>
    private void SyncItemHotkeys(string? mainHotkey = null)
    {
        if (_hotkeyService is null || _mainWindow is null || _configService is null || _hotkeyCaptureActive)
        {
            return; // a capture box has the hotkeys suspended; its blur re-syncs.
        }

        var config = _configService.Config;
        var all = HotkeyConflicts.Collect(config.Root);
        var conflicts = HotkeyConflicts.Find(mainHotkey ?? config.Settings.Hotkey, all);
        var skipped = conflicts.Select(c => c.Node).ToHashSet();
        var wanted = all.Where(h => !skipped.Contains(h.Node)).Select(h => new ItemHotkeyRequest(h.Node.Id, h.Hotkey)).ToList();

        var failures = _hotkeyService.SyncItems(_mainWindow, wanted);

        var names = all.ToDictionary(h => h.Node.Id, h => h.Node.Name, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var conflict in conflicts)
        {
            problems.Add(conflict.Other is { } other
                ? string.Format(Strings.HotkeyItemConflictItem, conflict.Node.Name, conflict.Hotkey, other.Name)
                : string.Format(Strings.HotkeyItemConflictMain, conflict.Node.Name, conflict.Hotkey));
        }

        foreach (var failure in failures)
        {
            var name = names.GetValueOrDefault(failure.NodeId, failure.NodeId);
            problems.Add(string.Format(
                failure.Reason == ItemHotkeyFailureReason.Invalid ? Strings.HotkeyItemInvalid : Strings.HotkeyItemInUse,
                name,
                failure.Hotkey));
        }

        ReportItemHotkeyProblems(problems);
    }

    private void ReportItemHotkeyProblems(IReadOnlyList<string> problems)
    {
        var signature = string.Join("\n", problems);
        if (signature == _lastItemHotkeyProblems)
        {
            return;
        }

        _lastItemHotkeyProblems = signature;
        if (problems.Count == 0)
        {
            return;
        }

        // Balloons hold ~250 characters: the first two problems, then a count of the rest.
        var shown = problems.Take(2).ToList();
        if (problems.Count > shown.Count)
        {
            shown.Add(string.Format(Strings.HotkeyItemsMore, problems.Count - shown.Count));
        }

        _trayService?.ShowBalloon("Your Launcher", string.Join("\n", shown), warning: true);
    }

    /// <summary>
    /// A per-item hotkey fired: launch that node without showing the panel, record usage. Nodes that
    /// need the panel ("Ask before launching", or a {query} to type) show it with the confirm bar / the keyword
    /// prefilled instead. A failed launch has no panel to show its error on, so it goes to the tray.
    /// </summary>
    private void OnItemHotkeyPressed(string nodeId)
    {
        if (_viewModel is null || _mainWindow is null)
        {
            return;
        }

        switch (_viewModel.LaunchFromHotkey(nodeId, out var error))
        {
            case HotkeyLaunchResult.NeedsPanel:
                if (_mainWindow.IsVisible && _viewModel.CurrentPage != PanelPage.List)
                {
                    // Editor/settings/picker/confirm bar open: showing the panel would reset it and drop unsaved work.
                    _mainWindow.RequestShow();
                    break;
                }

                _mainWindow.ShowLauncher(); // resets to a clean root first, so the prefill/confirm below is not wiped.
                _viewModel.ShowHotkeyNodeInPanel(nodeId);
                break;

            case HotkeyLaunchResult.Failed when error is not null:
                _trayService?.ShowBalloon("Your Launcher", error, warning: true);
                break;
        }
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
