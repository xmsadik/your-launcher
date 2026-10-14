using System.Globalization;
using System.Resources;

namespace YourLauncher.App.Localization;

/// <summary>
/// The single access point for user-visible text that lives in <c>Strings.resx</c> (design rule 4). It is a
/// hand-written accessor over <see cref="ResourceManager"/> on purpose: the generated designer class needs
/// Visual Studio, this builds with plain <c>dotnet build</c>. Add a resx entry and a property here together;
/// XAML can bind with <c>{x:Static loc:Strings.Name}</c>. The resource language follows the UI culture, so a
/// later <c>Strings.tr.resx</c> needs no code change.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new("YourLauncher.App.Localization.Strings", typeof(Strings).Assembly);

    private static string Get(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    /// <summary>{0} = result title.</summary>
    public static string ProviderConfirmRun => Get(nameof(ProviderConfirmRun));

    /// <summary>{0} = result title.</summary>
    public static string ProviderActionFailed => Get(nameof(ProviderActionFailed));

    /// <summary>{0} = result title, {1} = exception message.</summary>
    public static string ProviderActionError => Get(nameof(ProviderActionError));

    /// <summary>Settings page checkbox label.</summary>
    public static string SettingsUseWindowsAccent => Get(nameof(SettingsUseWindowsAccent));

    /// <summary>{0} = expression.</summary>
    public static string CalcSubtitle => Get(nameof(CalcSubtitle));

    /// <summary>{0} = expression, {1} = hex result.</summary>
    public static string CalcSubtitleHex => Get(nameof(CalcSubtitleHex));

    public static string SysCmdSubtitle => Get(nameof(SysCmdSubtitle));

    public static string SysCmdLock => Get(nameof(SysCmdLock));

    public static string SysCmdSleep => Get(nameof(SysCmdSleep));

    public static string SysCmdHibernate => Get(nameof(SysCmdHibernate));

    public static string SysCmdSignOut => Get(nameof(SysCmdSignOut));

    public static string SysCmdRestart => Get(nameof(SysCmdRestart));

    public static string SysCmdShutDown => Get(nameof(SysCmdShutDown));

    public static string SysCmdEmptyRecycleBin => Get(nameof(SysCmdEmptyRecycleBin));

    /// <summary>Hint under the URL, command and arguments fields.</summary>
    public static string EditorQueryHint => Get(nameof(EditorQueryHint));

    /// <summary>Editor field label.</summary>
    public static string EditorHotkeyLabel => Get(nameof(EditorHotkeyLabel));

    /// <summary>Hint next to the item hotkey box.</summary>
    public static string EditorHotkeyHint => Get(nameof(EditorHotkeyHint));

    /// <summary>{0} = hotkey.</summary>
    public static string EditorHotkeyConflictMain => Get(nameof(EditorHotkeyConflictMain));

    /// <summary>{0} = hotkey, {1} = other item name.</summary>
    public static string EditorHotkeyConflictItem => Get(nameof(EditorHotkeyConflictItem));

    /// <summary>{0} = hotkey text.</summary>
    public static string EditorHotkeyInvalid => Get(nameof(EditorHotkeyInvalid));

    /// <summary>{0} = item name, {1} = hotkey.</summary>
    public static string HotkeyItemInvalid => Get(nameof(HotkeyItemInvalid));

    /// <summary>{0} = item name, {1} = hotkey.</summary>
    public static string HotkeyItemInUse => Get(nameof(HotkeyItemInUse));

    /// <summary>{0} = item name, {1} = hotkey.</summary>
    public static string HotkeyItemConflictMain => Get(nameof(HotkeyItemConflictMain));

    /// <summary>{0} = item name, {1} = hotkey, {2} = the item that owns it.</summary>
    public static string HotkeyItemConflictItem => Get(nameof(HotkeyItemConflictItem));

    /// <summary>{0} = number of further problems.</summary>
    public static string HotkeyItemsMore => Get(nameof(HotkeyItemsMore));

    /// <summary>{0} = process name (e.g. chrome). Subtitle of an open-window row.</summary>
    public static string WindowSubtitle => Get(nameof(WindowSubtitle));

    /// <summary>Subtitle of an open-window row whose application could not be determined.</summary>
    public static string WindowSubtitleNoApp => Get(nameof(WindowSubtitleNoApp));

    /// <summary>Settings page button: opens the Start-menu app picker.</summary>
    public static string SettingsImportStartMenu => Get(nameof(SettingsImportStartMenu));

    /// <summary>Name of the root folder the Start-menu import creates.</summary>
    public static string StartMenuFolderName => Get(nameof(StartMenuFolderName));

    /// <summary>Breadcrumb suffix on the Start-menu picker page.</summary>
    public static string StartMenuBreadcrumb => Get(nameof(StartMenuBreadcrumb));

    /// <summary>Hint bar text on the Start-menu picker page.</summary>
    public static string StartMenuPickerHint => Get(nameof(StartMenuPickerHint));

    /// <summary>Placeholder of the picker's filter box.</summary>
    public static string StartMenuFilterPlaceholder => Get(nameof(StartMenuFilterPlaceholder));

    /// <summary>Status line while the Start menu is being scanned.</summary>
    public static string StartMenuLoading => Get(nameof(StartMenuLoading));

    /// <summary>Status line after Enter while the selected shortcuts are being resolved.</summary>
    public static string StartMenuImporting => Get(nameof(StartMenuImporting));

    /// <summary>Status line when the scan found nothing.</summary>
    public static string StartMenuNoApps => Get(nameof(StartMenuNoApps));

    /// <summary>Status line when the filter hides every app.</summary>
    public static string StartMenuNoMatches => Get(nameof(StartMenuNoMatches));

    /// <summary>{0} = checked apps, {1} = all apps found.</summary>
    public static string StartMenuSelectedCount => Get(nameof(StartMenuSelectedCount));

    /// <summary>Right-hand label of a packaged app in the picker.</summary>
    public static string StartMenuKindStore => Get(nameof(StartMenuKindStore));

    /// <summary>Right-hand label of a classic desktop app in the picker.</summary>
    public static string StartMenuKindDesktop => Get(nameof(StartMenuKindDesktop));

    /// <summary>{0} = exception message.</summary>
    public static string StartMenuScanFailed => Get(nameof(StartMenuScanFailed));

    /// <summary>{0} = exception message.</summary>
    public static string StartMenuImportFailed => Get(nameof(StartMenuImportFailed));

    /// <summary>{0} added, {1} updated, {2} removed nodes; {3} = folder name.</summary>
    public static string StartMenuImportDone => Get(nameof(StartMenuImportDone));

    /// <summary>{0} = plugin name.</summary>
    public static string PluginSearching => Get(nameof(PluginSearching));

    /// <summary>Tray balloon heading for plugins that failed to load.</summary>
    public static string PluginsSkipped => Get(nameof(PluginsSkipped));

    /// <summary>Settings page button label.</summary>
    public static string SettingsOpenPluginsFolder => Get(nameof(SettingsOpenPluginsFolder));

    public static string FileSearchPending => Get(nameof(FileSearchPending));

    public static string SettingsFileSearchPrefix => Get(nameof(SettingsFileSearchPrefix));

    public static string SettingsFileSearchPrefixHint => Get(nameof(SettingsFileSearchPrefixHint));

    /// <summary>List empty state in a folder.</summary>
    public static string NavEmptyFolder => Get(nameof(NavEmptyFolder));

    /// <summary>List empty state while searching.</summary>
    public static string NavNoResults => Get(nameof(NavNoResults));

    /// <summary>Hint bar, list page. Key names stay as is.</summary>
    public static string HintNav => Get(nameof(HintNav));

    /// <summary>Hint bar, search mode.</summary>
    public static string HintSearch => Get(nameof(HintSearch));

    /// <summary>Hint bar, item editor.</summary>
    public static string HintEditor => Get(nameof(HintEditor));

    /// <summary>Hint bar, type picker.</summary>
    public static string HintTypePicker => Get(nameof(HintTypePicker));

    /// <summary>Hint bar, icon picker.</summary>
    public static string HintIconPicker => Get(nameof(HintIconPicker));

    /// <summary>Hint bar, settings page.</summary>
    public static string HintSettings => Get(nameof(HintSettings));

    /// <summary>{0} = item name. Hint bar while an item is cut.</summary>
    public static string HintCut => Get(nameof(HintCut));

    /// <summary>Breadcrumb page suffix.</summary>
    public static string BreadcrumbNewItem => Get(nameof(BreadcrumbNewItem));

    /// <summary>Breadcrumb page suffix.</summary>
    public static string BreadcrumbEdit => Get(nameof(BreadcrumbEdit));

    /// <summary>Breadcrumb page suffix.</summary>
    public static string BreadcrumbIcon => Get(nameof(BreadcrumbIcon));

    /// <summary>Breadcrumb page suffix.</summary>
    public static string BreadcrumbSettings => Get(nameof(BreadcrumbSettings));

    /// <summary>Placeholder of the search box.</summary>
    public static string SearchPlaceholder => Get(nameof(SearchPlaceholder));

    /// <summary>{0} = 1. Secondary text of a folder row.</summary>
    public static string ItemCountOne => Get(nameof(ItemCountOne));

    /// <summary>{0} = count. Secondary text of a folder row.</summary>
    public static string ItemCountMany => Get(nameof(ItemCountMany));

    /// <summary>Tooltip of the missing-target badge.</summary>
    public static string ListMissingTarget => Get(nameof(ListMissingTarget));

    /// <summary>Tray/menu label.</summary>
    public static string RecoveryKeepCurrent => Get(nameof(RecoveryKeepCurrent));

    /// <summary>Tray/menu label.</summary>
    public static string RecoveryRestoreBackup => Get(nameof(RecoveryRestoreBackup));

    /// <summary>Appended to a read-only error line.</summary>
    public static string RecoveryHintKeepCurrent => Get(nameof(RecoveryHintKeepCurrent));

    /// <summary>Appended to a read-only error line.</summary>
    public static string RecoveryHintRestoreBackup => Get(nameof(RecoveryHintRestoreBackup));

    /// <summary>{0} = item name.</summary>
    public static string LaunchConfirm => Get(nameof(LaunchConfirm));

    /// <summary>Confirm bar.</summary>
    public static string DeleteSeparatorConfirm => Get(nameof(DeleteSeparatorConfirm));

    /// <summary>{0} = folder name, {1} = 1.</summary>
    public static string DeleteFolderConfirmOne => Get(nameof(DeleteFolderConfirmOne));

    /// <summary>{0} = folder name, {1} = count.</summary>
    public static string DeleteFolderConfirmMany => Get(nameof(DeleteFolderConfirmMany));

    /// <summary>{0} = item name.</summary>
    public static string DeleteConfirm => Get(nameof(DeleteConfirm));

    /// <summary>Confirm bar. M and R are key names.</summary>
    public static string ImportConfirm => Get(nameof(ImportConfirm));

    /// <summary>{0} = 1.</summary>
    public static string ImportedMergedOne => Get(nameof(ImportedMergedOne));

    /// <summary>{0} = count.</summary>
    public static string ImportedMergedMany => Get(nameof(ImportedMergedMany));

    /// <summary>{0} = 1.</summary>
    public static string ImportedReplacedOne => Get(nameof(ImportedReplacedOne));

    /// <summary>{0} = count.</summary>
    public static string ImportedReplacedMany => Get(nameof(ImportedReplacedMany));

    /// <summary>{0} = 1, {1} = folder name.</summary>
    public static string BookmarksImportedNewOne => Get(nameof(BookmarksImportedNewOne));

    /// <summary>{0} = count, {1} = folder name.</summary>
    public static string BookmarksImportedNewMany => Get(nameof(BookmarksImportedNewMany));

    /// <summary>{0} = 1, {1} = source.</summary>
    public static string BookmarksImportedReplacedOne => Get(nameof(BookmarksImportedReplacedOne));

    /// <summary>{0} = count, {1} = source.</summary>
    public static string BookmarksImportedReplacedMany => Get(nameof(BookmarksImportedReplacedMany));

    /// <summary>Drag/paste error line.</summary>
    public static string MoveIntoItself => Get(nameof(MoveIntoItself));

    /// <summary>Error line.</summary>
    public static string ReloadPending => Get(nameof(ReloadPending));

    /// <summary>Error line.</summary>
    public static string ReloadSaveRefused => Get(nameof(ReloadSaveRefused));

    /// <summary>Error line and tray balloon.</summary>
    public static string ConfigHasErrors => Get(nameof(ConfigHasErrors));

    /// <summary>Fallback balloon detail.</summary>
    public static string ConfigHasErrorsShort => Get(nameof(ConfigHasErrorsShort));

    /// <summary>Error line.</summary>
    public static string ConfigReadOnly => Get(nameof(ConfigReadOnly));

    /// <summary>Error line.</summary>
    public static string ConfigSaveFailed => Get(nameof(ConfigSaveFailed));

    /// <summary>{0} = exception message.</summary>
    public static string ConfigSaveFailedDetail => Get(nameof(ConfigSaveFailedDetail));

    /// <summary>Error line.</summary>
    public static string ConfigRecoverFailed => Get(nameof(ConfigRecoverFailed));

    /// <summary>{0} = parse error.</summary>
    public static string ConfigLoadFailed => Get(nameof(ConfigLoadFailed));

    /// <summary>Fallback error detail.</summary>
    public static string ConfigUnknownError => Get(nameof(ConfigUnknownError));

    /// <summary>Error line.</summary>
    public static string ConfigNoBackup => Get(nameof(ConfigNoBackup));

    /// <summary>{0} = exception message.</summary>
    public static string ConfigRestoreFailed => Get(nameof(ConfigRestoreFailed));

    /// <summary>{0} = item name, {1} = OS message.</summary>
    public static string LaunchOpenFailed => Get(nameof(LaunchOpenFailed));

    /// <summary>List context menu.</summary>
    public static string MenuOpen => Get(nameof(MenuOpen));

    /// <summary>List context menu.</summary>
    public static string MenuEdit => Get(nameof(MenuEdit));

    /// <summary>List context menu.</summary>
    public static string MenuChangeIcon => Get(nameof(MenuChangeIcon));

    /// <summary>List context menu.</summary>
    public static string MenuCut => Get(nameof(MenuCut));

    /// <summary>List context menu.</summary>
    public static string MenuPasteHere => Get(nameof(MenuPasteHere));

    /// <summary>List context menu.</summary>
    public static string MenuDuplicate => Get(nameof(MenuDuplicate));

    /// <summary>List context menu.</summary>
    public static string MenuDelete => Get(nameof(MenuDelete));

    /// <summary>List context menu.</summary>
    public static string MenuOpenFileLocation => Get(nameof(MenuOpenFileLocation));

    /// <summary>List context menu (empty area).</summary>
    public static string MenuNewItem => Get(nameof(MenuNewItem));

    /// <summary>List context menu (empty area).</summary>
    public static string MenuNewFolder => Get(nameof(MenuNewFolder));

    /// <summary>Tray menu.</summary>
    public static string TrayShow => Get(nameof(TrayShow));

    /// <summary>Tray menu.</summary>
    public static string TraySettings => Get(nameof(TraySettings));

    /// <summary>Tray menu.</summary>
    public static string TrayOpenConfigFile => Get(nameof(TrayOpenConfigFile));

    /// <summary>Tray menu.</summary>
    public static string TrayOpenConfigFolder => Get(nameof(TrayOpenConfigFolder));

    /// <summary>Tray menu.</summary>
    public static string TrayReloadConfig => Get(nameof(TrayReloadConfig));

    /// <summary>Tray menu.</summary>
    public static string TrayExit => Get(nameof(TrayExit));

    /// <summary>{0} = version. Balloon text.</summary>
    public static string TrayUpdated => Get(nameof(TrayUpdated));

    /// <summary>Balloon text.</summary>
    public static string TrayErrorLogged => Get(nameof(TrayErrorLogged));

    /// <summary>{0} = exception message. Balloon text.</summary>
    public static string TrayErrorMessage => Get(nameof(TrayErrorMessage));

    /// <summary>{0} = command name, {1} = exit code.</summary>
    public static string TrayCommandExited => Get(nameof(TrayCommandExited));

    /// <summary>{0} = log file path. Message box.</summary>
    public static string StartupFailedLogged => Get(nameof(StartupFailedLogged));

    /// <summary>{0} = exception message. Message box.</summary>
    public static string StartupFailedMessage => Get(nameof(StartupFailedMessage));

    /// <summary>{0} = hotkey.</summary>
    public static string HotkeyInUse => Get(nameof(HotkeyInUse));

    /// <summary>{0} = hotkey text.</summary>
    public static string HotkeyInvalid => Get(nameof(HotkeyInvalid));

    /// <summary>Appended to a hotkey failure message.</summary>
    public static string HotkeyPreviousNotRestored => Get(nameof(HotkeyPreviousNotRestored));

    /// <summary>Settings note.</summary>
    public static string StartupDisabledByUser => Get(nameof(StartupDisabledByUser));

    /// <summary>Settings note.</summary>
    public static string StartupBlockedByPolicy => Get(nameof(StartupBlockedByPolicy));

    /// <summary>Settings note.</summary>
    public static string StartupForcedByPolicy => Get(nameof(StartupForcedByPolicy));

    /// <summary>{0} = plugin folder, {1} = keyword.</summary>
    public static string PluginKeywordCollision => Get(nameof(PluginKeywordCollision));

    /// <summary>Settings field label.</summary>
    public static string SettingsHotkey => Get(nameof(SettingsHotkey));

    /// <summary>Settings field label.</summary>
    public static string SettingsTheme => Get(nameof(SettingsTheme));

    /// <summary>Settings field label.</summary>
    public static string SettingsOpacity => Get(nameof(SettingsOpacity));

    /// <summary>Settings field label.</summary>
    public static string SettingsStartWithWindows => Get(nameof(SettingsStartWithWindows));

    /// <summary>Settings field label.</summary>
    public static string SettingsCloseAfterLaunch => Get(nameof(SettingsCloseAfterLaunch));

    /// <summary>Settings field label.</summary>
    public static string SettingsVisibleRows => Get(nameof(SettingsVisibleRows));

    /// <summary>Settings field label.</summary>
    public static string SettingsDefaultShell => Get(nameof(SettingsDefaultShell));

    /// <summary>Settings field label.</summary>
    public static string SettingsRememberLocation => Get(nameof(SettingsRememberLocation));

    /// <summary>Settings field label.</summary>
    public static string SettingsShowHintBar => Get(nameof(SettingsShowHintBar));

    /// <summary>Settings field label.</summary>
    public static string SettingsLanguage => Get(nameof(SettingsLanguage));

    /// <summary>Language combo option: follow Windows. The other options (English, Türkçe) are shown in their own language.</summary>
    public static string SettingsLanguageSystem => Get(nameof(SettingsLanguageSystem));

    /// <summary>Note under the language combo.</summary>
    public static string SettingsLanguageRestartNote => Get(nameof(SettingsLanguageRestartNote));

    /// <summary>Settings button.</summary>
    public static string SettingsOpenConfigFolder => Get(nameof(SettingsOpenConfigFolder));

    /// <summary>Settings button.</summary>
    public static string SettingsExport => Get(nameof(SettingsExport));

    /// <summary>Settings button.</summary>
    public static string SettingsImport => Get(nameof(SettingsImport));

    /// <summary>Settings button.</summary>
    public static string SettingsImportBookmarks => Get(nameof(SettingsImportBookmarks));

    /// <summary>Bookmark source menu item.</summary>
    public static string SettingsImportFromHtml => Get(nameof(SettingsImportFromHtml));

    /// <summary>{0} = file name.</summary>
    public static string SettingsExported => Get(nameof(SettingsExported));

    /// <summary>{0} = exception message.</summary>
    public static string SettingsExportFailed => Get(nameof(SettingsExportFailed));

    /// <summary>{0} = exception message.</summary>
    public static string SettingsReadFailed => Get(nameof(SettingsReadFailed));

    /// <summary>{0} = error.</summary>
    public static string SettingsImportFailed => Get(nameof(SettingsImportFailed));

    /// <summary>{0} = browser, {1} = exception message.</summary>
    public static string SettingsBookmarksReadFailed => Get(nameof(SettingsBookmarksReadFailed));

    /// <summary>{0} = browser display name. Source label passed to the parser.</summary>
    public static string BookmarksFolderForBrowser => Get(nameof(BookmarksFolderForBrowser));

    /// <summary>{0} = file name without extension. Name of the imported folder.</summary>
    public static string BookmarksFolderForFile => Get(nameof(BookmarksFolderForFile));

    /// <summary>Dialog title.</summary>
    public static string DialogExportConfig => Get(nameof(DialogExportConfig));

    /// <summary>Dialog title.</summary>
    public static string DialogImportConfig => Get(nameof(DialogImportConfig));

    /// <summary>Dialog title.</summary>
    public static string DialogImportBookmarks => Get(nameof(DialogImportBookmarks));

    /// <summary>Dialog title.</summary>
    public static string DialogSelectFile => Get(nameof(DialogSelectFile));

    /// <summary>Dialog title.</summary>
    public static string DialogSelectFolder => Get(nameof(DialogSelectFolder));

    /// <summary>Dialog title.</summary>
    public static string DialogSelectWorkingDirectory => Get(nameof(DialogSelectWorkingDirectory));

    /// <summary>Dialog title.</summary>
    public static string DialogSelectIconFile => Get(nameof(DialogSelectIconFile));

    /// <summary>Dialog title.</summary>
    public static string DialogSelectExe => Get(nameof(DialogSelectExe));

    /// <summary>File dialog filter: keep the | separators and patterns.</summary>
    public static string FilterJson => Get(nameof(FilterJson));

    /// <summary>File dialog filter: keep the | separators and patterns.</summary>
    public static string FilterHtmlBookmarks => Get(nameof(FilterHtmlBookmarks));

    /// <summary>File dialog filter: keep the | separators and patterns.</summary>
    public static string FilterIconImages => Get(nameof(FilterIconImages));

    /// <summary>File dialog filter: keep the | separators and patterns.</summary>
    public static string FilterPrograms => Get(nameof(FilterPrograms));

    /// <summary>Editor field label.</summary>
    public static string EditorName => Get(nameof(EditorName));

    /// <summary>Editor field label for app/path items.</summary>
    public static string EditorTargetLabel => Get(nameof(EditorTargetLabel));

    /// <summary>Editor field label for URL items.</summary>
    public static string EditorUrlLabel => Get(nameof(EditorUrlLabel));

    /// <summary>Editor field label.</summary>
    public static string EditorCommand => Get(nameof(EditorCommand));

    /// <summary>Editor field label.</summary>
    public static string EditorShell => Get(nameof(EditorShell));

    /// <summary>Editor shell combo: use the default shell.</summary>
    public static string EditorShellDefault => Get(nameof(EditorShellDefault));

    /// <summary>Editor field label.</summary>
    public static string EditorArguments => Get(nameof(EditorArguments));

    /// <summary>Editor field label.</summary>
    public static string EditorWorkingDirectory => Get(nameof(EditorWorkingDirectory));

    /// <summary>Editor field label.</summary>
    public static string EditorWindow => Get(nameof(EditorWindow));

    /// <summary>Editor window combo.</summary>
    public static string EditorWindowVisible => Get(nameof(EditorWindowVisible));

    /// <summary>Editor window combo.</summary>
    public static string EditorWindowHidden => Get(nameof(EditorWindowHidden));

    /// <summary>Editor checkbox.</summary>
    public static string EditorKeepOpen => Get(nameof(EditorKeepOpen));

    /// <summary>Editor checkbox.</summary>
    public static string EditorRunAsAdmin => Get(nameof(EditorRunAsAdmin));

    /// <summary>Editor checkbox.</summary>
    public static string EditorAskBeforeLaunch => Get(nameof(EditorAskBeforeLaunch));

    /// <summary>Editor field label.</summary>
    public static string EditorKeywords => Get(nameof(EditorKeywords));

    /// <summary>Editor field label.</summary>
    public static string EditorDescription => Get(nameof(EditorDescription));

    /// <summary>Editor field label.</summary>
    public static string EditorIcon => Get(nameof(EditorIcon));

    /// <summary>Hint next to the icon preview.</summary>
    public static string EditorIconChangeHint => Get(nameof(EditorIconChangeHint));

    /// <summary>Editor section toggle.</summary>
    public static string EditorAdvancedExpanded => Get(nameof(EditorAdvancedExpanded));

    /// <summary>Editor section toggle.</summary>
    public static string EditorAdvancedCollapsed => Get(nameof(EditorAdvancedCollapsed));

    /// <summary>Validation error.</summary>
    public static string EditorNameRequired => Get(nameof(EditorNameRequired));

    /// <summary>Warning after saving.</summary>
    public static string EditorTargetMissing => Get(nameof(EditorTargetMissing));

    /// <summary>Browse button.</summary>
    public static string ButtonBrowse => Get(nameof(ButtonBrowse));

    /// <summary>Browse-for-folder button.</summary>
    public static string ButtonBrowseFolder => Get(nameof(ButtonBrowseFolder));

    /// <summary>Icon picker Exe/DLL tab.</summary>
    public static string ButtonLoad => Get(nameof(ButtonLoad));

    /// <summary>Node type name.</summary>
    public static string TypeFolder => Get(nameof(TypeFolder));

    /// <summary>Key hint letter (A-Z) that selects the type; unique per language.</summary>
    public static string TypeFolderKey => Get(nameof(TypeFolderKey));

    /// <summary>Node type name.</summary>
    public static string TypeApp => Get(nameof(TypeApp));

    /// <summary>Key hint letter.</summary>
    public static string TypeAppKey => Get(nameof(TypeAppKey));

    /// <summary>Node type name.</summary>
    public static string TypePath => Get(nameof(TypePath));

    /// <summary>Key hint letter.</summary>
    public static string TypePathKey => Get(nameof(TypePathKey));

    /// <summary>Node type name.</summary>
    public static string TypeCommand => Get(nameof(TypeCommand));

    /// <summary>Key hint letter.</summary>
    public static string TypeCommandKey => Get(nameof(TypeCommandKey));

    /// <summary>Node type name.</summary>
    public static string TypeUrl => Get(nameof(TypeUrl));

    /// <summary>Key hint letter.</summary>
    public static string TypeUrlKey => Get(nameof(TypeUrlKey));

    /// <summary>Node type name.</summary>
    public static string TypeSeparator => Get(nameof(TypeSeparator));

    /// <summary>Key hint letter.</summary>
    public static string TypeSeparatorKey => Get(nameof(TypeSeparatorKey));

    /// <summary>Tab and description.</summary>
    public static string IconTabGlyph => Get(nameof(IconTabGlyph));

    /// <summary>Tab.</summary>
    public static string IconTabEmoji => Get(nameof(IconTabEmoji));

    /// <summary>Tab.</summary>
    public static string IconTabFile => Get(nameof(IconTabFile));

    /// <summary>Tab.</summary>
    public static string IconTabExe => Get(nameof(IconTabExe));

    /// <summary>Label before the current-icon preview.</summary>
    public static string IconCurrent => Get(nameof(IconCurrent));

    /// <summary>Icon description.</summary>
    public static string IconAutomatic => Get(nameof(IconAutomatic));

    /// <summary>Icon description.</summary>
    public static string IconCustomEmoji => Get(nameof(IconCustomEmoji));

    /// <summary>Icon description.</summary>
    public static string IconCustomGlyph => Get(nameof(IconCustomGlyph));

    /// <summary>Icon description.</summary>
    public static string IconCustomFile => Get(nameof(IconCustomFile));

    /// <summary>Icon description.</summary>
    public static string IconCustomExe => Get(nameof(IconCustomExe));

    /// <summary>{0} = the emoji.</summary>
    public static string IconCurrentEmoji => Get(nameof(IconCurrentEmoji));

    /// <summary>{0} = file name.</summary>
    public static string IconCurrentFile => Get(nameof(IconCurrentFile));

    /// <summary>{0} = index, {1} = file name.</summary>
    public static string IconCurrentExe => Get(nameof(IconCurrentExe));

    /// <summary>Emoji tab instruction.</summary>
    public static string IconEmojiPrompt => Get(nameof(IconEmojiPrompt));

    /// <summary>Validation error.</summary>
    public static string IconEmojiEmpty => Get(nameof(IconEmojiEmpty));

    /// <summary>Validation error.</summary>
    public static string IconEmojiSingle => Get(nameof(IconEmojiSingle));

    /// <summary>File tab instruction.</summary>
    public static string IconFileInfo => Get(nameof(IconFileInfo));

    /// <summary>Exe tab status.</summary>
    public static string IconLoading => Get(nameof(IconLoading));

    /// <summary>Exe tab status.</summary>
    public static string IconNoneFound => Get(nameof(IconNoneFound));

    /// <summary>{0} = 1. Exe tab status.</summary>
    public static string IconCountOne => Get(nameof(IconCountOne));

    /// <summary>{0} = count. Exe tab status.</summary>
    public static string IconCountMany => Get(nameof(IconCountMany));

    /// <summary>Localized name of a curated icon-picker glyph; <paramref name="key"/> is the part after "Glyph" in the resource name.</summary>
    public static string GlyphName(string key) => Get("Glyph" + key);
}
