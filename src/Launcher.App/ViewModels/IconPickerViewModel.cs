using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using YourLauncher.App.Localization;
using YourLauncher.App.Services;
using YourLauncher.Core.Model;
using YourLauncher.Core.Search;

namespace YourLauncher.App.ViewModels;

/// <summary>Ctrl+I icon picker tabs (spec §4/§7.8) - no "Default" tab; Ctrl+0 resets to default from any of these.</summary>
public enum IconPickerTab
{
    Glyph,
    Emoji,
    File,
    Exe,
}

/// <summary>One entry in the curated glyph grid (spec §4 step: "grid of ~60 curated Segoe Fluent glyphs").</summary>
public sealed record GlyphOption(string Name, string Glyph);

/// <summary>One extracted exe/dll icon, keeping the real 0-based resource index alongside the bitmap so a gap left by a failed extraction never shifts a later icon's applied index (spec §2 <c>ExtractAllAsync</c>).</summary>
public sealed record ExeIconOption(int Index, ImageSource Image);

/// <summary>Direction for grid navigation (spec §7.8: fixed 10-column grid, arrows wrap by row/column).</summary>
public enum GridDirection
{
    Left,
    Right,
    Up,
    Down,
    PageUp,
    PageDown,
}

/// <summary>
/// Drives the Ctrl+I icon picker (spec §4, revised §7.7-§7.12). Owned by MainViewModel while
/// <see cref="PanelPage.IconPicker"/> is the current page, same pattern as
/// <see cref="TypePickerViewModel"/>/<see cref="EditorViewModel"/>: nothing is applied to the real node
/// until <see cref="Applied"/> fires, which MainViewModel turns into a save + cache invalidation + list
/// refresh.
/// </summary>
public sealed partial class IconPickerViewModel : ObservableObject
{
    private const int GridColumns = 10;

    /// <summary>Curated glyphs (spec §7.12: only code points actually in Segoe MDL2 Assets' Win10-safe E700-E9FF range).</summary>
    public static readonly IReadOnlyList<GlyphOption> AllGlyphs = new[]
    {
        new GlyphOption(Strings.GlyphName("Folder"), ""),
        new GlyphOption(Strings.GlyphName("FolderOpen"), ""),
        new GlyphOption(Strings.GlyphName("NewFolder"), ""),
        new GlyphOption(Strings.GlyphName("App"), ""),
        new GlyphOption(Strings.GlyphName("Terminal"), ""),
        new GlyphOption(Strings.GlyphName("Globe"), ""),
        new GlyphOption(Strings.GlyphName("World"), ""),
        new GlyphOption(Strings.GlyphName("Document"), ""),
        new GlyphOption(Strings.GlyphName("Page"), ""),
        new GlyphOption(Strings.GlyphName("Code"), ""),
        new GlyphOption(Strings.GlyphName("Settings"), ""),
        new GlyphOption(Strings.GlyphName("Star"), ""),
        new GlyphOption(Strings.GlyphName("StarFilled"), ""),
        new GlyphOption(Strings.GlyphName("Home"), ""),
        new GlyphOption(Strings.GlyphName("Mail"), ""),
        new GlyphOption(Strings.GlyphName("Calendar"), ""),
        new GlyphOption(Strings.GlyphName("Cloud"), ""),
        new GlyphOption(Strings.GlyphName("Lock"), ""),
        new GlyphOption(Strings.GlyphName("Unlock"), ""),
        new GlyphOption(Strings.GlyphName("Key"), ""),
        new GlyphOption(Strings.GlyphName("Contact"), ""),
        new GlyphOption(Strings.GlyphName("ContactInfo"), ""),
        new GlyphOption(Strings.GlyphName("People"), ""),
        new GlyphOption(Strings.GlyphName("Group"), ""),
        new GlyphOption(Strings.GlyphName("Music"), ""),
        new GlyphOption(Strings.GlyphName("Video"), ""),
        new GlyphOption(Strings.GlyphName("Photo"), ""),
        new GlyphOption(Strings.GlyphName("Picture"), ""),
        new GlyphOption(Strings.GlyphName("Camera"), ""),
        new GlyphOption(Strings.GlyphName("Game"), ""),
        new GlyphOption(Strings.GlyphName("Chart"), ""),
        new GlyphOption(Strings.GlyphName("Briefcase"), ""),
        new GlyphOption(Strings.GlyphName("Lightning"), ""),
        new GlyphOption(Strings.GlyphName("Flag"), ""),
        new GlyphOption(Strings.GlyphName("Bookmark"), ""),
        new GlyphOption(Strings.GlyphName("Pin"), ""),
        new GlyphOption(Strings.GlyphName("Link"), ""),
        new GlyphOption(Strings.GlyphName("Download"), ""),
        new GlyphOption(Strings.GlyphName("Upload"), ""),
        new GlyphOption(Strings.GlyphName("Sync"), ""),
        new GlyphOption(Strings.GlyphName("Refresh"), ""),
        new GlyphOption(Strings.GlyphName("Search"), ""),
        new GlyphOption(Strings.GlyphName("Zoom"), ""),
        new GlyphOption(Strings.GlyphName("Edit"), ""),
        new GlyphOption(Strings.GlyphName("Delete"), ""),
        new GlyphOption(Strings.GlyphName("Save"), ""),
        new GlyphOption(Strings.GlyphName("Print"), ""),
        new GlyphOption(Strings.GlyphName("Phone"), ""),
        new GlyphOption(Strings.GlyphName("Chat"), ""),
        new GlyphOption(Strings.GlyphName("Comment"), ""),
        new GlyphOption(Strings.GlyphName("Shop"), ""),
        new GlyphOption(Strings.GlyphName("Map"), ""),
        new GlyphOption(Strings.GlyphName("Car"), ""),
        new GlyphOption(Strings.GlyphName("Airplane"), ""),
        new GlyphOption(Strings.GlyphName("Book"), ""),
        new GlyphOption(Strings.GlyphName("Wrench"), ""),
        new GlyphOption(Strings.GlyphName("Shield"), ""),
        new GlyphOption(Strings.GlyphName("Power"), ""),
        new GlyphOption(Strings.GlyphName("Admin"), ""),
        new GlyphOption(Strings.GlyphName("Network"), ""),
        new GlyphOption(Strings.GlyphName("Brightness"), ""),
        new GlyphOption(Strings.GlyphName("Volume"), ""),
        new GlyphOption(Strings.GlyphName("Clock"), ""),
        new GlyphOption(Strings.GlyphName("Warning"), ""),
        new GlyphOption(Strings.GlyphName("Info"), ""),
        new GlyphOption(Strings.GlyphName("Accept"), ""),
        new GlyphOption(Strings.GlyphName("Add"), ""),
        new GlyphOption(Strings.GlyphName("Remove"), ""),
        new GlyphOption(Strings.GlyphName("Close"), ""),
        new GlyphOption(Strings.GlyphName("Favorite"), ""),
    };

    private readonly Node _node;
    private readonly IconService _iconService;
    private readonly string _configDirectory;
    private CancellationTokenSource? _exeLoadCts;
    private string? _lastLoadedExePath;

    [ObservableProperty]
    private IconPickerTab _activeTab;

    // ---- Glyph tab ----
    [ObservableProperty]
    private string _glyphFilterText = "";

    public ObservableCollection<GlyphOption> FilteredGlyphs { get; } = new(AllGlyphs);

    [ObservableProperty]
    private int _glyphSelectedIndex;

    // ---- Emoji tab ----
    [ObservableProperty]
    private string _emojiText = "";

    [ObservableProperty]
    private string? _emojiError;

    // ---- Exe/DLL tab ----
    [ObservableProperty]
    private string _exePath = "";

    public ObservableCollection<ExeIconOption> ExeIcons { get; } = new();

    [ObservableProperty]
    private int _exeSelectedIndex = -1;

    [ObservableProperty]
    private bool _isLoadingExeIcons;

    [ObservableProperty]
    private string _exeStatusText = "";

    // ---- "Current:" header preview (spec §7.9) ----
    [ObservableProperty]
    private string _currentDescription = "";

    [ObservableProperty]
    private string _currentGlyph = "";

    [ObservableProperty]
    private string? _currentEmoji;

    [ObservableProperty]
    private ImageSource? _currentImage;

    /// <summary>Raised once a tab confirms a choice; null means "reset to automatic" (spec §7.8 Ctrl+0).</summary>
    public event Action<IconSpec?>? Applied;

    /// <summary>Raised on Esc.</summary>
    public event Action? Cancelled;

    public IconPickerViewModel(Node node, IconService iconService, string configDirectory)
    {
        _node = node;
        _iconService = iconService;
        _configDirectory = configDirectory;

        InitializeFromCurrentIcon();
    }

    /// <summary>True if the Exe path box has changed since icons were last loaded from it (spec §7.8: Enter either loads or applies, depending on this).</summary>
    public bool HasExePathChangedSinceLastLoad =>
        !string.Equals(ExePath.Trim(), _lastLoadedExePath, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------------------------------------
    // Initial state (spec §7.9: open on the node's current icon, current tab/value preselected).
    // ---------------------------------------------------------------------------------------------

    private void InitializeFromCurrentIcon()
    {
        var spec = _node.Icon;
        CurrentDescription = DescribeCurrent(spec);

        switch (spec?.Kind)
        {
            case IconKind.Emoji:
                ActiveTab = IconPickerTab.Emoji;
                EmojiText = spec.Value;
                break;

            case IconKind.File:
                ActiveTab = IconPickerTab.File;
                break;

            case IconKind.Exe:
                ActiveTab = IconPickerTab.Exe;
                ExePath = spec.Value;
                break;

            default:
                ActiveTab = IconPickerTab.Glyph;
                var currentGlyphValue = spec is { Kind: IconKind.Glyph } ? spec.Value : IconGlyphs.DefaultFor(_node);
                var index = AllGlyphs.ToList().FindIndex(g => g.Glyph == currentGlyphValue);
                GlyphSelectedIndex = Math.Max(index, 0);
                break;
        }

        if (ExePath.Length == 0)
        {
            ExePath = _node is AppNode appForDefault && IsExeOrDll(appForDefault.Target)
                ? appForDefault.Target
                : "%SystemRoot%\\System32\\imageres.dll"; // spec §7.11 default path
        }

        RefreshCurrentPreview();
    }

    private static bool IsExeOrDll(string target) =>
        target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static string DescribeCurrent(IconSpec? spec) => spec?.Kind switch
    {
        IconKind.Glyph => Strings.IconTabGlyph,
        IconKind.Emoji => string.Format(Strings.IconCurrentEmoji, spec.Value),
        IconKind.File => string.Format(Strings.IconCurrentFile, Path.GetFileName(spec.Value)),
        IconKind.Exe => string.Format(Strings.IconCurrentExe, spec.Index ?? 0, Path.GetFileName(spec.Value)),
        _ => Strings.IconAutomatic,
    };

    /// <summary>Fire-and-forget: shows the node's *current* icon in the header while the tabs below let the user pick a new one. Failures leave the type default glyph showing (IconService itself never throws).</summary>
    private async void RefreshCurrentPreview()
    {
        var spec = _node.Icon;
        if (spec is { Kind: IconKind.Glyph })
        {
            CurrentGlyph = spec.Value;
            CurrentEmoji = null;
            CurrentImage = null;
            return;
        }

        if (spec is { Kind: IconKind.Emoji })
        {
            CurrentGlyph = IconGlyphs.DefaultFor(_node);
            CurrentEmoji = spec.Value;
            CurrentImage = null;
            return;
        }

        CurrentGlyph = IconGlyphs.DefaultFor(_node);
        CurrentEmoji = null;
        CurrentImage = await _iconService.GetAsync(spec, _node).ConfigureAwait(true);
    }

    // ---------------------------------------------------------------------------------------------
    // Tab switching (spec §7.8: Ctrl+Tab/Ctrl+Shift+Tab or Ctrl+1..4, or a click).
    // ---------------------------------------------------------------------------------------------

    public void SetTab(IconPickerTab tab) => ActiveTab = tab;

    public void NextTab() => ActiveTab = (IconPickerTab)(((int)ActiveTab + 1) % 4);

    public void PreviousTab() => ActiveTab = (IconPickerTab)(((int)ActiveTab + 3) % 4);

    // ---------------------------------------------------------------------------------------------
    // Glyph tab.
    // ---------------------------------------------------------------------------------------------

    partial void OnGlyphFilterTextChanged(string value)
    {
        var filter = TextNormalizer.Normalize(value).Trim();
        var matches = string.IsNullOrWhiteSpace(value)
            ? AllGlyphs
            : AllGlyphs.Where(g => TextNormalizer.Normalize(g.Name).Contains(filter, StringComparison.Ordinal)).ToList();

        FilteredGlyphs.Clear();
        foreach (var glyph in matches)
        {
            FilteredGlyphs.Add(glyph);
        }

        GlyphSelectedIndex = FilteredGlyphs.Count > 0 ? 0 : -1;
    }

    public void MoveGlyphSelection(GridDirection direction) =>
        GlyphSelectedIndex = MoveGridSelection(GlyphSelectedIndex, FilteredGlyphs.Count, direction);

    public void ApplyGlyphSelection()
    {
        if (GlyphSelectedIndex < 0 || GlyphSelectedIndex >= FilteredGlyphs.Count)
        {
            return;
        }

        Applied?.Invoke(new IconSpec { Kind = IconKind.Glyph, Value = FilteredGlyphs[GlyphSelectedIndex].Glyph });
    }

    // ---------------------------------------------------------------------------------------------
    // Emoji tab (spec §7.10: exactly one grapheme).
    // ---------------------------------------------------------------------------------------------

    public void ApplyEmoji()
    {
        var trimmed = EmojiText.Trim();
        if (trimmed.Length == 0)
        {
            EmojiError = Strings.IconEmojiEmpty;
            return;
        }

        if (trimmed.Length > 8 || new StringInfo(trimmed).LengthInTextElements != 1)
        {
            EmojiError = Strings.IconEmojiSingle;
            return;
        }

        EmojiError = null;
        Applied?.Invoke(new IconSpec { Kind = IconKind.Emoji, Value = trimmed });
    }

    // ---------------------------------------------------------------------------------------------
    // File tab: the View owns the OpenFileDialog + auto-hide suppression (EditorView's pattern); it
    // hands the imported destination path back here to apply.
    // ---------------------------------------------------------------------------------------------

    public void ApplyImportedFile(string importedAbsolutePath)
    {
        // Store relative to the config directory so config.json stays portable (spec §7.5); fall back to
        // the absolute path in the unlikely case the import landed outside it.
        var relative = Path.GetRelativePath(_configDirectory, importedAbsolutePath);
        var value = relative.StartsWith("..", StringComparison.Ordinal) ? importedAbsolutePath : relative;
        Applied?.Invoke(new IconSpec { Kind = IconKind.File, Value = value });
    }

    // ---------------------------------------------------------------------------------------------
    // Exe/DLL tab (spec §7.11: cap 1024, fill progressively).
    // ---------------------------------------------------------------------------------------------

    public async Task LoadExeIconsAsync()
    {
        var path = ExePath.Trim();
        if (path.Length == 0)
        {
            return;
        }

        _exeLoadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _exeLoadCts = cts;

        ExeIcons.Clear();
        ExeSelectedIndex = -1;
        IsLoadingExeIcons = true;
        ExeStatusText = Strings.IconLoading;
        _lastLoadedExePath = path;

        try
        {
            var all = await _iconService.ExtractAllAsync(
                path,
                batch => Application.Current?.Dispatcher.BeginInvoke(() => OnExeBatchLoaded(cts, batch)),
                cts.Token).ConfigureAwait(true);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            ExeStatusText = all.Count == 0
                ? Strings.IconNoneFound
                : string.Format(all.Count == 1 ? Strings.IconCountOne : Strings.IconCountMany, all.Count);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                IsLoadingExeIcons = false;
            }
        }
    }

    private void OnExeBatchLoaded(CancellationTokenSource cts, IReadOnlyList<(int Index, ImageSource Image)> batch)
    {
        if (cts.IsCancellationRequested)
        {
            return;
        }

        foreach (var (index, image) in batch)
        {
            ExeIcons.Add(new ExeIconOption(index, image));
        }

        if (ExeSelectedIndex < 0 && ExeIcons.Count > 0)
        {
            ExeSelectedIndex = 0;
        }
    }

    public void MoveExeSelection(GridDirection direction) =>
        ExeSelectedIndex = MoveGridSelection(ExeSelectedIndex, ExeIcons.Count, direction);

    public void ApplyExeSelection()
    {
        if (ExeSelectedIndex < 0 || ExeSelectedIndex >= ExeIcons.Count)
        {
            return;
        }

        var option = ExeIcons[ExeSelectedIndex];
        Applied?.Invoke(new IconSpec { Kind = IconKind.Exe, Value = ExePath.Trim(), Index = option.Index });
    }

    // ---------------------------------------------------------------------------------------------
    // Reset / cancel.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Ctrl+0, from any tab (spec §7.8): reset to automatic.</summary>
    public void ResetToDefault() => Applied?.Invoke(null);

    public void Cancel() => Cancelled?.Invoke();

    // ---------------------------------------------------------------------------------------------
    // Shared fixed-column grid navigation (Glyph + Exe tabs), wrapping by row/column (spec §7.8).
    // ---------------------------------------------------------------------------------------------

    private static int MoveGridSelection(int currentIndex, int itemCount, GridDirection direction)
    {
        if (itemCount == 0)
        {
            return -1;
        }

        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var rowCount = (itemCount + GridColumns - 1) / GridColumns;
        var row = currentIndex / GridColumns;
        var col = currentIndex % GridColumns;

        switch (direction)
        {
            case GridDirection.Left:
                col = col == 0 ? GridColumns - 1 : col - 1;
                break;
            case GridDirection.Right:
                col = col == GridColumns - 1 ? 0 : col + 1;
                break;
            case GridDirection.Up:
                row = row == 0 ? rowCount - 1 : row - 1;
                break;
            case GridDirection.Down:
                row = row == rowCount - 1 ? 0 : row + 1;
                break;
            case GridDirection.PageUp:
                row = Math.Max(0, row - 5);
                break;
            case GridDirection.PageDown:
                row = Math.Min(rowCount - 1, row + 5);
                break;
        }

        return Math.Clamp(row * GridColumns + col, 0, itemCount - 1);
    }
}
