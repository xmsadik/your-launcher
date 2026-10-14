using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using YourLauncher.App.Interop;

namespace YourLauncher.App.Services;

/// <summary>One per-item hotkey to register: the node it launches and the hotkey text from config.json.</summary>
public readonly record struct ItemHotkeyRequest(string NodeId, string Hotkey);

/// <summary>Why a per-item hotkey could not be registered.</summary>
public enum ItemHotkeyFailureReason
{
    /// <summary>Not a recognizable "Ctrl+Alt+Key" combination.</summary>
    Invalid,

    /// <summary>Windows refused it - another program (or this app) already owns the combination.</summary>
    InUse,
}

public readonly record struct ItemHotkeyFailure(string NodeId, string Hotkey, ItemHotkeyFailureReason Reason);

/// <summary>
/// Registers global hotkeys (parsed from strings like "Alt+Space") against a window's HWND via
/// RegisterHotKey + one WM_HOTKEY hook on that window's HwndSource: the main hotkey (id
/// <see cref="MainHotkeyId"/>, toggles the panel) plus any number of per-item hotkeys (ids from
/// <see cref="FirstItemHotkeyId"/> up, one per node, each launching that node). WM_HOTKEY is dispatched by id.
/// All registrations use MOD_NOREPEAT so holding the keys down fires once.
/// </summary>
public sealed class HotkeyService
{
    // Arbitrary ids, only need to be unique within this process.
    public const int MainHotkeyId = 0xA000;
    public const int FirstItemHotkeyId = 0xA001;

    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _mainRegistered;

    /// <summary>Per-item registrations: node id -> (Windows hotkey id, modifiers, virtual key).</summary>
    private readonly Dictionary<string, (int Id, uint Modifiers, uint VirtualKey)> _items = new(StringComparer.Ordinal);

    /// <summary>Reverse lookup used by the WM_HOTKEY dispatch.</summary>
    private readonly Dictionary<int, string> _nodeByHotkeyId = new();

    public event Action? HotkeyPressed;

    /// <summary>Raised with the node id when a per-item hotkey fires.</summary>
    public event Action<string>? ItemHotkeyPressed;

    /// <summary>Window must already have an HWND (e.g. via WindowInteropHelper.EnsureHandle()). Returns false on failure.</summary>
    public bool TryRegister(Window window, string hotkeyText)
    {
        if (!TryParse(hotkeyText, out var modifiers, out var vk) || !Attach(window))
        {
            return false;
        }

        UnregisterMain();
        _mainRegistered = Win32.RegisterHotKey(_hwnd, MainHotkeyId, modifiers | Win32.MOD_NOREPEAT, vk);
        return _mainRegistered;
    }

    /// <summary>Unregisters the main hotkey only; per-item hotkeys stay.</summary>
    public void Unregister(Window window)
    {
        UnregisterMain();
    }

    /// <summary>Unregisters the main hotkey and every per-item hotkey (a hotkey capture box has focus, or the app is exiting).</summary>
    public void UnregisterAll(Window window)
    {
        UnregisterMain();
        foreach (var registration in _items.Values)
        {
            Win32.UnregisterHotKey(_hwnd, registration.Id);
        }

        _items.Clear();
        _nodeByHotkeyId.Clear();
    }

    /// <summary>
    /// Makes the registered per-item hotkeys exactly <paramref name="wanted"/>: ones that vanished or changed
    /// are unregistered, new ones registered, unchanged ones are left alone (no gap in which a press is lost).
    /// Returns the ones that could not be registered; the caller owns conflict handling between items.
    /// </summary>
    public IReadOnlyList<ItemHotkeyFailure> SyncItems(Window window, IReadOnlyList<ItemHotkeyRequest> wanted)
    {
        var failures = new List<ItemHotkeyFailure>();
        if (!Attach(window))
        {
            return failures;
        }

        var parsed = new Dictionary<string, (uint Modifiers, uint VirtualKey)>(StringComparer.Ordinal);
        foreach (var request in wanted)
        {
            // A bare key ("J") would hijack that key in every program, so an item hotkey needs a modifier.
            if (TryParse(request.Hotkey, out var modifiers, out var vk) && modifiers != 0)
            {
                parsed[request.NodeId] = (modifiers, vk);
            }
            else
            {
                failures.Add(new ItemHotkeyFailure(request.NodeId, request.Hotkey, ItemHotkeyFailureReason.Invalid));
            }
        }

        // Drop first, so a hotkey that moved from one node to another can be re-registered below.
        foreach (var nodeId in _items.Keys.ToList())
        {
            var registration = _items[nodeId];
            if (parsed.TryGetValue(nodeId, out var target) && target == (registration.Modifiers, registration.VirtualKey))
            {
                continue;
            }

            Win32.UnregisterHotKey(_hwnd, registration.Id);
            _nodeByHotkeyId.Remove(registration.Id);
            _items.Remove(nodeId);
        }

        foreach (var request in wanted)
        {
            if (!parsed.TryGetValue(request.NodeId, out var target) || _items.ContainsKey(request.NodeId))
            {
                continue;
            }

            var id = AllocateItemId();
            if (Win32.RegisterHotKey(_hwnd, id, target.Modifiers | Win32.MOD_NOREPEAT, target.VirtualKey))
            {
                _items[request.NodeId] = (id, target.Modifiers, target.VirtualKey);
                _nodeByHotkeyId[id] = request.NodeId;
            }
            else
            {
                failures.Add(new ItemHotkeyFailure(request.NodeId, request.Hotkey, ItemHotkeyFailureReason.InUse));
            }
        }

        return failures;
    }

    /// <summary>Hooks the window's message loop once; the hook stays for the process lifetime so main and item registrations share it.</summary>
    private bool Attach(Window window)
    {
        if (_source is not null)
        {
            return true;
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

        _hwnd = hwnd;
        _source.AddHook(WndProc);
        return true;
    }

    private void UnregisterMain()
    {
        if (_mainRegistered)
        {
            Win32.UnregisterHotKey(_hwnd, MainHotkeyId);
            _mainRegistered = false;
        }
    }

    private int AllocateItemId()
    {
        var id = FirstItemHotkeyId;
        while (_nodeByHotkeyId.ContainsKey(id))
        {
            id++;
        }

        return id;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY)
        {
            return IntPtr.Zero;
        }

        var id = wParam.ToInt32();
        if (id == MainHotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        else if (_nodeByHotkeyId.TryGetValue(id, out var nodeId))
        {
            ItemHotkeyPressed?.Invoke(nodeId);
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
