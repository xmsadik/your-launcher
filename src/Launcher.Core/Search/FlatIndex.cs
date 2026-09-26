using YourLauncher.Core.Model;

namespace YourLauncher.Core.Search;

/// <summary>One node flattened for search, with its parent chain and every field pre-normalized (spec §7).</summary>
public sealed class FlatIndexEntry
{
    public required Node Node { get; init; }

    /// <summary>Root through the node's parent, inclusive of root; used to enter a folder result and to build the breadcrumb.</summary>
    public required IReadOnlyList<FolderNode> ParentChain { get; init; }

    /// <summary>0 for a root-level node, 1 for a node one folder deep, etc.</summary>
    public required int Depth { get; init; }

    /// <summary>Ancestor folder names excluding "Root", joined with " › "; empty for a root-level node.</summary>
    public required string Breadcrumb { get; init; }

    public required string NormalizedName { get; init; }

    /// <summary>Word-start flags computed from the ORIGINAL name, so camelCase boundaries (e.g. "VSCode") are still detected.</summary>
    public required bool[] NameWordStarts { get; init; }

    public required IReadOnlyList<string> NormalizedKeywords { get; init; }

    public required string NormalizedDescription { get; init; }

    /// <summary>Normalized command text for a <see cref="CommandNode"/>; empty for every other node type.</summary>
    public required string NormalizedCommand { get; init; }
}

/// <summary>
/// Flattened, pre-normalized view of the whole config tree (spec §7): one entry per node, excluding the
/// root itself. Cheap to rebuild - call <see cref="Build"/> again whenever the config is (re)loaded.
/// </summary>
public sealed class FlatIndex
{
    public IReadOnlyList<FlatIndexEntry> Entries { get; }

    private FlatIndex(IReadOnlyList<FlatIndexEntry> entries) => Entries = entries;

    public static FlatIndex Build(FolderNode root)
    {
        var entries = new List<FlatIndexEntry>();
        var chain = new List<FolderNode> { root };
        Walk(root, chain, entries);
        return new FlatIndex(entries);
    }

    private static void Walk(FolderNode folder, List<FolderNode> chain, List<FlatIndexEntry> entries)
    {
        foreach (var child in folder.Children)
        {
            entries.Add(CreateEntry(child, chain));

            if (child is FolderNode childFolder)
            {
                chain.Add(childFolder);
                Walk(childFolder, chain, entries);
                chain.RemoveAt(chain.Count - 1);
            }
        }
    }

    private static FlatIndexEntry CreateEntry(Node node, List<FolderNode> chain)
    {
        // chain[0] is the root itself ("Root"), which is excluded from the breadcrumb.
        var breadcrumb = string.Join(" › ", chain.Skip(1).Select(f => f.Name));

        return new FlatIndexEntry
        {
            Node = node,
            ParentChain = chain.ToList(),
            Depth = chain.Count - 1,
            Breadcrumb = breadcrumb,
            NormalizedName = TextNormalizer.Normalize(node.Name),
            NameWordStarts = FuzzyScorer.ComputeWordStarts(node.Name),
            NormalizedKeywords = node.Keywords.Select(TextNormalizer.Normalize).ToList(),
            NormalizedDescription = TextNormalizer.Normalize(node.Description ?? ""),
            NormalizedCommand = node is CommandNode command ? TextNormalizer.Normalize(command.Command) : "",
        };
    }
}
