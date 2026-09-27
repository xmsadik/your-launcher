namespace YourLauncher.App.ViewModels;

/// <summary>
/// Which in-list drag indicator (spec §7/§10 item 8) a row currently shows while an internal drag hovers
/// over it - a property on <see cref="ListItemViewModel"/> rather than a WPF Adorner, per §10 item 8, so the
/// row template can react to it with a plain DataTrigger.
/// </summary>
public enum DropIndicator
{
    None,

    /// <summary>Insertion line above this row.</summary>
    Above,

    /// <summary>Insertion line below this row.</summary>
    Below,

    /// <summary>Hovering a folder row's middle 50% - dropping here moves the dragged node into this folder.</summary>
    Into,
}
