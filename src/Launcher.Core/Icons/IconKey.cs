using YourLauncher.Core.Launch;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Icons;

/// <summary>
/// Builds a stable, deterministic cache key for a node's icon (Phase 4, spec §4.4 / §7.1). Two nodes that
/// resolve to the same icon (same custom file/exe+index, or the same auto target) always get the same
/// key, so <see cref="IconFileStore"/>/<see cref="Services.IconService"/> (App layer) can share one loaded
/// bitmap between them.
/// </summary>
public static class IconKey
{
    /// <summary>
    /// Null for <see cref="IconKind.Glyph"/>/<see cref="IconKind.Emoji"/> (spec §7.15): those render as
    /// text, never as an image, so they're never enqueued for extraction.
    /// </summary>
    public static string? For(IconSpec? spec, Node node)
    {
        if (spec is not null)
        {
            return spec.Kind switch
            {
                IconKind.File => $"file|{NormalizeCustomValue(spec.Value)}|{spec.Index ?? 0}",
                IconKind.Exe => $"exe|{NormalizeCustomValue(spec.Value)}|{spec.Index ?? 0}",
                IconKind.Glyph => null,
                IconKind.Emoji => null,
                _ => null,
            };
        }

        return node switch
        {
            AppNode app => $"sys|{NormalizeCustomValue(app.Target)}",
            PathNode path => $"sys|{NormalizeCustomValue(path.Target)}",
            FolderNode => "glyph|default-folder",
            CommandNode => "glyph|default-command",
            UrlNode => "glyph|default-url",
            SeparatorNode => null,
            _ => "glyph|default-folder",
        };
    }

    /// <summary>Env-expands then lower-invariants, so a config edited to add/remove %ENV% noise, or differing only by case, still hits the same cache entry (paths are case-insensitive on Windows).</summary>
    private static string NormalizeCustomValue(string value) => (EnvExpander.Expand(value) ?? "").ToLowerInvariant();
}
