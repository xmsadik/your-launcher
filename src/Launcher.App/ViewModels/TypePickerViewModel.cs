using CommunityToolkit.Mvvm.ComponentModel;

namespace YourLauncher.App.ViewModels;

/// <summary>Node kind chosen in the type picker / created by the editor (Phase 3, spec §9 step 1, decision D6).</summary>
public enum NodeKind
{
    Folder,
    App,
    Path,
    Command,
    Url,
}

/// <summary>One row of the type picker: glyph (same Segoe Fluent icons as the list), display name, key hint letter.</summary>
public sealed record TypePickerOption(NodeKind Kind, string Glyph, string Name, string KeyHint);

/// <summary>
/// Drives the Ctrl+N type picker (spec §9 step 1 / D6): vertical list of the 5 node kinds. ↑/↓ wrap,
/// Enter confirms the current selection, a key hint letter (F/A/P/C/U) selects immediately. Owned by
/// MainViewModel while <see cref="PanelPage.TypePicker"/> is the current page.
/// </summary>
public sealed partial class TypePickerViewModel : ObservableObject
{
    public static readonly IReadOnlyList<TypePickerOption> Options = new[]
    {
        new TypePickerOption(NodeKind.Folder, "", "Folder", "F"),
        new TypePickerOption(NodeKind.App, "", "App", "A"),
        new TypePickerOption(NodeKind.Path, "", "File or folder", "P"),
        new TypePickerOption(NodeKind.Command, "", "Command", "C"),
        new TypePickerOption(NodeKind.Url, "", "URL", "U"),
    };

    [ObservableProperty]
    private int _selectedIndex;

    /// <summary>Raised when a type is chosen (Enter, or a key-hint letter).</summary>
    public event Action<NodeKind>? Selected;

    /// <summary>Raised on Esc.</summary>
    public event Action? Cancelled;

    public void MoveSelection(int delta)
    {
        var count = Options.Count;
        SelectedIndex = ((SelectedIndex + delta) % count + count) % count;
    }

    public void ConfirmSelection() => Selected?.Invoke(Options[SelectedIndex].Kind);

    /// <summary>Selects immediately if <paramref name="key"/> matches a row's key hint letter (case-insensitive). Returns false otherwise.</summary>
    public bool TrySelectByKey(char key)
    {
        var upper = char.ToUpperInvariant(key).ToString();
        var match = Options.FirstOrDefault(o => o.KeyHint == upper);
        if (match is null)
        {
            return false;
        }

        Selected?.Invoke(match.Kind);
        return true;
    }

    public void Cancel() => Cancelled?.Invoke();
}
