namespace YourLauncher.Core.Providers;

/// <summary>
/// One list row that is not a tree node (calculator answer, system command, open window, file hit, ...).
/// It never touches config.json: the panel only ever shows it and runs <see cref="Execute"/>.
/// </summary>
public sealed class ProviderResult
{
    /// <summary>
    /// Stable identity, e.g. "sys:lock" or "calc:result". Must contain ':' - that is how usage pruning tells
    /// a provider id from a node id (node ids are GUIDs and never contain it), so the usage history of
    /// provider results survives the startup prune.
    /// </summary>
    public required string Id
    {
        get => _id;
        init => _id = value.Contains(':')
            ? value
            : throw new ArgumentException("A provider result id must contain ':' (e.g. \"sys:lock\").", nameof(value));
    }

    private readonly string _id = "";

    /// <summary>
    /// What usage history (recording and ranking) is keyed by; defaults to <see cref="Id"/>. Differs from the id
    /// when several rows must stay distinct but count as the same thing for "frequently used" - e.g. two open
    /// windows of one app have different ids ("win:chrome:1a2b") yet share the usage key "win:chrome". Like the
    /// id it must contain ':' so the startup usage prune keeps it.
    /// </summary>
    public string UsageKey
    {
        get => _usageKey ?? Id;
        init => _usageKey = value.Contains(':')
            ? value
            : throw new ArgumentException("A provider usage key must contain ':' (e.g. \"win:chrome\").", nameof(value));
    }

    private readonly string? _usageKey;

    public required string Title { get; init; }

    public string Subtitle { get; init; } = "";

    /// <summary>Segoe Fluent code point shown when there is no <see cref="IconPath"/> image.</summary>
    public required string Glyph { get; init; }

    /// <summary>File whose shell icon is shown instead of <see cref="Glyph"/>; null when the glyph is enough.</summary>
    public string? IconPath { get; init; }

    /// <summary>
    /// File or folder this row stands for, if any. Ctrl+Enter then reveals it in Explorer ("show in folder")
    /// instead of doing nothing; null for rows that are not a file system entry.
    /// </summary>
    public string? LocationPath { get; init; }

    public ResultTier Tier { get; init; } = ResultTier.SystemCommands;

    /// <summary>Order inside the tier, higher first.</summary>
    public int Score { get; init; }

    /// <summary>Ask Enter/Esc in the confirm bar before running (shutdown, restart, ...).</summary>
    public bool ConfirmLaunch { get; init; }

    /// <summary>False keeps the panel open after a successful run (e.g. a copy action the user may repeat).</summary>
    public bool CloseAfterRun { get; init; } = true;

    /// <summary>Matched char indices into <see cref="Title"/>, for highlighting; empty when nothing to highlight.</summary>
    public IReadOnlyList<int> HighlightPositions { get; init; } = Array.Empty<int>();

    /// <summary>Runs the action; returns true when it succeeded (only then is usage recorded and the panel closed).</summary>
    public required Func<bool> Execute { get; init; }
}
