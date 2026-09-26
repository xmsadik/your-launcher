using YourLauncher.Core.Model;

namespace YourLauncher.App.ViewModels;

/// <summary>Display-ready wrapper around a single row in the panel's list.</summary>
public sealed class ListItemViewModel
{
    public required Node Node { get; init; }
    public required string Name { get; init; }
    public required string Glyph { get; init; }
    public required string SecondaryText { get; init; }

    /// <summary>Matched char indices into <see cref="Name"/>, for highlighting. Empty in nav mode or when a non-name field won the match.</summary>
    public IReadOnlyList<int> HighlightPositions { get; init; } = Array.Empty<int>();

    /// <summary>Root..parent chain for a search result (used by Enter/Ctrl+Enter to navigate there); empty in nav mode.</summary>
    public IReadOnlyList<FolderNode> ParentChain { get; init; } = Array.Empty<FolderNode>();

    public bool IsFolder => Node is FolderNode;

    /// <summary>True while this node is the current Ctrl+X target (spec §6.3) - shown dimmed/italic until paste/another cut/hide clears it.</summary>
    public bool IsCut { get; init; }
}
