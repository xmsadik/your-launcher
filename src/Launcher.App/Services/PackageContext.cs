using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace YourLauncher.App.Services;

/// <summary>
/// What differs when the app runs from its MSIX (Microsoft Store) package instead of as a plain exe.
/// Measured on Win11 26100 (tasks/todo.md, Store spike): a folder the packaged process creates under
/// %APPDATA% is redirected into the package's LocalCache, while an already-existing real folder is used in
/// place; HKCU writes are virtualized (so "Start with Windows" needs a StartupTask); processes it starts run
/// outside the package, so launched apps behave normally.
/// </summary>
internal static class PackageContext
{
    private const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged { get; } = DetectPackaged();

    /// <summary>True when the packaged app was started by its StartupTask at sign-in (the MSIX equivalent of the Run value's --silent).</summary>
    public static bool LaunchedByStartupTask()
    {
        if (!IsPackaged)
        {
            return false;
        }

        try
        {
            return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The on-disk location of <paramref name="path"/> as Explorer and other apps see it. Inside the package a
    /// redirected %APPDATA% folder looks like the normal path, but anything started outside the package
    /// (Explorer, the user's editor) only finds it under LocalCache\Roaming - so shell-opens go there instead.
    /// </summary>
    public static string ToPhysicalPath(string path)
    {
        if (!IsPackaged)
        {
            return path;
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var relative = Path.GetRelativePath(roaming, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return path;
        }

        try
        {
            var redirected = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "Roaming", relative);
            if (!File.Exists(redirected) && !Directory.Exists(redirected))
            {
                return path;
            }

            // Both a redirected copy and a real folder can exist (Store build first, plain exe later); the package
            // then uses the real one, so only pick the redirected copy if it is the file this process actually sees.
            // A folder is judged by its config.json, the file that matters when it's opened.
            var probe = Directory.Exists(redirected) ? Path.Combine(relative, "config.json") : relative;
            var seen = new FileInfo(Path.Combine(roaming, probe));
            var copy = new FileInfo(Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "Roaming", probe));
            return !seen.Exists || !copy.Exists || (seen.Length == copy.Length && seen.LastWriteTimeUtc == copy.LastWriteTimeUtc)
                ? redirected
                : path;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return path;
        }
    }

    private static bool DetectPackaged()
    {
        var length = 0u;
        return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, StringBuilder? packageFullName);
}
