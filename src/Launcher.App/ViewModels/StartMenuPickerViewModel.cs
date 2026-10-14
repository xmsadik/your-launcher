using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using YourLauncher.App.Localization;
using YourLauncher.Core.Search;
using YourLauncher.Core.StartMenu;

namespace YourLauncher.App.ViewModels;

/// <summary>One row of the Start-menu picker: an app plus its check state.</summary>
public sealed partial class StartMenuPickItem : ObservableObject
{
    public StartMenuPickItem(StartMenuApp app, bool isChecked)
    {
        App = app;
        _isChecked = isChecked;
        NormalizedName = TextNormalizer.Normalize(app.Name);
    }

    public StartMenuApp App { get; }

    public string Name => App.Name;

    /// <summary>"Desktop app" / "Store app", shown right-aligned in the row.</summary>
    public string KindText => App.Kind == StartMenuAppKind.Packaged ? Strings.StartMenuKindStore : Strings.StartMenuKindDesktop;

    /// <summary>Segoe Fluent check box glyphs (checked / empty).</summary>
    public string CheckGlyph => IsChecked ? "" : "";

    internal string NormalizedName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckGlyph))]
    private bool _isChecked;
}

/// <summary>
/// Drives the "Import from Start menu" page (<see cref="PanelPage.StartMenuPicker"/>): a filterable checklist
/// of the apps <see cref="Services.StartMenuScanner"/> found. Type to filter, Up/Down move, Space toggles,
/// Ctrl+A checks everything shown (or unchecks it when it already is), Enter imports, Esc cancels. Check state
/// lives on the items, so it survives filtering; only checked items are imported, wherever the filter is.
/// On a re-import the apps imported before start out checked (<paramref name="alreadyImportedIds"/>).
/// </summary>
public sealed partial class StartMenuPickerViewModel : ObservableObject
{
    private readonly List<StartMenuPickItem> _all = [];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>Loading / empty / count line under the filter box.</summary>
    [ObservableProperty]
    private string _statusText = Strings.StartMenuLoading;

    public ObservableCollection<StartMenuPickItem> Items { get; } = [];

    /// <summary>True after the scan threw; Enter does nothing, Esc still leaves.</summary>
    public bool IsFailed { get; private set; }

    /// <summary>Raised on Enter with the checked apps (possibly none) and every app this scan saw; the import only removes earlier imports among the latter that are unchecked.</summary>
    public event Action<IReadOnlyList<StartMenuApp>, IReadOnlyList<StartMenuApp>>? Confirmed;

    /// <summary>Raised on Esc.</summary>
    public event Action? Cancelled;

    /// <summary>Called once the scan finished (UI thread).</summary>
    public void SetApps(IReadOnlyList<StartMenuApp> apps, HashSet<string> alreadyImportedIds)
    {
        _all.Clear();
        _all.AddRange(apps.Select(a => new StartMenuPickItem(a, alreadyImportedIds.Contains(StartMenuImport.ItemId(a.Key)))));
        foreach (var item in _all)
        {
            item.PropertyChanged += (_, _) => UpdateStatus();
        }

        IsLoading = false;
        ApplyFilter();
    }

    /// <summary>Enter was accepted and the nodes are being built; further Enter presses are ignored until the page closes.</summary>
    public void BeginImporting()
    {
        IsLoading = true;
        StatusText = Strings.StartMenuImporting;
    }

    /// <summary>The scan threw; the page stays open with the message so Esc still leaves it.</summary>
    public void SetFailed(string message)
    {
        IsLoading = false;
        IsFailed = true;
        StatusText = message;
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var tokens = TextNormalizer.Normalize(FilterText).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var previous = SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

        Items.Clear();
        foreach (var item in _all.Where(i => tokens.All(t => i.NormalizedName.Contains(t, StringComparison.Ordinal))))
        {
            Items.Add(item);
        }

        var keep = previous is null ? -1 : Items.IndexOf(previous);
        SelectedIndex = keep >= 0 ? keep : (Items.Count > 0 ? 0 : -1);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (IsLoading)
        {
            return;
        }

        var checkedCount = _all.Count(i => i.IsChecked);
        StatusText = _all.Count == 0
            ? Strings.StartMenuNoApps
            : Items.Count == 0
                ? Strings.StartMenuNoMatches
                : string.Format(Strings.StartMenuSelectedCount, checkedCount, _all.Count);
    }

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Clamp((SelectedIndex < 0 ? 0 : SelectedIndex) + delta, 0, Items.Count - 1);
    }

    public void SelectIndex(int index)
    {
        if (index >= 0 && index < Items.Count)
        {
            SelectedIndex = index;
        }
    }

    public void ToggleSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Items.Count)
        {
            Items[SelectedIndex].IsChecked = !Items[SelectedIndex].IsChecked;
        }
    }

    /// <summary>Ctrl+A: checks every item currently shown; when they all are checked already, unchecks them instead.</summary>
    public void ToggleAllShown()
    {
        var target = Items.Any(i => !i.IsChecked);
        foreach (var item in Items)
        {
            item.IsChecked = target;
        }
    }

    public void Confirm()
    {
        // A failed scan has no apps to check; confirming it must never reach the import.
        if (IsLoading || IsFailed)
        {
            return;
        }

        Confirmed?.Invoke(_all.Where(i => i.IsChecked).Select(i => i.App).ToList(), _all.Select(i => i.App).ToList());
    }

    public void Cancel() => Cancelled?.Invoke();
}
