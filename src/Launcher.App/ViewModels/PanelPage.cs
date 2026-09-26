namespace YourLauncher.App.ViewModels;

/// <summary>
/// Which content the panel currently shows (Phase 3). Only <see cref="List"/> keeps the search box
/// live and lets the search TextBox's key handler drive navigation; the other pages own their own
/// keyboard handling in their respective Views. <see cref="ConfirmDelete"/> keeps the list visible
/// underneath an inline confirm bar rather than swapping to a whole different view.
/// </summary>
public enum PanelPage
{
    List,
    TypePicker,
    Editor,
    ConfirmDelete,
}
