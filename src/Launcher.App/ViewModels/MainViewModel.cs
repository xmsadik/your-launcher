using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using YourLauncher.App.Services;
using YourLauncher.Core.Config;
using YourLauncher.Core.Icons;
using YourLauncher.Core.Model;
using YourLauncher.Core.Search;

namespace YourLauncher.App.ViewModels;

/// <summary>
/// Drives the single panel: current folder, filtered/ordered item list, breadcrumb, error line, and
/// (Phase 3) the add/edit/delete/move/cut-paste/duplicate flows and which <see cref="PanelPage"/> is
/// showing. Keyboard focus stays in the search TextBox while <see cref="CurrentPage"/> is
/// <see cref="PanelPage.List"/> or <see cref="PanelPage.ConfirmDelete"/>; MainWindow's code-behind moves
/// focus into the TypePicker/Editor views for the other two pages, which own their own key handling.
///
/// Two list modes, switched purely by whether <see cref="SearchText"/> is non-blank (spec §6.1/§6.2):
/// nav mode lists the current folder's children (folders first); search mode lists ranked whole-tree
/// results from <see cref="SearchService"/>, independent of the current folder, with a breadcrumb per
/// row instead of the folder-relative secondary text. Every editing shortcut (spec §6.3) acts on the
/// selected node in either mode; Ctrl+↑/↓ is the one exception (nav mode only - search order is by
/// score, not a position that can be nudged).
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const string EmptyFolderMessage = "This folder is empty — press Ctrl+N to add";
    private const string NoResultsMessage = "No results";
    private const string NavHint = "↵ open  → enter  ← back  Ctrl+N add  F2 edit  Ctrl+I icon  Del delete";
    private const string SearchHint = "↵ open  Ctrl+↵ show in folder  Esc clear";
    private const string EditorHint = "Enter save  Esc cancel  Alt+A advanced";
    private const string TypePickerHint = "↑↓ choose  ↵ select  Esc cancel";
    private const string IconPickerHint = "Ctrl+Tab tab  ↑↓←→ move  ↵ apply  Ctrl+0 default  Esc cancel";

    private readonly LauncherConfig _config;
    private readonly ConfigService _configService;
    private readonly LaunchService _launchService;
    private readonly SearchService _searchService;
    private readonly IconService _iconService;
    private readonly Func<string, string?> _fileDescriptionLookup;
    private readonly List<FolderNode> _folderStack = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private Node? _pendingDeleteNode;
    private Node? _iconPickerNode;

    /// <summary>
    /// Bumped every time <see cref="RefreshItems"/> rebuilds <see cref="Items"/> (folder change, search,
    /// add/edit/delete/move/...). Each <see cref="ListItemViewModel"/> captures the value current at its
    /// own creation and compares against this live counter when its background icon/missing-check
    /// completes, so a result for a row that's no longer part of the current list is discarded instead of
    /// updating a row nobody sees anymore (spec §7.4).
    /// </summary>
    private int _listGeneration;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _breadcrumbPath = "Root";

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private PanelPage _currentPage = PanelPage.List;

    [ObservableProperty]
    private TypePickerViewModel? _typePicker;

    [ObservableProperty]
    private EditorViewModel? _editor;

    [ObservableProperty]
    private IconPickerViewModel? _iconPicker;

    [ObservableProperty]
    private Node? _cutNode;

    [ObservableProperty]
    private string _deleteConfirmMessage = "";

    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(HasItems));

    partial void OnBreadcrumbPathChanged(string value) => OnPropertyChanged(nameof(Breadcrumb));

    partial void OnCurrentPageChanged(PanelPage value)
    {
        OnPropertyChanged(nameof(Breadcrumb));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(IsListPage));
        OnPropertyChanged(nameof(ShowTypePickerPage));
        OnPropertyChanged(nameof(ShowEditorPage));
        OnPropertyChanged(nameof(ShowIconPickerPage));
        OnPropertyChanged(nameof(ShowConfirmBar));
        OnPropertyChanged(nameof(ShowHintBarRow));
    }

    partial void OnCutNodeChanged(Node? value) => OnPropertyChanged(nameof(HintText));

    public bool HasItems => !IsEmpty;

    public ObservableCollection<ListItemViewModel> Items { get; } = new();

    /// <summary>True once the search box holds non-whitespace text - the sole switch between nav and search mode.</summary>
    public bool IsSearchMode => !string.IsNullOrWhiteSpace(SearchText);

    public string EmptyMessage => IsSearchMode ? NoResultsMessage : EmptyFolderMessage;

    /// <summary>Breadcrumb path plus a page suffix while adding/editing (e.g. "Root › Dev · New item" / "· Edit").</summary>
    public string Breadcrumb => CurrentPage switch
    {
        PanelPage.TypePicker => BreadcrumbPath + " · New item",
        PanelPage.Editor => BreadcrumbPath + (Editor?.Mode == EditorMode.Add ? " · New item" : " · Edit"),
        PanelPage.IconPicker => BreadcrumbPath + " · Icon",
        _ => BreadcrumbPath,
    };

    public string HintText => CurrentPage switch
    {
        PanelPage.Editor => EditorHint,
        PanelPage.TypePicker => TypePickerHint,
        PanelPage.IconPicker => IconPickerHint,
        _ => IsSearchMode ? SearchHint : (CutNode is not null ? $"Cut: {CutNode.Name} — go to a folder and press Ctrl+V" : NavHint),
    };

    public bool ShowHintBar => _config.Settings.ShowHintBar;

    /// <summary>The hint bar row is replaced by the inline delete-confirm bar on that page.</summary>
    public bool ShowHintBarRow => ShowHintBar && CurrentPage != PanelPage.ConfirmDelete;

    public bool ShowConfirmBar => CurrentPage == PanelPage.ConfirmDelete;

    /// <summary>Search box + item list are visible on List and ConfirmDelete (the confirm bar is an overlay, not a page swap).</summary>
    public bool IsListPage => CurrentPage is PanelPage.List or PanelPage.ConfirmDelete;

    public bool ShowTypePickerPage => CurrentPage == PanelPage.TypePicker;

    public bool ShowEditorPage => CurrentPage == PanelPage.Editor;

    public bool ShowIconPickerPage => CurrentPage == PanelPage.IconPicker;

    public int MaxVisibleItems => _config.Settings.MaxVisibleItems;

    public bool CanNavigateUp => _folderStack.Count > 1;

    public FolderNode CurrentFolder => _folderStack[^1];

    public ListItemViewModel? SelectedItem =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    public Node? SelectedNode => SelectedItem?.Node;

    /// <summary>Raised when a launch succeeds and settings.closeAfterLaunch is true.</summary>
    public event Action? RequestHide;

    public MainViewModel(ConfigService configService, LaunchService launchService, SearchService searchService, IconService iconService, Func<string, string?> fileDescriptionLookup)
    {
        _configService = configService;
        _config = configService.Config;
        _launchService = launchService;
        _searchService = searchService;
        _iconService = iconService;
        _fileDescriptionLookup = fileDescriptionLookup;
        _folderStack.Add(_config.Root);

        if (!string.IsNullOrEmpty(configService.LoadError))
        {
            ErrorMessage = configService.LoadError;
        }

        _launchService.ErrorOccurred += OnLaunchServiceError;
        RefreshItems();
    }

    private void OnLaunchServiceError(string message)
    {
        // LaunchService raises this from a background thread when monitoring a hidden command's exit
        // code, so the property change (and the UI binding update it triggers) must be marshaled back
        // onto the thread this VM was created on.
        if (_dispatcher.CheckAccess())
        {
            ErrorMessage = message;
        }
        else
        {
            _dispatcher.BeginInvoke(() => ErrorMessage = message);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ClearErrorMessage();
        OnPropertyChanged(nameof(IsSearchMode));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(HintText));
        RefreshItems();
    }

    partial void OnErrorMessageChanged(string value) => HasError = !string.IsNullOrEmpty(value);

    public void ClearErrorMessage() => ErrorMessage = "";

    /// <summary>Called every time the panel is shown (spec: always starts at root) and whenever it's hidden - resets to a clean List page so a half-finished edit never lingers across show/hide.</summary>
    public void ResetToRoot()
    {
        DetachTypePicker();
        DetachEditor();
        DetachIconPicker();
        _pendingDeleteNode = null;
        CurrentPage = PanelPage.List;

        _folderStack.Clear();
        _folderStack.Add(_config.Root);
        SearchText = "";
        UpdateBreadcrumb();
        RefreshItems();
    }

    /// <summary>Cut state is cleared when the launcher is hidden (spec §6.3), independently of ResetToRoot (which also runs on the next show).</summary>
    public void ClearCutState() => CutNode = null;

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var next = (SelectedIndex + delta) % Items.Count;
        if (next < 0)
        {
            next += Items.Count;
        }

        SelectedIndex = next;
    }

    public void SelectFirst()
    {
        if (Items.Count > 0)
        {
            SelectedIndex = 0;
        }
    }

    public void SelectLast()
    {
        if (Items.Count > 0)
        {
            SelectedIndex = Items.Count - 1;
        }
    }

    public void PageMove(int direction)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var step = direction * Math.Max(1, MaxVisibleItems);
        SelectedIndex = Math.Clamp(SelectedIndex + step, 0, Items.Count - 1);
    }

    /// <summary>Enter (spec §6.1 nav mode / §6.2 search mode): folder result → enter it (search: navigate there directly); anything else → launch.</summary>
    public void EnterSelected()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        if (IsSearchMode)
        {
            if (item.IsFolder)
            {
                EnterFolderFromSearchResult(item);
            }
            else
            {
                Launch(item.Node);
            }
        }
        else if (item.Node is FolderNode folder)
        {
            EnterFolder(folder);
        }
        else
        {
            Launch(item.Node);
        }
    }

    /// <summary>Enters the given folder if it's a folder; no-op otherwise (used by nav-mode →/Tab, which only act on folders).</summary>
    public void EnterFolderIfSelected()
    {
        if (SelectedNode is FolderNode folder)
        {
            EnterFolder(folder);
        }
    }

    public void EnterFolder(FolderNode folder)
    {
        _folderStack.Add(folder);
        SearchText = "";
        UpdateBreadcrumb();
        RefreshItems();
    }

    public void NavigateUp()
    {
        if (!CanNavigateUp)
        {
            return;
        }

        var leaving = _folderStack[^1];
        _folderStack.RemoveAt(_folderStack.Count - 1);
        SearchText = "";
        UpdateBreadcrumb();
        RefreshItems();

        var indexOfLeaving = Items.ToList().FindIndex(i => ReferenceEquals(i.Node, leaving));
        SelectedIndex = indexOfLeaving >= 0 ? indexOfLeaving : (Items.Count > 0 ? 0 : -1);
    }

    /// <summary>Search-mode Enter on a folder result: jump straight into it using its full parent chain, so breadcrumb/← work correctly.</summary>
    private void EnterFolderFromSearchResult(ListItemViewModel item)
    {
        if (item.Node is not FolderNode folder)
        {
            return;
        }

        _folderStack.Clear();
        _folderStack.AddRange(item.ParentChain);
        _folderStack.Add(folder);
        UpdateBreadcrumb();
        SearchText = ""; // switches back to nav mode and triggers RefreshItems() via OnSearchTextChanged.
    }

    /// <summary>Ctrl+Enter (search mode): clear search, open the result's containing folder, and select the result there.</summary>
    public void ShowSelectedInFolder()
    {
        var item = SelectedItem;
        if (item is null || item.ParentChain.Count == 0)
        {
            return;
        }

        _folderStack.Clear();
        _folderStack.AddRange(item.ParentChain);
        UpdateBreadcrumb();
        SearchText = ""; // switches back to nav mode and triggers RefreshItems() via OnSearchTextChanged.

        var indexInFolder = Items.ToList().FindIndex(i => ReferenceEquals(i.Node, item.Node));
        SelectedIndex = indexInFolder >= 0 ? indexInFolder : (Items.Count > 0 ? 0 : -1);
    }

    private void Launch(Node node)
    {
        var launched = _launchService.Launch(node, _config.Settings);
        if (launched && _config.Settings.CloseAfterLaunch)
        {
            RequestHide?.Invoke();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Phase 3: add / edit (type picker + editor)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// True (and sets the read-only error line) when config.json failed to load at startup - editing
    /// must not overwrite that untouched file, so every mutating entry point below refuses outright
    /// (spec: "edits are refused") rather than letting a change happen in memory that can never be
    /// saved. Checked before anything else in each entry point, so no TreeOps mutation ever runs and -
    /// for F2 in particular - the editor (which mutates its target node in place as soon as it saves)
    /// never even opens.
    /// </summary>
    private bool BlockIfReadOnly()
    {
        if (!_configService.IsReadOnly)
        {
            return false;
        }

        ErrorMessage = "Config is read-only because config.json could not be read. Fix or restore it first.";
        return true;
    }

    /// <summary>Ctrl+N: open the type picker for the current folder (works in nav and search mode - CurrentFolder doesn't change while searching).</summary>
    public void BeginAddNode()
    {
        if (BlockIfReadOnly())
        {
            return;
        }

        var picker = new TypePickerViewModel();
        picker.Selected += OnTypePicked;
        picker.Cancelled += OnTypePickerCancelled;
        TypePicker = picker;
        CurrentPage = PanelPage.TypePicker;
    }

    /// <summary>Ctrl+Shift+N: skip the picker, open the editor directly for a new folder.</summary>
    public void BeginAddFolderDirect()
    {
        if (BlockIfReadOnly())
        {
            return;
        }

        OpenEditorForAdd(NodeKind.Folder);
    }

    /// <summary>F2: edit the selected node.</summary>
    public void BeginEditSelected()
    {
        if (BlockIfReadOnly() || SelectedNode is null)
        {
            return;
        }

        AttachEditor(EditorViewModel.ForEdit(SelectedNode, _fileDescriptionLookup));
    }

    // ---------------------------------------------------------------------------------------------
    // Phase 4: icon picker (Ctrl+I, spec §6.3/§4).
    // ---------------------------------------------------------------------------------------------

    /// <summary>Ctrl+I: open the icon picker for the selected node.</summary>
    public void BeginChangeIcon()
    {
        if (BlockIfReadOnly() || SelectedNode is null)
        {
            return;
        }

        _iconPickerNode = SelectedNode;
        var picker = new IconPickerViewModel(SelectedNode, _iconService, ConfigService.ResolveConfigDirectory());
        picker.Applied += OnIconApplied;
        picker.Cancelled += OnIconPickerCancelled;
        IconPicker = picker;
        CurrentPage = PanelPage.IconPicker;
    }

    private void OnIconApplied(IconSpec? spec)
    {
        var node = _iconPickerNode;
        _iconPickerNode = null;
        DetachIconPicker();
        CurrentPage = PanelPage.List;

        if (node is null)
        {
            return;
        }

        // The just-picked key never needs to show a stale bitmap under a coincidentally-identical key
        // (spec §4 "Applying: ... invalidate that key") - harmless no-op the overwhelming majority of the
        // time, since a genuinely new spec/value produces a fresh key that was never cached.
        var newKey = IconKey.For(spec, node);
        if (newKey is not null)
        {
            _iconService.InvalidateKey(newKey);
        }

        node.Icon = spec;
        CommitChangeAndSelect(node);
    }

    private void OnIconPickerCancelled()
    {
        DetachIconPicker();
        CurrentPage = PanelPage.List;
    }

    private void DetachIconPicker()
    {
        _iconPickerNode = null;

        if (IconPicker is null)
        {
            return;
        }

        IconPicker.Applied -= OnIconApplied;
        IconPicker.Cancelled -= OnIconPickerCancelled;
        IconPicker = null;
    }

    private void OnTypePicked(NodeKind kind)
    {
        DetachTypePicker();
        OpenEditorForAdd(kind);
    }

    private void OnTypePickerCancelled()
    {
        DetachTypePicker();
        CurrentPage = PanelPage.List;
    }

    private void DetachTypePicker()
    {
        if (TypePicker is null)
        {
            return;
        }

        TypePicker.Selected -= OnTypePicked;
        TypePicker.Cancelled -= OnTypePickerCancelled;
        TypePicker = null;
    }

    private void OpenEditorForAdd(NodeKind kind) => AttachEditor(EditorViewModel.ForAdd(kind, _fileDescriptionLookup));

    private void AttachEditor(EditorViewModel editor)
    {
        editor.Saved += OnEditorSaved;
        editor.Cancelled += OnEditorCancelled;
        Editor = editor;
        CurrentPage = PanelPage.Editor;
    }

    private void DetachEditor()
    {
        if (Editor is null)
        {
            return;
        }

        Editor.Saved -= OnEditorSaved;
        Editor.Cancelled -= OnEditorCancelled;
        Editor = null;
    }

    private void OnEditorCancelled()
    {
        DetachEditor();
        CurrentPage = PanelPage.List;
    }

    private void OnEditorSaved(Node node)
    {
        var wasAdd = Editor!.Mode == EditorMode.Add;
        var warning = Editor.WarningMessage;
        DetachEditor();
        CurrentPage = PanelPage.List;

        if (wasAdd)
        {
            TreeOps.Add(CurrentFolder, node);

            // Ctrl+N in search mode adds to the folder searched from, then shows it there (spec §6.3),
            // rather than leaving a stale search that doesn't include the just-added node.
            if (IsSearchMode)
            {
                SearchText = "";
            }
        }
        // Edit mode: `node` is the same object the tree already holds, mutated in place - no tree
        // surgery needed, just save/refresh/reselect below.

        CommitChangeAndSelect(node);

        // The editor closes on save, so surface its non-blocking warning on the list's message line
        // (unless the save itself failed and already put an error there).
        if (!string.IsNullOrEmpty(warning) && string.IsNullOrEmpty(ErrorMessage))
        {
            ErrorMessage = warning;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Phase 3: delete (spec §6.3)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Delete: shows the inline confirm bar instead of deleting immediately.</summary>
    public void BeginDelete()
    {
        if (BlockIfReadOnly() || SelectedNode is null)
        {
            return;
        }

        _pendingDeleteNode = SelectedNode;
        var descendantCount = CountDescendants(_pendingDeleteNode);
        DeleteConfirmMessage = _pendingDeleteNode is FolderNode && descendantCount > 0
            ? $"Delete folder '{_pendingDeleteNode.Name}' and its {descendantCount} item{(descendantCount == 1 ? "" : "s")}? Enter = delete, Esc = cancel"
            : $"Delete '{_pendingDeleteNode.Name}'? Enter = delete, Esc = cancel";
        CurrentPage = PanelPage.ConfirmDelete;
    }

    /// <summary>Enter on the confirm bar.</summary>
    public void ConfirmPendingDelete()
    {
        var node = _pendingDeleteNode;
        _pendingDeleteNode = null;
        CurrentPage = PanelPage.List;

        if (node is null)
        {
            return;
        }

        var deletedAt = Items.ToList().FindIndex(i => ReferenceEquals(i.Node, node));
        if (ReferenceEquals(CutNode, node))
        {
            CutNode = null;
        }

        TreeOps.Remove(_config.Root, node);
        SaveAndRebuildIndex();
        RefreshItems();

        SelectedIndex = Items.Count == 0 ? -1 : Math.Clamp(deletedAt, 0, Items.Count - 1);
    }

    /// <summary>Esc, or any other navigation key, while the confirm bar is showing.</summary>
    public void CancelPendingDelete()
    {
        _pendingDeleteNode = null;
        CurrentPage = PanelPage.List;
    }

    private static int CountDescendants(Node node)
    {
        if (node is not FolderNode folder)
        {
            return 0;
        }

        var count = 0;
        foreach (var child in folder.Children)
        {
            count += 1 + CountDescendants(child);
        }

        return count;
    }

    // ---------------------------------------------------------------------------------------------
    // Phase 3: move / cut-paste / duplicate (spec §6.3)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Ctrl+↑ - nav mode only; ignored in search mode (order there is by score).</summary>
    public void MoveSelectedUp() => MoveSelected(TreeOps.MoveUp);

    /// <summary>Ctrl+↓ - nav mode only; ignored in search mode.</summary>
    public void MoveSelectedDown() => MoveSelected(TreeOps.MoveDown);

    private void MoveSelected(Func<FolderNode, Node, bool> move)
    {
        if (IsSearchMode || SelectedNode is null || BlockIfReadOnly())
        {
            return;
        }

        var node = SelectedNode;
        if (!move(_config.Root, node))
        {
            return;
        }

        SaveAndRebuildIndex();
        RefreshItems();
        SelectNode(node);
    }

    /// <summary>Ctrl+X: marks the selected node as cut (dimmed in the list) until it's pasted, another node is cut, or the launcher is hidden.</summary>
    public void CutSelected()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var node = SelectedNode;
        CutNode = node;
        RefreshItems();
        SelectNode(node);
    }

    /// <summary>Ctrl+V: moves the cut node into the current folder.</summary>
    public void PasteIntoCurrentFolder()
    {
        var node = CutNode;
        if (node is null || BlockIfReadOnly())
        {
            return;
        }

        switch (TreeOps.MoveTo(_config.Root, node, CurrentFolder))
        {
            case MoveOutcome.Rejected:
                ErrorMessage = "Cannot move a folder into itself.";
                break;

            case MoveOutcome.NoOp:
                CutNode = null;
                RefreshItems();
                break;

            case MoveOutcome.Moved:
                CutNode = null;
                SaveAndRebuildIndex();
                RefreshItems();
                SelectNode(node);
                break;
        }
    }

    /// <summary>Ctrl+D: duplicates the selected node right after itself and selects the copy.</summary>
    public void DuplicateSelected()
    {
        if (SelectedNode is null || BlockIfReadOnly())
        {
            return;
        }

        var clone = TreeOps.Duplicate(_config.Root, SelectedNode);
        if (clone is null)
        {
            return;
        }

        SaveAndRebuildIndex();
        RefreshItems();
        SelectNode(clone);
    }

    // ---------------------------------------------------------------------------------------------
    // Save pipeline (spec: atomic save after every change, then rebuild the search index, refresh the
    // list, and restore selection to the affected node).
    // ---------------------------------------------------------------------------------------------

    private void CommitChangeAndSelect(Node affectedNode)
    {
        SaveAndRebuildIndex();
        RefreshItems();
        SelectNode(affectedNode);
    }

    private void SaveAndRebuildIndex()
    {
        var result = _configService.Save();
        if (!result.Success)
        {
            ErrorMessage = result.Error ?? "Could not save config.";
        }

        _searchService.Rebuild(_config);
    }

    private void SelectNode(Node node)
    {
        var index = Items.ToList().FindIndex(i => ReferenceEquals(i.Node, node));
        SelectedIndex = index >= 0 ? index : (Items.Count > 0 ? 0 : -1);
    }

    private void RefreshItems()
    {
        // Every row created below captures this value (spec §7.4) - bump it first so a background
        // icon/missing-check that finishes after the *next* rebuild never touches a row that's no longer shown.
        _listGeneration++;

        // Clearing Items resets the ListBox's own selection; force a change notification so the
        // OneWay SelectedIndex binding re-applies even when the new index equals the old one.
        SelectedIndex = -1;
        Items.Clear();

        if (IsSearchMode)
        {
            var stopwatch = Stopwatch.StartNew();
            var results = _searchService.Search(SearchText);
            stopwatch.Stop();
            Debug.WriteLine(
                $"[YourLauncher] Search '{SearchText}' -> {results.Count} result(s) in {stopwatch.Elapsed.TotalMilliseconds:F2} ms");

            foreach (var result in results)
            {
                Items.Add(ToSearchListItem(result));
            }
        }
        else
        {
            var children = CurrentFolder.Children;
            var ordered = children.OfType<FolderNode>().Cast<Node>()
                .Concat(children.Where(n => n is not FolderNode));

            foreach (var node in ordered)
            {
                Items.Add(ToListItem(node));
            }
        }

        IsEmpty = Items.Count == 0;
        SelectedIndex = Items.Count > 0 ? 0 : -1;
    }

    /// <summary>Fallback glyph shown behind IconImage/EmojiText, and EmojiText itself (spec §4.3 display priority: IconImage -> EmojiText -> Glyph). A custom glyph value that isn't a valid curated code point (spec §7.12, e.g. a hand-edited config) falls back to the type default.</summary>
    private static (string Glyph, string? EmojiText) GetIconDisplay(Node node) => node.Icon switch
    {
        { Kind: IconKind.Emoji } spec when spec.Value.Length > 0 => (IconGlyphs.DefaultFor(node), spec.Value),
        { Kind: IconKind.Glyph } spec when IconGlyphs.IsValidCustomGlyph(spec.Value) => (spec.Value, null),
        _ => (IconGlyphs.DefaultFor(node), null),
    };

    private ListItemViewModel ToListItem(Node node)
    {
        var secondary = node switch
        {
            FolderNode folder => $"{folder.Children.Count} item{(folder.Children.Count == 1 ? "" : "s")}",
            CommandNode command => Truncate(command.Command, 40),
            _ => "",
        };

        var (glyph, emoji) = GetIconDisplay(node);
        var item = new ListItemViewModel
        {
            Node = node,
            Name = node.Name,
            Glyph = glyph,
            EmojiText = emoji,
            SecondaryText = secondary,
            IsCut = ReferenceEquals(node, CutNode),
        };
        item.ConfigureIconLoading(_iconService, node.Icon, _listGeneration, () => _listGeneration);
        return item;
    }

    private ListItemViewModel ToSearchListItem(SearchResult result)
    {
        var (glyph, emoji) = GetIconDisplay(result.Node);
        var item = new ListItemViewModel
        {
            Node = result.Node,
            Name = result.Node.Name,
            Glyph = glyph,
            EmojiText = emoji,
            SecondaryText = result.Breadcrumb,
            HighlightPositions = result.NamePositions,
            ParentChain = result.ParentChain,
            IsCut = ReferenceEquals(result.Node, CutNode),
        };
        item.ConfigureIconLoading(_iconService, result.Node.Icon, _listGeneration, () => _listGeneration);
        return item;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    private void UpdateBreadcrumb() => BreadcrumbPath = string.Join(" › ", _folderStack.Select(f => f.Name));
}
