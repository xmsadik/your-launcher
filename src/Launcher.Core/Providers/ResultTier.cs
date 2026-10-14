namespace YourLauncher.Core.Providers;

/// <summary>
/// Fixed display order of the list's result groups (design rule 3): scores are never mixed across tiers, so
/// a calculator answer is always on top and a slow file hit can never jump above a system command. The
/// numeric order is the display order. <see cref="Nodes"/> is only here to document where the user's own
/// items sit - they come from the search engine, never from a <see cref="ProviderResult"/>.
/// </summary>
public enum ResultTier
{
    Calculator = 0,
    Nodes = 1,
    SystemCommands = 2,
    Windows = 3,

    /// <summary>Slow, asynchronous sources (file search, plugins); appended to the end when they arrive.</summary>
    Background = 4,
}
