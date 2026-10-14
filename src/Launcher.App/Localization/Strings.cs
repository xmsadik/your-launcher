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
}
