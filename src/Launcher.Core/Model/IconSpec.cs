using System.Text.Json.Serialization;
using YourLauncher.Core.Json;

namespace YourLauncher.Core.Model;

/// <summary>Custom icon source. Unused by the UI until Phase 4, but kept in the model per spec §4.4.</summary>
public enum IconKind
{
    File,
    Exe,
    Glyph,
    Emoji,
}

public sealed class IconSpec
{
    [JsonConverter(typeof(JsonCamelCaseEnumConverter<IconKind>))]
    public IconKind Kind { get; set; }

    public string Value { get; set; } = "";

    /// <summary>Icon index within an exe/dll, only meaningful when <see cref="Kind"/> is <see cref="IconKind.Exe"/>.</summary>
    public int? Index { get; set; }
}
