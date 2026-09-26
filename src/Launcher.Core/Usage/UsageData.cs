namespace YourLauncher.Core.Usage;

/// <summary>Per-node usage record (spec §8.4): how many times a node has been launched, and when it was last launched.</summary>
public sealed class UsageEntry
{
    public int UseCount { get; set; }

    public DateTime LastUsedUtc { get; set; }
}

/// <summary>
/// Root of usage.json (spec §8.4) - kept entirely separate from config.json so the config file a user
/// might hand-edit stays free of noisy, frequently-changing data. Keyed by <see cref="Model.Node.Id"/>.
/// </summary>
public sealed class UsageData
{
    public Dictionary<string, UsageEntry> Entries { get; set; } = new();
}
