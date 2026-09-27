using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using YourLauncher.App.Services;
using YourLauncher.Core.Icons;
using YourLauncher.Core.Model;

namespace YourLauncher.App.ViewModels;

/// <summary>
/// Display-ready wrapper around a single row in the panel's list. Row icon display priority (spec §4.3):
/// <see cref="IconImage"/> → <see cref="EmojiText"/> → <see cref="Glyph"/>.
///
/// <see cref="IconImage"/> and <see cref="IsTargetMissing"/> are both loaded lazily off the UI thread: the
/// fetch/disk-check only starts the first time either property is <i>read</i>, which for a virtualized
/// ListBox means only realized rows ever touch <see cref="IconService"/> or the filesystem (spec §7.4).
/// <see cref="ConfigureIconLoading"/> wires up the plumbing right after construction; MainViewModel passes
/// a generation counter so a result that comes back after the list has since been rebuilt (folder change,
/// new search, ...) is discarded instead of updating a row nobody is looking at anymore.
/// </summary>
public sealed partial class ListItemViewModel : ObservableObject
{
    private IconService? _iconService;
    private IconSpec? _iconSpec;
    private int _generation;
    private Func<int>? _currentGeneration;
    private bool _loadStarted;

    public required Node Node { get; init; }
    public required string Name { get; init; }

    /// <summary>Fallback glyph: the node's custom glyph value, or the type's default glyph.</summary>
    public required string Glyph { get; init; }

    /// <summary>Custom emoji text (spec §4.3), or null when the node has no emoji icon.</summary>
    public string? EmojiText { get; init; }

    public required string SecondaryText { get; init; }

    /// <summary>Matched char indices into <see cref="Name"/>, for highlighting. Empty in nav mode or when a non-name field won the match.</summary>
    public IReadOnlyList<int> HighlightPositions { get; init; } = Array.Empty<int>();

    /// <summary>Root..parent chain for a search result (used by Enter/Ctrl+Enter to navigate there); empty in nav mode.</summary>
    public IReadOnlyList<FolderNode> ParentChain { get; init; } = Array.Empty<FolderNode>();

    public bool IsFolder => Node is FolderNode;

    /// <summary>Rendered as a plain divider line instead of icon + name.</summary>
    public bool IsSeparator => Node is SeparatorNode;

    /// <summary>True while this node is the current Ctrl+X target (spec §6.3) - shown dimmed/italic until paste/another cut/hide clears it.</summary>
    public bool IsCut { get; init; }

    /// <summary>Set by MainWindow's in-list drag handlers (spec §7/§10 item 8) while an internal drag hovers over this row; reset to <see cref="ViewModels.DropIndicator.None"/> once the drag leaves or ends.</summary>
    [ObservableProperty]
    private DropIndicator _dropIndicator;

    [ObservableProperty]
    private ImageSource? _iconImageValue;

    partial void OnIconImageValueChanged(ImageSource? value) => OnPropertyChanged(nameof(IconImage));

    /// <summary>Backing field's public façade: reading it is what triggers the lazy load (spec §7.4).</summary>
    public ImageSource? IconImage
    {
        get
        {
            EnsureLoadStarted();
            return IconImageValue;
        }
    }

    [ObservableProperty]
    private bool _isTargetMissingValue;

    partial void OnIsTargetMissingValueChanged(bool value) => OnPropertyChanged(nameof(IsTargetMissing));

    /// <summary>True (row dimmed + warning badge, spec §7.3) once the background check confirms an app/path target can't be found. False until that check completes.</summary>
    public bool IsTargetMissing
    {
        get
        {
            EnsureLoadStarted();
            return IsTargetMissingValue;
        }
    }

    /// <summary>Called once by MainViewModel right after construction (not from the object initializer - IconService/generation aren't part of the shape shared with plain list-building).</summary>
    public void ConfigureIconLoading(IconService iconService, IconSpec? iconSpec, int generation, Func<int> currentGeneration)
    {
        _iconService = iconService;
        _iconSpec = iconSpec;
        _generation = generation;
        _currentGeneration = currentGeneration;
    }

    private void EnsureLoadStarted()
    {
        if (_loadStarted || _iconService is null)
        {
            return;
        }

        _loadStarted = true;

        var key = IconKey.For(_iconSpec, Node);
        if (key is not null)
        {
            if (_iconService.TryGetCached(key, out var cached))
            {
                IconImageValue = cached; // synchronous, no flicker (spec §4.3).
            }
            else
            {
                _ = LoadIconAsync();
            }
        }

        if (Node is AppNode or PathNode)
        {
            _ = LoadMissingCheckAsync();
        }
    }

    private async Task LoadIconAsync()
    {
        var generation = _generation;
        var currentGeneration = _currentGeneration!;
        var image = await _iconService!.GetAsync(_iconSpec, Node).ConfigureAwait(true);

        if (currentGeneration() != generation)
        {
            return; // the list was rebuilt since this row was created - discard (spec §7.4).
        }

        IconImageValue = image;
    }

    private async Task LoadMissingCheckAsync()
    {
        var generation = _generation;
        var currentGeneration = _currentGeneration!;
        var missing = await Task.Run(ComputeIsMissing).ConfigureAwait(true);

        if (currentGeneration() != generation)
        {
            return;
        }

        IsTargetMissingValue = missing;
    }

    private bool ComputeIsMissing() => Node switch
    {
        AppNode app => TargetCheck.IsMissing(app.Target),
        PathNode path => TargetCheck.IsMissing(path.Target),
        _ => false,
    };
}
