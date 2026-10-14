using System.Runtime.InteropServices;
using System.Windows;

namespace YourLauncher.App.Services;

/// <summary>
/// Clipboard access that survives a busy clipboard. <see cref="Clipboard.SetText(string)"/> throws a
/// COMException (CLIPBRD_E_CANT_OPEN) whenever another process - a clipboard manager, remote-desktop
/// sync, an Office app - has it open for a few milliseconds, which is common enough that a single attempt
/// would make the calculator's "copy" fail at random. Must be called on the UI (STA) thread.
/// </summary>
public static class ClipboardHelper
{
    private const int Attempts = 5;
    private const int DelayMs = 40;

    /// <summary>True when the text is on the clipboard; false after every attempt hit a busy clipboard.</summary>
    public static bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                // COMException derives from ExternalException; anything else is a real bug and should surface.
            }

            if (attempt < Attempts - 1)
            {
                Thread.Sleep(DelayMs);
            }
        }

        return false;
    }
}
