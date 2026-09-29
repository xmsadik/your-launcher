namespace YourLauncher.Core.Diagnostics;

/// <summary>
/// <c>last-version.txt</c> next to config.json: the app version that last ran against this config folder,
/// so the App can say "Updated to X" once after an update (Store updates install silently).
/// <see cref="CheckAndRecord"/> never throws - a failed marker write just means no notice.
/// </summary>
public static class VersionMarker
{
    public const string FileName = "last-version.txt";

    /// <summary>
    /// Records <paramref name="current"/> and returns true if this run is an update: the marker holds an
    /// older version, or there is no marker but config.json already exists (0.1.0 predates the marker).
    /// A fresh install, the same version, a downgrade or an unreadable marker return false. Call it before
    /// anything creates config.json, or a fresh install looks like an update.
    /// </summary>
    public static bool CheckAndRecord(string configDirectory, Version current)
    {
        var markerPath = Path.Combine(configDirectory, FileName);
        try
        {
            bool updated;
            if (File.Exists(markerPath))
            {
                updated = Version.TryParse(File.ReadAllText(markerPath).Trim(), out var last) && last < current;
                if (!updated && last == current)
                {
                    return false; // nothing to record
                }
            }
            else
            {
                updated = File.Exists(Path.Combine(configDirectory, "config.json"));
            }

            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(markerPath, current.ToString());
            return updated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
