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
}
