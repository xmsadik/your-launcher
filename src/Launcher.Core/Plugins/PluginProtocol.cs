using System.Text.Json;

namespace YourLauncher.Core.Plugins;

/// <summary>Constants and the stdin request of the plugin protocol (documented in the README "Plugins" section).</summary>
public static class PluginProtocol
{
    public const int Version = 1;

    /// <summary>More rows than this are dropped: the panel is a short list, and a runaway plugin must not flood it.</summary>
    public const int MaxItems = 50;

    /// <summary>Stdout is read up to this many bytes; a plugin that prints more is treated as broken (zero items).</summary>
    public const int MaxOutputBytes = 1024 * 1024;

    public const int DefaultTimeoutMs = 3000;
    public const int DefaultDebounceMs = 200;
    public const int MinTimeoutMs = 100;
    public const int MaxTimeoutMs = 30_000;
    public const int MaxDebounceMs = 2000;

    /// <summary>The single JSON line sent to the plugin: <c>{"query":"...","version":1}</c>.</summary>
    public static string BuildRequest(string query) =>
        JsonSerializer.Serialize(new PluginRequestDto { Query = query }, PluginJsonContext.Default.PluginRequestDto);
}
