using System.IO;
using System.Runtime.InteropServices;
using YourLauncher.App.Interop;
using YourLauncher.Core.Config;
using YourLauncher.Core.Model;
using YourLauncher.Core.StartMenu;

namespace YourLauncher.App.Services;

/// <summary>
/// Lists what the "Import from Start menu" picker offers: classic desktop apps from the .lnk files under the
/// per-user and all-users Programs folders, plus packaged (Store/MSIX) apps from the shell's AppsFolder.
/// <para>
/// De-duplication: AppsFolder lists desktop apps too, but only as an AppUserModelID or a known-folder path
/// - turning those into a launchable target, arguments, working directory and icon would be guesswork,
/// whereas the .lnk already carries all of that and goes through the same mapping as a dropped shortcut. So
/// shortcuts are the source for desktop apps and AppsFolder contributes only entries with a package-style id
/// (<see cref="StartMenuApp.IsPackagedAppId"/>), which never have a .lnk. A per-user and an all-users copy
/// of the same shortcut share a key; the per-user one wins.
/// </para>
/// Only reads names and paths; shortcut targets are resolved later, for the apps actually chosen.
/// </summary>
public static class StartMenuScanner
{
    /// <summary>Scans on a dedicated STA thread (the shell item APIs expect one) so the UI thread never waits.</summary>
    public static Task<IReadOnlyList<StartMenuApp>> ScanAsync() => RunSta(Scan);

    /// <summary>
    /// Builds the nodes for the chosen apps off the UI thread: resolving a few hundred .lnk files through
    /// IShellLink takes long enough to be felt. Order is preserved.
    /// </summary>
    public static Task<IReadOnlyList<(string Key, Node Node)>> BuildNodesAsync(
        IReadOnlyList<StartMenuApp> apps, Func<string, ShellLinkInfo> resolveShellLink) =>
        RunSta<IReadOnlyList<(string Key, Node Node)>>(() => apps.Select(a => (a.Key, a.ToNode(resolveShellLink))).ToList());

    /// <summary>Runs <paramref name="work"/> on a fresh STA thread and waits for it; for the rare shell call made from a pool thread (icon loading).</summary>
    public static T RunStaBlocking<T>(Func<T> work) => RunSta(work).GetAwaiter().GetResult();

    private static Task<T> RunSta<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "StartMenuImport",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static IReadOnlyList<StartMenuApp> Scan()
    {
        var byKey = new Dictionary<string, StartMenuApp>(StringComparer.Ordinal);

        // Per-user first so its copy wins a key collision with the all-users one.
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                 })
        {
            AddShortcuts(root, byKey);
        }

        try
        {
            foreach (var (displayName, parsingName) in AppsFolder.Enumerate(StartMenuApp.IsPackagedAppId))
            {
                var key = StartMenuApp.PackagedKey(parsingName);
                byKey.TryAdd(key, new StartMenuApp(key, displayName, StartMenuAppKind.Packaged, parsingName));
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or DllNotFoundException or EntryPointNotFoundException)
        {
            // The shortcut list is still useful without the Store apps.
            System.Diagnostics.Debug.WriteLine($"[YourLauncher] Could not enumerate shell:AppsFolder: {ex.Message}");
        }

        return byKey.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void AddShortcuts(string programsFolder, Dictionary<string, StartMenuApp> byKey)
    {
        if (string.IsNullOrEmpty(programsFolder) || !Directory.Exists(programsFolder))
        {
            return;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(programsFolder, "*.lnk", options))
        {
            var key = StartMenuApp.ShortcutKey(Path.GetRelativePath(programsFolder, file));
            byKey.TryAdd(key, new StartMenuApp(key, Path.GetFileNameWithoutExtension(file), StartMenuAppKind.Shortcut, file));
        }
    }
}
