using System.Windows.Input;

namespace YourLauncher.App.Services;

/// <summary>What a key press in a hotkey capture box means (see <see cref="HotkeyCapture.Interpret"/>).</summary>
public enum HotkeyCaptureAction
{
    /// <summary>Tab / Esc / Enter keep their normal meaning (focus move, cancel, save): the caller does nothing and the event bubbles on.</summary>
    PassThrough,

    /// <summary>Backspace: each box decides (settings resets to the saved hotkey, the item editor clears it).</summary>
    Backspace,

    /// <summary>Handled but nothing to record yet (a modifier alone, a key without a modifier, Win).</summary>
    Ignore,

    /// <summary>A complete combination was typed; its text is in the <c>text</c> out value.</summary>
    Captured,
}

/// <summary>
/// The key-press-to-hotkey-text rules shared by the Settings page's hotkey box and the item editor's
/// hotkey box, so both capture exactly the same way and produce text <see cref="HotkeyService.TryParse"/> reads back.
/// </summary>
public static class HotkeyCapture
{
    /// <summary>
    /// Spec §10 item 1: Tab/Esc/Enter are not capturable and Backspace is left to the caller; everything else
    /// needs at least one modifier (Ctrl/Alt/Shift - Win is never offered, Windows reserves it) plus one
    /// non-modifier key. Marks <paramref name="e"/> handled for every action except
    /// <see cref="HotkeyCaptureAction.PassThrough"/>; for <see cref="HotkeyCaptureAction.Backspace"/> the
    /// caller does it too, so it is handled here as well.
    /// </summary>
    public static HotkeyCaptureAction Interpret(KeyEventArgs e, out string text)
    {
        text = "";

        if (e.Key is Key.Tab or Key.Escape or Key.Enter)
        {
            return HotkeyCaptureAction.PassThrough;
        }

        e.Handled = true;

        if (e.Key == Key.Back)
        {
            return HotkeyCaptureAction.Backspace;
        }

        // Alt-held keys arrive as "system keys" (e.Key == Key.System, real key in e.SystemKey) - the same WPF
        // quirk EditorView's Alt+A handling works around.
        var effectiveKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(effectiveKey))
        {
            return HotkeyCaptureAction.Ignore;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None || modifiers.HasFlag(ModifierKeys.Windows))
        {
            return HotkeyCaptureAction.Ignore;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(effectiveKey.ToString()); // WPF Key enum name - HotkeyService.TryParse uses Enum.TryParse<Key>.
        text = string.Join("+", parts);
        return HotkeyCaptureAction.Captured;
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin
        or Key.System or Key.None;
}
