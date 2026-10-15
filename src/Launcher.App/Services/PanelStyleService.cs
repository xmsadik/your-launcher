using YourLauncher.Core.Model;

namespace YourLauncher.App.Services;

/// <summary>
/// Remembers the <see cref="PanelStyle"/> this process started with. A docked panel needs a transparent (layered)
/// window, which has to be chosen before the window handle exists, so <c>settings.panelStyle</c> is read once at
/// startup and a change saved later only takes effect after a restart (same idea as <see cref="LanguageService.Applied"/>).
/// </summary>
public static class PanelStyleService
{
    /// <summary>The style the main window was created with.</summary>
    public static PanelStyle Applied { get; private set; } = PanelStyle.Floating;

    public static void Apply(PanelStyle style) => Applied = style;
}
