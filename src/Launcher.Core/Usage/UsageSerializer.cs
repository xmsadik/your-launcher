using System.Text.Json;
using YourLauncher.Core.Json;

namespace YourLauncher.Core.Usage;

/// <summary>Pure JSON (de)serialization for usage.json (spec §8.4), same tolerant-reading shape as <c>ConfigSerializer</c> - no file I/O (see the App's <c>UsageService</c> for that).</summary>
public static class UsageSerializer
{
    public static string Serialize(UsageData data) => JsonSerializer.Serialize(data, LauncherJsonContext.Default.UsageData);

    /// <summary>Never throws: malformed JSON (or a missing file's caller passing "") returns an empty <see cref="UsageData"/> rather than losing the whole app to one corrupt file.</summary>
    public static UsageData Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, LauncherJsonContext.Default.UsageData) ?? new UsageData();
        }
        catch (JsonException)
        {
            return new UsageData();
        }
    }
}
