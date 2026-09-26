using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using YourLauncher.App.Interop;

namespace YourLauncher.App.Services;

/// <summary>
/// Registers a single global hotkey (parsed from strings like "Alt+Space") against a window's HWND via
/// RegisterHotKey + a WM_HOTKEY hook on that window's HwndSource.
/// </summary>
public sealed class HotkeyService
{
    // Arbitrary id, only needs to be unique within this process.
    private const int HotkeyId = 0xA000;

    private HwndSource? _source;
    private bool _registered;

    public event Action? HotkeyPressed;

    /// <summary>Window must already have an HWND (e.g. via WindowInteropHelper.EnsureHandle()). Returns false on failure.</summary>
    public bool TryRegister(Window window, string hotkeyText)
    {
        if (!TryParse(hotkeyText, out var modifiers, out var vk))
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        _source = HwndSource.FromHwnd(hwnd);
        if (_source is null)
        {
            return false;
        }

        _source.AddHook(WndProc);

        _registered = Win32.RegisterHotKey(hwnd, HotkeyId, modifiers | Win32.MOD_NOREPEAT, vk);
        if (!_registered)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        return _registered;
    }

    public void Unregister(Window window)
    {
        if (_registered)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                Win32.UnregisterHotKey(hwnd, HotkeyId);
            }

            _registered = false;
        }

        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>Parses "Ctrl+Alt+Shift+Win+&lt;Key&gt;" combinations (order-independent, case-insensitive).</summary>
    public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        string? keyPart = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= Win32.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= Win32.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= Win32.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= Win32.MOD_WIN;
                    break;
                default:
                    if (keyPart is not null)
                    {
                        return false; // more than one non-modifier token, not a valid hotkey spec
                    }

                    keyPart = part;
                    break;
            }
        }

        if (keyPart is null || !Enum.TryParse<Key>(keyPart, ignoreCase: true, out var key))
        {
            return false;
        }

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }
}
