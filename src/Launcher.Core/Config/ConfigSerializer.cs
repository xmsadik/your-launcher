using System.Text.Encodings.Web;
using System.Text.Json;
using YourLauncher.Core.Json;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Config;

/// <summary>Result of attempting to parse config.json. Never throws for malformed input; see <see cref="ConfigSerializer.Deserialize"/>.</summary>
public sealed record ConfigLoadResult(bool Success, LauncherConfig? Config, string? Error)
{
    public static ConfigLoadResult Ok(LauncherConfig config) => new(true, config, null);
    public static ConfigLoadResult Failed(string error) => new(false, null, error);
}

/// <summary>Pure JSON (de)serialization for <see cref="LauncherConfig"/>, no file I/O (see <see cref="ConfigStore"/> for that).</summary>
public static class ConfigSerializer
{
    // The source-gen context's default encoder conservatively escapes plain ASCII punctuation (e.g. "+"
    // becomes "+"), which is fine for wire protocols but makes a file meant to be hand-edited (e.g.
    // settings.hotkey: "Alt+Space") harder to read. This is a purely local, trusted file - not HTML - so
    // relax escaping while keeping every other source-gen option (naming policy, comment/trailing-comma
    // tolerance, indentation) from the generated context.
    private static readonly JsonSerializerOptions Options = new(LauncherJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(LauncherConfig config) => JsonSerializer.Serialize(config, Options);

    /// <summary>
    /// Parses config.json text. Unknown/missing optional fields never throw. Malformed JSON or a
    /// structurally invalid document returns a failure result instead of throwing, so callers
    /// (namely <see cref="ConfigStore"/>) can fall back to an empty in-memory config without crashing
    /// and without touching the file on disk.
    /// </summary>
    public static ConfigLoadResult Deserialize(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<LauncherConfig>(json, Options);
            return config is null
                ? ConfigLoadResult.Failed("Config file parsed to an empty document.")
                : ConfigLoadResult.Ok(config);
        }
        catch (JsonException ex)
        {
            return ConfigLoadResult.Failed(ex.Message);
        }
    }
}
