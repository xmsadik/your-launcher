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
    private const string SettingsHint = "Tab move  Enter save  Esc cancel";

    private readonly LauncherConfig _config;
    private readonly ConfigService _configService;
    private readonly LaunchService _launchService;
    private readonly SearchService _searchService;
    private readonly IconService _iconService;
    private readonly Func<string, string?> _fileDescriptionLookup;
    private readonly Action<string> _recordUsage;
    private readonly Action _pruneUsage;
    private readonly Func<string, string?> _tryApplyHotkey;
    private readonly Action _beginHotkeyCapture;
    private readonly Action _endHotkeyCaptureRestore;
    private readonly Func<string, ShellLinkInfo> _resolveShellLink;
    private readonly List<FolderNode> _folderStack = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private Node? _pendingDeleteNode;
    private Node? _iconPickerNode;
    private LauncherConfig? _pendingImportConfig;

    /// <summary>Folder id the panel was showing right before the last hide, consulted by <see cref="ResetToRoot"/> only when settings.rememberLastLocation is true (spec §1.2) - kept in memory only, never persisted.</summary>
    private string? _lastLocationFolderId;

    /// <summary>
    /// Set by <see cref="RequestReload"/> while the Editor/TypePicker/IconPicker page is open (spec §10
    /// item 5): applying a reload right then would swap <see cref="_config"/>'s Root out from under a
    /// still-in-progress edit (the Editor/IconPicker hold direct references into the *old* tree). Flushed
    /// the moment the page returns to List (Cancel, or a completed Save/Apply) or the panel is hidden.
    /// </summary>
    private bool _reloadPending;

    private DispatcherTimer? _reloadRetryTimer;

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
    private SettingsViewModel? _settings;

    [ObservableProperty]
    private Node? _cutNode;

    [ObservableProperty]
    private string _deleteConfirmMessage = "";

    [ObservableProperty]
    private string _importConfirmMessage = "";

    /// <summary>Raised after a settings save succeeds (spec §10 item 2) - App handles StartupService.SetEnabled + ThemeService.Apply, since this VM stays free of Win32/registry/WPF-resource references.</summary>
    public event Action? SettingsApplied;

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
        OnPropertyChanged(nameof(ShowSettingsPage));
        OnPropertyChanged(nameof(ShowConfirmBar));
        OnPropertyChanged(nameof(ShowConfirmImportBar));
        OnPropertyChanged(nameof(IsConfirmBarActive));
        OnPropertyChanged(nameof(ShowHintBarRow));

        // A reload deferred while Editor/TypePicker/IconPicker was open (spec §10 item 5) is safe to apply
        // the moment we're back on the list - whether that's via Cancel or a completed Save/Apply.
        if (value == PanelPage.List && _reloadPending)
        {
            _reloadPending = false;
            ApplyExternalReload();
        }
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
        PanelPage.Settings => BreadcrumbPath + " · Settings",
        _ => BreadcrumbPath,
    };

    public string HintText => CurrentPage switch
    {
        PanelPage.Editor => EditorHint,
        PanelPage.TypePicker => TypePickerHint,
        PanelPage.IconPicker => IconPickerHint,
        PanelPage.Settings => SettingsHint,
        _ => IsSearchMode ? SearchHint : (CutNode is not null ? $"Cut: {CutNode.Name} — go to a folder and press Ctrl+V" : NavHint),
    };

    public bool ShowHintBar => _config.Settings.ShowHintBar;

    /// <summary>The hint bar row is replaced by the inline delete-/import-confirm bar on those pages.</summary>
    public bool ShowHintBarRow => ShowHintBar && CurrentPage is not (PanelPage.ConfirmDelete or PanelPage.ConfirmImport);

    public bool ShowConfirmBar => CurrentPage == PanelPage.ConfirmDelete;

    public bool ShowConfirmImportBar => CurrentPage == PanelPage.ConfirmImport;

    /// <summary>Either inline confirm bar being up makes the search box read-only (spec: nav keys there mean confirm/cancel, not typing).</summary>
    public bool IsConfirmBarActive => ShowConfirmBar || ShowConfirmImportBar;

    /// <summary>Search box + item list are visible on List, ConfirmDelete and ConfirmImport (the confirm bars are overlays, not page swaps).</summary>
    public bool IsListPage => CurrentPage is PanelPage.List or PanelPage.ConfirmDelete or PanelPage.ConfirmImport;

    public bool ShowTypePickerPage => CurrentPage == PanelPage.TypePicker;

    public bool ShowEditorPage => CurrentPage == PanelPage.Editor;

    public bool ShowIconPickerPage => CurrentPage == PanelPage.IconPicker;

    public bool ShowSettingsPage => CurrentPage == PanelPage.Settings;

    public int MaxVisibleItems => _config.Settings.MaxVisibleItems;

    public bool CanNavigateUp => _folderStack.Count > 1;

    public FolderNode CurrentFolder => _folderStack[^1];

    public ListItemViewModel? SelectedItem =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    public Node? SelectedNode => SelectedItem?.Node;

    /// <summary>Raised when a launch succeeds and settings.closeAfterLaunch is true.</summary>
    public event Action? RequestHide;

    /// <summary>Raised when an external config.json change turns out to be genuinely unparsable even after the Notepad-style truncate-then-write retry (spec §10 item 3) - App.xaml.cs shows a tray balloon for this.</summary>
    public event Action<string>? ReloadFailed;

    /// <summary>True while config.json is corrupt (startup or reload) *and* some recovery action exists - either "keep current" (there's good in-memory data) or "restore backup" (there is one).</summary>
    public bool CanRecoverFromCorruption => _configService.IsReadOnly && (_configService.HasEverLoadedSuccessfully || _configService.HasBackup);

    /// <summary>Passthrough for MainWindow's context menu (spec §5: "Edit items disabled in read-only mode") and the in-list drag guard (spec §7) - true while config.json failed to load and every mutating entry point above refuses (<see cref="BlockIfReadOnly"/>).</summary>
    public bool IsReadOnly => _configService.IsReadOnly;

    /// <summary>Menu/shortcut label: "keep current" once there's real in-memory data to keep, otherwise "restore backup" (spec §10 item 4).</summary>
    public string RecoveryMenuText => _configService.HasEverLoadedSuccessfully ? "Keep my current version" : "Restore from backup";

    private string? RecoveryHint => !CanRecoverFromCorruption
        ? null
        : _configService.HasEverLoadedSuccessfully
            ? "Ctrl+Shift+R: keep current version"
            : "Ctrl+Shift+R: restore backup";

    public MainViewModel(
        ConfigService configService,
        LaunchService launchService,
        SearchService searchService,
        IconService iconService,
        Func<string, string?> fileDescriptionLookup,
        Action<string> recordUsage,
        Action pruneUsage,
        Func<string, string?> tryApplyHotkey,
        Action beginHotkeyCapture,
        Action endHotkeyCaptureRestore,
        Func<string, ShellLinkInfo> resolveShellLink)
    {
        _configService = configService;
        _config = configService.Config;
        _launchService = launchService;
        _searchService = searchService;
        _iconService = iconService;
        _fileDescriptionLookup = fileDescriptionLookup;
        _recordUsage = recordUsage;
        _pruneUsage = pruneUsage;
        _tryApplyHotkey = tryApplyHotkey;
        _beginHotkeyCapture = beginHotkeyCapture;
        _endHotkeyCaptureRestore = endHotkeyCaptureRestore;
        _resolveShellLink = resolveShellLink;
        _folderStack.Add(_config.Root);

        if (!string.IsNullOrEmpty(configService.LoadError))
        {
            SetCorruptErrorMessage(configService.LoadError);
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

    /// <summary>
    /// Clears a one-off error/warning (launch failure, "target not found", ...). A read-only/corrupt
    /// config is a standing state rather than a one-off message, though (spec §10 item 4) - it must
    /// survive every show/hide cycle and keystroke until the user actually fixes or recovers it, so this
    /// re-derives it instead of blanking it. Without this, <see cref="MainWindow.ShowLauncher"/>'s
    /// unconditional clear-on-show wiped the startup "config could not be read" message before the panel
    /// was ever shown, leaving a silently-empty root with no clue anything was wrong (bug found in Phase 5
    /// verification).
    /// </summary>
    public void ClearErrorMessage()
    {
        if (_configService.IsReadOnly)
        {
            SetCorruptErrorMessage(_configService.LoadError!);
            return;
        }

        ErrorMessage = "";
    }

    /// <summary>
    /// Called every time the panel is shown and whenever it's hidden - resets to a clean List page so a
    /// half-finished edit never lingers across show/hide. Normally goes to root (spec: launcher always
    /// starts at root); when settings.rememberLastLocation is true (spec §1.2) it instead reopens the
    /// folder that was current at the *last hide* (tracked in <see cref="_lastLocationFolderId"/>, by id,
    /// falling back to root if that folder no longer exists) - the id walk mirrors
    /// <see cref="OnConfigReloadedSuccess"/>'s own folder-stack rebuild.
    /// </summary>
    public void ResetToRoot()
    {
        DetachTypePicker();
        DetachEditor();
        DetachIconPicker();
        DetachSettings();
        _pendingDeleteNode = null;
        _pendingImportConfig = null;
        CurrentPage = PanelPage.List;

        _folderStack.Clear();
        _folderStack.Add(_config.Root);

        if (_config.Settings.RememberLastLocation && _lastLocationFolderId is { } lastId)
        {
            var found = FindChildFolderByIdRecursive(_config.Root, lastId);
            if (found is not null)
            {
                _folderStack.Clear();
                _folderStack.AddRange(found);
            }
        }

        SearchText = "";
        UpdateBreadcrumb();
        RefreshItems();
    }

    /// <summary>Cut state is cleared when the launcher is hidden (spec §6.3), independently of ResetToRoot (which also runs on the next show).</summary>
    public void ClearCutState() => CutNode = null;

    /// <summary>Called by MainWindow.HideLauncher() (spec §1.2) - remembers the current folder in memory only, for the next ResetToRoot() to consult if settings.rememberLastLocation is on.</summary>
    public void RememberCurrentLocation()
    {
        if (_config.Settings.RememberLastLocation)
        {
            _lastLocationFolderId = CurrentFolder.Id;
        }
    }

    /// <summary>Root..folder chain down to the folder with the given id, or null if it no longer exists (spec §1.2's "fall back to root if it no longer exists").</summary>
    private static List<FolderNode>? FindChildFolderByIdRecursive(FolderNode root, string id)
    {
        if (root.Id == id)
        {
            return new List<FolderNode> { root };
        }

        foreach (var child in root.Children.OfType<FolderNode>())
        {
            var found = FindChildFolderByIdRecursive(child, id);
            if (found is not null)
            {
                found.Insert(0, root);
                return found;
            }
        }

        return null;
    }

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
        if (launched)
        {
            // Spec §8.4/§10 item 10: recorded for every successful launch. Folders never reach here
            // (EnterSelected routes them to EnterFolder instead), so "all node types except folders" is
            // satisfied automatically.
            _recordUsage(node.Id);
        }

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
    /// <summary>MainWindow's external-drop path (spec §6: "Read-only → reject with error line") - a rejected drag never reaches Drop, so the error line is shown when the drag enters instead.</summary>
    public void ShowReadOnlyError() => BlockIfReadOnly();

    private bool BlockIfReadOnly()
    {
        if (!_configService.IsReadOnly)
        {
            return false;
        }

        SetCorruptErrorMessage("Config is read-only because config.json could not be read. Fix or restore it first.");
        return true;
    }

    /// <summary>Appends the applicable Ctrl+Shift+R recovery hint (spec §10 item 4) to a read-only/corrupt error line, when one applies.</summary>
    private void SetCorruptErrorMessage(string baseMessage)
    {
        var hint = RecoveryHint;
        ErrorMessage = hint is null ? baseMessage : $"{baseMessage} {hint}";
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
        if (RefuseSaveIfReloadPending())
        {
            _iconPickerNode = null;
            DetachIconPicker();
            CurrentPage = PanelPage.List;
            return;
        }

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
        if (RefuseSaveIfReloadPending())
        {
            DetachEditor();
            CurrentPage = PanelPage.List;
            return;
        }

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
    // Phase 6: settings page (Ctrl+,, spec §11) and import/export (spec §10/§11, revised §10 item 9).
    // ---------------------------------------------------------------------------------------------

    /// <summary>Ctrl+,, the tray's Settings…, or a click on the hotkey-failure balloon. Opens even when read-only (spec: "page opens but Save is refused").</summary>
    public void BeginSettings() => OpenSettings(hotkeyErrorToShow: null);

    /// <summary>Spec §10 item 13: a startup/reload hotkey failure opens the panel straight to Settings with the error line showing and the hotkey box focused, in addition to the tray balloon.</summary>
    public void OpenSettingsWithHotkeyError(string message) => OpenSettings(message);

    private void OpenSettings(string? hotkeyErrorToShow)
    {
        var vm = new SettingsViewModel(
            _config.Settings,
            _configService.Directory,
            _configService.IsReadOnly,
            _tryApplyHotkey,
            _beginHotkeyCapture,
            _endHotkeyCaptureRestore,
            () => ConfigSerializer.Serialize(_config),
            hotkeyErrorToShow);
        vm.Saved += OnSettingsSaved;
        vm.Cancelled += OnSettingsCancelled;
        vm.ImportParsed += OnImportParsed;
        Settings = vm;
        CurrentPage = PanelPage.Settings;
    }

    private void DetachSettings()
    {
        if (Settings is null)
        {
            return;
        }

        Settings.Saved -= OnSettingsSaved;
        Settings.Cancelled -= OnSettingsCancelled;
        Settings.ImportParsed -= OnImportParsed;
        Settings = null;
    }

    private void OnSettingsCancelled()
    {
        DetachSettings();
        CurrentPage = PanelPage.List;
    }

    private void OnSettingsSaved(Settings newSettings)
    {
        if (RefuseSaveIfReloadPending())
        {
            DetachSettings();
            CurrentPage = PanelPage.List;
            return;
        }

        _config.Settings = newSettings;
        DetachSettings();
        CurrentPage = PanelPage.List;

        var result = _configService.Save();
        if (!result.Success)
        {
            ErrorMessage = result.Error ?? "Could not save config.";
        }

        // Live-apply (spec §10 item 2): the hotkey itself was already applied by _tryApplyHotkey before
        // this event fired; StartWithWindows/theme need Win32/WPF-resource access this VM deliberately
        // doesn't have, so SettingsApplied hands those to App. maxVisibleItems/showHintBar/closeAfterLaunch/
        // defaultShell need nothing beyond the property-changed notifications ApplySettings() raises.
        ApplySettings();
        SettingsApplied?.Invoke();
    }

    /// <summary>The live-apply projection of a settings change (spec §10 item 2) - shared between a settings-page save and a config reload that changed settings, so there's exactly one place that decides what "apply settings" means.</summary>
    private void ApplySettings()
    {
        OnPropertyChanged(nameof(MaxVisibleItems));
        OnPropertyChanged(nameof(ShowHintBar));
        OnPropertyChanged(nameof(ShowHintBarRow));
    }

    private void OnImportParsed(LauncherConfig imported)
    {
        DetachSettings();

        if (BlockIfReadOnly())
        {
            CurrentPage = PanelPage.List;
            return;
        }

        _pendingImportConfig = imported;
        ImportConfirmMessage = "Import this config? M = Merge (append; renames colliding ids) · R = Replace (keeps current settings) · Esc = cancel";
        CurrentPage = PanelPage.ConfirmImport;
    }

    public void ConfirmImportMerge() => ApplyImport(merge: true);

    public void ConfirmImportReplace() => ApplyImport(merge: false);

    public void CancelPendingImport()
    {
        _pendingImportConfig = null;
        CurrentPage = PanelPage.List;
    }

    private void ApplyImport(bool merge)
    {
        var imported = _pendingImportConfig;
        _pendingImportConfig = null;
        CurrentPage = PanelPage.List;

        if (imported is null)
        {
            return;
        }

        int count;
        if (merge)
        {
            count = ConfigImport.Merge(_config.Root, imported.Root);
        }
        else
        {
            // Replace keeps current settings untouched (spec §10 revision item 9) - only Root swaps.
            count = ConfigImport.CountDescendants(imported.Root);
            _config.Root = imported.Root;
        }

        SaveAndRebuildIndex();
        _pruneUsage();

        _folderStack.Clear();
        _folderStack.Add(_config.Root);
        SearchText = "";
        UpdateBreadcrumb();
        RefreshItems();

        ErrorMessage = $"Imported {count} item{(count == 1 ? "" : "s")} ({(merge ? "merged" : "replaced")}).";
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
        _pruneUsage(); // spec §10 item 10: usage.json shouldn't keep scoring nodes that no longer exist.
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
    // Phase 6 Part B: drag & drop (spec §6/§7, revised §10 items 6/8). Drop target is always
    // CurrentFolder, in both nav and search mode (MainWindow only accepts external drops on the List page,
    // but that page still shows search results while SearchText is non-blank).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Explorer/Start-menu file drop (spec §6/AC12): maps every dropped path to a node via Core's
    /// <see cref="DropMapper"/> and adds them all to <see cref="CurrentFolder"/> in drop order, then saves
    /// once and selects the last one added.
    /// </summary>
    public void AddNodesFromDrop(IReadOnlyList<string> paths)
    {
        if (BlockIfReadOnly() || paths.Count == 0)
        {
            return;
        }

        Node? last = null;
        foreach (var path in paths)
        {
            var node = DropMapper.Map(path, _resolveShellLink, _fileDescriptionLookup);
            TreeOps.Add(CurrentFolder, node);
            last = node;
        }

        if (last is null)
        {
            return;
        }

        CommitChangeAndSelect(last);
    }

    /// <summary>
    /// In-list drag (spec §7/§10 item 8), nav mode only - MainWindow already guards search mode/read-only/
    /// non-List pages before starting a drag, this is the defense in depth. <paramref name="indicator"/> is
    /// whichever the row showed when the drop happened: <see cref="DropIndicator.Into"/> on a folder moves
    /// the source there (same as cut/paste); <see cref="DropIndicator.Above"/>/<see cref="DropIndicator.Below"/>
    /// reorder the source within its own display group (folders vs. non-folders), landing it exactly where
    /// the insertion line was shown relative to the target.
    /// </summary>
    public void MoveDraggedNode(string sourceNodeId, string targetNodeId, DropIndicator indicator)
    {
        if (BlockIfReadOnly() || IsSearchMode)
        {
            return;
        }

        var source = TreeOps.FindById(_config.Root, sourceNodeId);
        var target = TreeOps.FindById(_config.Root, targetNodeId);
        if (source is null || target is null || ReferenceEquals(source, target))
        {
            return;
        }

        if (indicator == DropIndicator.Into && target is FolderNode targetFolder)
        {
            switch (TreeOps.MoveTo(_config.Root, source, targetFolder))
            {
                case MoveOutcome.Rejected:
                    ErrorMessage = "Cannot move a folder into itself.";
                    return;
                case MoveOutcome.NoOp:
                    return;
            }
        }
        else
        {
            // Both rows must be siblings in the shown folder - a stale drag that started before the list
            // changed underneath it (e.g. double-click into a folder while still holding the button) must
            // never reorder something in a different folder.
            var parent = TreeOps.FindParent(_config.Root, target);
            if (parent is null || !ReferenceEquals(TreeOps.FindParent(_config.Root, source), parent))
            {
                return;
            }

            int finalIndex;
            if ((source is FolderNode) != (target is FolderNode))
            {
                // Across the folders-first boundary (spec §7: "clamp"): the nearest slot the source's own
                // group allows - an item dropped among folders goes first among items, a folder dropped
                // among items goes last among folders (MoveToGroupIndex clamps int.MaxValue).
                finalIndex = source is FolderNode ? int.MaxValue : 0;
            }
            else
            {
                // Final absolute position within the group, computed with the source excluded first (spec
                // §10 item 8's contract: MoveToGroupIndex places node at that exact index in the result).
                var isFolderGroup = target is FolderNode;
                var group = parent.Children.Where(n => (n is FolderNode) == isFolderGroup).ToList();
                group.Remove(source);
                var targetIndex = group.IndexOf(target);
                finalIndex = indicator == DropIndicator.Below ? targetIndex + 1 : targetIndex;
            }

            if (!TreeOps.MoveToGroupIndex(_config.Root, source, finalIndex))
            {
                return;
            }
        }

        SaveAndRebuildIndex();
        RefreshItems();
        SelectNode(source);
    }

    // ---------------------------------------------------------------------------------------------
    // Phase 5: config reload / recovery (spec §4, revised §10 items 2-5). Ctrl+R, Ctrl+Shift+R, and the
    // tray's "Reload config"/"Restore from backup"/"Keep my current version" items all funnel through here.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Ctrl+R / tray "Reload config", and the config file watcher's own external-change signal.</summary>
    public void RequestReload()
    {
        if (CurrentPage is PanelPage.Editor or PanelPage.TypePicker or PanelPage.IconPicker)
        {
            _reloadPending = true;
            ErrorMessage = "config.json changed — will reload once you finish this.";
            return;
        }

        ApplyExternalReload();
    }

    /// <summary>Spec §10 item 5: a save/apply attempted while a reload is pending is refused - never silently overwriting the external change - and the reload is applied right afterwards instead.</summary>
    private bool RefuseSaveIfReloadPending()
    {
        if (!_reloadPending)
        {
            return false;
        }

        _reloadPending = false;
        ApplyExternalReload();
        ErrorMessage = "config.json changed on disk — your edit was not saved; reopen and redo it.";
        return true;
    }

    private void ApplyExternalReload()
    {
        var result = _configService.Reload();
        if (result.Success)
        {
            OnConfigReloadedSuccess();
            return;
        }

        // Notepad-style truncate-then-write: the file may have been mid-write when we first read it.
        // Re-read once more shortly before declaring it genuinely corrupt (spec §10 item 3).
        _reloadRetryTimer?.Stop();
        _reloadRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _reloadRetryTimer.Tick += (_, _) =>
        {
            _reloadRetryTimer!.Stop();
            var retry = _configService.Reload();
            if (retry.Success)
            {
                OnConfigReloadedSuccess();
                return;
            }

            SetCorruptErrorMessage("config.json has errors — kept the previous version.");
            ReloadFailed?.Invoke(retry.Error ?? "config.json has errors.");
        };
        _reloadRetryTimer.Start();
    }

    /// <summary>Ctrl+Shift+R / the tray's "Restore from backup"/"Keep my current version" item - whichever <see cref="RecoveryMenuText"/> currently says applies.</summary>
    public void RecoverFromCorruption()
    {
        if (!CanRecoverFromCorruption)
        {
            return;
        }

        var result = _configService.HasEverLoadedSuccessfully
            ? _configService.KeepCurrentVersion()
            : _configService.RestoreFromBackup();

        if (result.Success)
        {
            OnConfigReloadedSuccess();
        }
        else
        {
            ErrorMessage = result.Error ?? "Could not recover config.";
        }
    }

    /// <summary>Called by MainWindow.HideLauncher() so a reload deferred by a still-open editor is applied once the panel is hidden, even without an explicit Cancel/Save (spec §10 item 5).</summary>
    public void FlushPendingReloadIfAny()
    {
        if (!_reloadPending)
        {
            return;
        }

        _reloadPending = false;
        ApplyExternalReload();
    }

    /// <summary>
    /// Applies a just-reloaded/-recovered config.json onto the already-live <see cref="_config"/> instance
    /// (spec §10 item 2): rebuilds the folder stack by id (stays in the same folder if it still exists,
    /// else falls back to root), rebuilds the search index, invalidates every cached icon (the whole tree
    /// is new), clears cut/pending-delete state, re-applies maxVisibleItems/hint-bar (both bindable
    /// properties MainWindow also reacts to directly), and returns to List if a delete confirmation was
    /// mid-flight against a now-gone node.
    /// </summary>
    private void OnConfigReloadedSuccess()
    {
        ErrorMessage = "";

        var keptIds = _folderStack.Select(f => f.Id).ToList();
        _folderStack.Clear();
        _folderStack.Add(_config.Root);
        for (var i = 1; i < keptIds.Count; i++)
        {
            var found = FindChildFolderById(_folderStack[^1], keptIds[i]);
            if (found is null)
            {
                break;
            }

            _folderStack.Add(found);
        }

        _searchService.Rebuild(_config);
        _iconService.Invalidate();
        _pruneUsage();
        CutNode = null;
        _pendingDeleteNode = null;
        _pendingImportConfig = null;

        if (CurrentPage is PanelPage.ConfirmDelete or PanelPage.ConfirmImport)
        {
            CurrentPage = PanelPage.List;
        }

        ApplySettings();

        UpdateBreadcrumb();
        RefreshItems();
    }

    private static FolderNode? FindChildFolderById(FolderNode parent, string id) =>
        parent.Children.OfType<FolderNode>().FirstOrDefault(f => f.Id == id);

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
