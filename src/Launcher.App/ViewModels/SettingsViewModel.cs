using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using YourLauncher.App.Localization;
using YourLauncher.App.Services;
using YourLauncher.Core.Bookmarks;
using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.App.ViewModels;

/// <summary>
/// The settings page (<c>Ctrl+,</c>, spec §11, revised §10 items 1-2/11-13). Built with the same
/// "nothing touches the live config until Save" shape as <see cref="EditorViewModel"/>: Enter/Ctrl+S
/// saves, Esc cancels, and every field here is a local copy of <see cref="Settings"/> until
/// <see cref="RequestSave"/> succeeds and raises <see cref="Saved"/>.
///
/// The hotkey field is the one exception to "no live-apply while editing" (spec §10 item 1): while its
/// capture box has keyboard focus the *live* global hotkey must be unregistered (otherwise WM_HOTKEY would
/// eat the very combination the user is trying to type), so <see cref="BeginHotkeyCapture"/>/
/// <see cref="EndHotkeyCapture"/> call back into App-level delegates that own the real
/// <c>HotkeyService</c>. <see cref="_tryApplyHotkey"/> is the same seam, used only at Save time - it
/// unregisters the old hotkey (a no-op if capture already did that), tries to register the new text, and
/// restores the old one on failure, returning an error string in that case (mirrors
/// <see cref="EditorViewModel"/>'s injected <c>fileDescriptionLookup</c> pattern).
///
/// The other exception is the live theme preview: changing Theme, Opacity or UseWindowsAccent re-themes the
/// panel immediately through <see cref="_previewTheme"/>, and <see cref="EndPreview"/> (called by
/// MainViewModel whenever this page is detached, however that happens) puts the saved look back via
/// <see cref="_restoreTheme"/>. Nothing here writes the config - Save still goes through <see cref="Saved"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly string _originalHotkeyText;
    private readonly bool _isReadOnly;
    private readonly Func<string, string?> _tryApplyHotkey;
    private readonly Action _beginHotkeyCapture;
    private readonly Action _endHotkeyCaptureRestore;
    private readonly Func<string> _exportJson;
    private readonly Action<Theme, int, bool> _previewTheme;
    private readonly Action _restoreTheme;

    private bool _capturingHotkey;
    private bool _previewing;

    [ObservableProperty]
    private string _hotkeyText;

    [ObservableProperty]
    private Theme _theme;

    [ObservableProperty]
    private AppLanguage _language;

    [ObservableProperty]
    private int _opacity;

    [ObservableProperty]
    private bool _useWindowsAccent;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _closeAfterLaunch;

    [ObservableProperty]
    private int _maxVisibleItems;

    [ObservableProperty]
    private ShellKind _defaultShell;

    [ObservableProperty]
    private bool _rememberLastLocation;

    [ObservableProperty]
    private bool _showHintBar;

    [ObservableProperty]
    private string _fileSearchPrefix;

    [ObservableProperty]
    private string _errorMessage = "";

    /// <summary>True the moment a hotkey-registration failure at startup/reload opened this page (spec §10 item 13) - FocusFirstField() puts the caret in the hotkey box instead of the top of the form.</summary>
    public bool FocusHotkeyFirst { get; init; }

    public string ConfigDirectory { get; }

    /// <summary>Why Windows won't start the app even though "Start with Windows" is on (Store build: turned off in Task Manager or by policy); empty hides the line.</summary>
    public string StartupNote { get; init; } = "";

    /// <summary>Export/Import are disabled in read-only mode too (spec §10 revision item 9) - exporting an empty fallback tree, or importing into a config that can't be saved, is more confusing than useful.</summary>
    public bool CanExportImport => !_isReadOnly;

    /// <summary>Raised once Save succeeds (hotkey, if changed, already applied) with the new settings to write into the live config.</summary>
    public event Action<Settings>? Saved;

    public event Action? Cancelled;

    /// <summary>Raised when Import… successfully parses a file - MainViewModel owns the Merge/Replace/cancel prompt (spec §10 item 9), since that mutates the tree, not just Settings.</summary>
    public event Action<LauncherConfig>? ImportParsed;

    /// <summary>
    /// Raised when "Import bookmarks…" successfully parses a source (browser profile or HTML file).
    /// Unlike <see cref="ImportParsed"/> there is no Merge/Replace prompt - MainViewModel applies this
    /// immediately (bookmark spec §2: "MainViewModel applies it immediately... no Merge/Replace prompt"),
    /// since <see cref="Bookmarks.BookmarkImport.Apply"/> already knows on its own whether this source was
    /// imported before (replace in place) or not (append as a new folder). Args: the imported folder, its
    /// stable source key, and the human-readable source label for the status line.
    /// </summary>
    public event Action<FolderNode, string, string>? BookmarksParsed;

    /// <summary>
    /// Raised by "Import from Start menu…". Unlike the bookmark import nothing is parsed here: MainViewModel
    /// opens the picker page, scans the Start menu and applies the user's selection (it owns the tree).
    /// </summary>
    public event Action? StartMenuImportRequested;

    public SettingsViewModel(
        Settings current,
        string configDirectory,
        bool isReadOnly,
        Func<string, string?> tryApplyHotkey,
        Action beginHotkeyCapture,
        Action endHotkeyCaptureRestore,
        Func<string> exportJson,
        Action<Theme, int, bool> previewTheme,
        Action restoreTheme,
        string? hotkeyErrorToShow = null)
    {
        ConfigDirectory = configDirectory;
        _isReadOnly = isReadOnly;
        _tryApplyHotkey = tryApplyHotkey;
        _beginHotkeyCapture = beginHotkeyCapture;
        _endHotkeyCaptureRestore = endHotkeyCaptureRestore;
        _exportJson = exportJson;
        _previewTheme = previewTheme;
        _restoreTheme = restoreTheme;

        _originalHotkeyText = current.Hotkey;
        _hotkeyText = current.Hotkey;
        _theme = current.Theme;
        _language = current.Language;
        _opacity = current.Opacity;
        _useWindowsAccent = current.UseWindowsAccent;
        _startWithWindows = current.StartWithWindows;
        _closeAfterLaunch = current.CloseAfterLaunch;
        _maxVisibleItems = current.MaxVisibleItems;
        _defaultShell = current.DefaultShell;
        _rememberLastLocation = current.RememberLastLocation;
        _showHintBar = current.ShowHintBar;
        _fileSearchPrefix = current.FileSearchPrefix;

        if (isReadOnly)
        {
            _errorMessage = Strings.ConfigReadOnly;
        }
        else if (hotkeyErrorToShow is not null)
        {
            _errorMessage = hotkeyErrorToShow;
            FocusHotkeyFirst = true;
        }
    }

    partial void OnThemeChanged(Theme value) => Preview();

    partial void OnLanguageChanged(AppLanguage value) => OnPropertyChanged(nameof(LanguageNote));

    /// <summary>
    /// Shown under the language combo while the chosen language differs from the one this process started with:
    /// the language is applied before any UI text exists, so a change only takes effect after a restart.
    /// </summary>
    public string LanguageNote => Language == LanguageService.Applied ? "" : Strings.SettingsLanguageRestartNote;

    partial void OnOpacityChanged(int value) => Preview();

    partial void OnUseWindowsAccentChanged(bool value) => Preview();

    /// <summary>
    /// Applies the page's current Theme/Opacity/UseWindowsAccent to the live panel. The opacity box binds on
    /// every keystroke, so a half-typed value ("5" on the way to "50") is out of range and simply skipped -
    /// the preview keeps the last valid value; unparsable text never reaches the property at all.
    /// </summary>
    private void Preview()
    {
        if (Opacity < Settings.MinOpacity || Opacity > 100)
        {
            return;
        }

        _previewing = true;
        _previewTheme(Theme, Opacity, UseWindowsAccent);
    }

    /// <summary>Puts the saved theme/opacity/accent back if a preview changed it; a no-op otherwise. MainViewModel calls this on every exit from the page (save, cancel, hide, import...).</summary>
    public void EndPreview()
    {
        if (!_previewing)
        {
            return;
        }

        _previewing = false;
        _restoreTheme();
    }

    /// <summary>The hotkey box's GotFocus (spec §10 item 1): unregisters the live hotkey so typing the new combination doesn't get eaten by WM_HOTKEY.</summary>
    public void BeginHotkeyCapture()
    {
        if (_capturingHotkey)
        {
            return;
        }

        _capturingHotkey = true;
        _beginHotkeyCapture();
    }

    /// <summary>The hotkey box's LostFocus: restores the still-current (not-yet-saved) hotkey. A no-op if Save already resolved capture (successfully applied the new one, or restored the old one on failure).</summary>
    public void EndHotkeyCapture()
    {
        if (!_capturingHotkey)
        {
            return;
        }

        _capturingHotkey = false;
        _endHotkeyCaptureRestore();
    }

    /// <summary>Backspace in the hotkey box (spec §10 item 1): resets to the value that's actually saved right now, discarding whatever's been typed.</summary>
    public void ResetHotkeyToSaved() => HotkeyText = _originalHotkeyText;

    public void RequestSave()
    {
        if (_isReadOnly)
        {
            ErrorMessage = Strings.ConfigReadOnly;
            return;
        }

        MaxVisibleItems = Math.Clamp(MaxVisibleItems, 3, 20);
        Opacity = Math.Clamp(Opacity, Settings.MinOpacity, 100);

        string? hotkeyError = null;
        var hotkeyChanged = !string.Equals(HotkeyText, _originalHotkeyText, StringComparison.Ordinal);
        if (hotkeyChanged)
        {
            _capturingHotkey = false; // resolved by tryApplyHotkey below, not by the blur-restore path.
            hotkeyError = _tryApplyHotkey(HotkeyText);
            if (hotkeyError is not null)
            {
                HotkeyText = _originalHotkeyText; // tryApplyHotkey already restored the old one on failure.
            }
        }
        else if (_capturingHotkey)
        {
            EndHotkeyCapture(); // nothing changed - just restore the (still-registered) current hotkey.
        }

        if (hotkeyError is not null)
        {
            ErrorMessage = hotkeyError;
            return;
        }

        Saved?.Invoke(new Settings
        {
            Hotkey = HotkeyText,
            Theme = Theme,
            Language = Language,
            Opacity = Opacity,
            UseWindowsAccent = UseWindowsAccent,
            StartWithWindows = StartWithWindows,
            CloseAfterLaunch = CloseAfterLaunch,
            MaxVisibleItems = MaxVisibleItems,
            DefaultShell = DefaultShell,
            RememberLastLocation = RememberLastLocation,
            ShowHintBar = ShowHintBar,
            FileSearchPrefix = (FileSearchPrefix ?? "").Trim(),
        });
    }

    public void RequestCancel()
    {
        if (_capturingHotkey)
        {
            EndHotkeyCapture();
        }

        Cancelled?.Invoke();
    }

    public void RequestExport(string path)
    {
        try
        {
            File.WriteAllText(path, _exportJson());
            ErrorMessage = string.Format(Strings.SettingsExported, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = string.Format(Strings.SettingsExportFailed, ex.Message);
        }
    }

    public void RequestImport(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = string.Format(Strings.SettingsReadFailed, ex.Message);
            return;
        }

        var result = ConfigSerializer.Deserialize(json);
        if (!result.Success)
        {
            ErrorMessage = string.Format(Strings.SettingsImportFailed, result.Error);
            return;
        }

        ImportParsed?.Invoke(result.Config!);
    }

    // ---------------------------------------------------------------------------------------------
    // Bookmark import ("Import bookmarks…" button, bookmark spec §2) - discovery + parsing happen here,
    // same shape as RequestImport above; applying the result to the tree is MainViewModel's job
    // (BookmarksParsed), since only it can touch the current folder / save / rebuild the search index.
    // ---------------------------------------------------------------------------------------------

    public void RequestImportStartMenu() => StartMenuImportRequested?.Invoke();

    /// <summary>Every Chromium-family browser profile (+ Opera/Opera GX) with a readable <c>Bookmarks</c> file on this machine, for the button's menu (bookmark spec §2).</summary>
    public IReadOnlyList<BookmarkSource> DiscoverBookmarkSources() => ChromiumBookmarkLocator.Discover(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>One menu item's click: read + parse the chosen browser's <c>Bookmarks</c> file. Errors go on this page's own error line, nothing changes (bookmark spec §2).</summary>
    public void RequestImportBookmarksFromSource(BookmarkSource source)
    {
        List<string> jsons;
        try
        {
            jsons = source.BookmarksPaths.Select(ReadAllTextSharingWithBrowser).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = string.Format(Strings.SettingsBookmarksReadFailed, source.DisplayName, ex.Message);
            return;
        }

        FolderNode imported;
        try
        {
            imported = ChromiumBookmarkParser.Parse(jsons, string.Format(Strings.BookmarksFolderForBrowser, source.DisplayName));
        }
        catch (BookmarkImportException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        BookmarksParsed?.Invoke(imported, source.SourceKey, source.DisplayName);
    }

    /// <summary>"From HTML file…": read + parse an exported Netscape-format bookmarks HTML file (bookmark spec §1.3/§2).</summary>
    public void RequestImportBookmarksFromHtml(string path)
    {
        string html;
        try
        {
            html = ReadAllTextSharingWithBrowser(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = string.Format(Strings.SettingsReadFailed, ex.Message);
            return;
        }

        var folderName = string.Format(Strings.BookmarksFolderForFile, Path.GetFileNameWithoutExtension(path));
        FolderNode imported;
        try
        {
            imported = NetscapeBookmarkParser.Parse(html, folderName);
        }
        catch (BookmarkImportException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        BookmarksParsed?.Invoke(imported, BookmarkImport.HtmlSourceKey(path), Path.GetFileName(path));
    }

    /// <summary>Bookmark spec §2: "the browser may have this file open" - <see cref="File.ReadAllText(string)"/> defaults to a share mode that would fail against that, so open the stream explicitly instead.</summary>
    private static string ReadAllTextSharingWithBrowser(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
