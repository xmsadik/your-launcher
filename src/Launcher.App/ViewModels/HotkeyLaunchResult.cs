namespace YourLauncher.App.ViewModels;

/// <summary>Outcome of <see cref="MainViewModel.LaunchFromHotkey"/>.</summary>
public enum HotkeyLaunchResult
{
    /// <summary>The node is gone (or is a folder/separator) - nothing to do.</summary>
    NotFound,

    /// <summary>Started without showing the panel.</summary>
    Launched,

    /// <summary>The launch failed; the error text was returned for a tray balloon (the panel is hidden, so an error line would go unseen).</summary>
    Failed,

    /// <summary>The node needs the panel: a confirm bar ("Ask before launching") or the query to be typed ({query}). App shows the panel and calls <see cref="MainViewModel.ShowHotkeyNodeInPanel"/>.</summary>
    NeedsPanel,
}
