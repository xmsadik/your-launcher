using System.Text.Json.Serialization;
using YourLauncher.Core.Json;

namespace YourLauncher.Core.Model;

/// <summary>
/// Base type for all tree nodes. JsonPropertyOrder keeps config.json readable: id, type, name,
/// type-specific fields, keywords, description, icon, then (folders) children.
/// Polymorphic on a "type" discriminator property so config.json stays
/// human-editable (spec §4.2); see <see cref="NodeJsonConverter"/> for why this uses a hand-written
/// converter instead of System.Text.Json's built-in [JsonPolymorphic] mechanism. New node kinds
/// (Phase 2+) are added as further derived types with their own "type" property.
/// </summary>
[JsonConverter(typeof(NodeJsonConverter))]
public abstract class Node
{
    /// <summary>Unique, immutable identifier.</summary>
    [JsonPropertyOrder(-3)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyOrder(-1)]
    public string Name { get; set; } = "";

    /// <summary>Null means "automatic" icon (spec §4.4). Unused until Phase 4.</summary>
    [JsonPropertyOrder(12)]
    public IconSpec? Icon { get; set; }

    /// <summary>Ask "Launch '...'? Enter = launch" before launching (e.g. shutdown/restart items). Folders ignore it; omitted from config.json while false.</summary>
    [JsonPropertyOrder(9)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ConfirmLaunch { get; set; }

    [JsonPropertyOrder(10)]
    public List<string> Keywords { get; set; } = new();

    /// <summary>Optional short description, shown as a secondary line once search exists (Phase 2).</summary>
    [JsonPropertyOrder(11)]
    public string? Description { get; set; }
}
