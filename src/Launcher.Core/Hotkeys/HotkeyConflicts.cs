using YourLauncher.Core.Model;

namespace YourLauncher.Core.Hotkeys;

/// <summary>A node together with its (raw, as written in config.json) hotkey text.</summary>
public sealed record NodeHotkey(Node Node, string Hotkey);

/// <summary>
/// A hotkey that cannot be registered because something earlier already owns it.
/// <see cref="Other"/> is the earlier node, or null when the main hotkey (settings.hotkey) owns it.
/// </summary>
public sealed record HotkeyConflict(Node Node, string Hotkey, Node? Other);

/// <summary>Who owns a hotkey: <see cref="Node"/> is the node, or null for the main (panel) hotkey.</summary>
public sealed record HotkeyOwner(Node? Node);

/// <summary>
/// Pure hotkey bookkeeping for per-item hotkeys: syntax normalization (so "ctrl+alt+j", "Alt+Control+J" and
/// "Ctrl+Alt+J" count as the same combination) and conflict detection. Registration with Windows lives in
/// the App layer; this part is testable without a window.
/// </summary>
public static class HotkeyConflicts
{
    /// <summary>
    /// Canonical text ("Ctrl+Alt+Shift+Win+KEY") of a hotkey, or false when it is not a modifier list plus
    /// exactly one key. Modifiers may come in any order and case; "Control" = "Ctrl", "Windows" = "Win".
    /// Whether the key name exists is for the App layer to decide (it owns the WPF key table).
    /// </summary>
    public static bool TryNormalize(string? text, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        bool ctrl = false, alt = false, shift = false, win = false;
        string? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "win":
                case "windows":
                    win = true;
                    break;
                default:
                    if (key is not null)
                    {
                        return false;
                    }

                    key = part.ToUpperInvariant();
                    break;
            }
        }

        if (key is null)
        {
            return false;
        }

        normalized = string.Concat(ctrl ? "Ctrl+" : "", alt ? "Alt+" : "", shift ? "Shift+" : "", win ? "Win+" : "", key);
        return true;
    }

    /// <summary>
    /// Every node with a hotkey, in tree order. Folders and separators are skipped: they cannot be launched,
    /// so a hand-edited hotkey on one is ignored rather than registered.
    /// </summary>
    public static IReadOnlyList<NodeHotkey> Collect(FolderNode root)
    {
        var result = new List<NodeHotkey>();
        CollectInto(root, result);
        return result;
    }

    private static void CollectInto(FolderNode folder, List<NodeHotkey> result)
    {
        foreach (var child in folder.Children)
        {
            switch (child)
            {
                case FolderNode sub:
                    CollectInto(sub, result);
                    break;
                case SeparatorNode:
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(child.Hotkey))
                    {
                        result.Add(new NodeHotkey(child, child.Hotkey));
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// First-come-first-served: the main hotkey owns its combination, then items in the given order. Every
    /// later item that repeats a taken combination is reported once. Hotkeys that are not valid syntax are
    /// not conflicts (the App layer reports them as invalid when it tries to register them).
    /// </summary>
    public static IReadOnlyList<HotkeyConflict> Find(string mainHotkey, IEnumerable<NodeHotkey> items)
    {
        var owners = new Dictionary<string, Node?>(StringComparer.Ordinal);
        if (TryNormalize(mainHotkey, out var main))
        {
            owners[main] = null;
        }

        var conflicts = new List<HotkeyConflict>();
        foreach (var item in items)
        {
            if (!TryNormalize(item.Hotkey, out var key))
            {
                continue;
            }

            if (owners.TryGetValue(key, out var owner))
            {
                conflicts.Add(new HotkeyConflict(item.Node, item.Hotkey, owner));
            }
            else
            {
                owners[key] = item.Node;
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Editor-side check: who already owns <paramref name="candidate"/> - the main hotkey or a hotkey in
    /// <paramref name="others"/> (the tree's other nodes, not the node being edited)? Null when it is free.
    /// </summary>
    public static HotkeyOwner? FindOwner(string candidate, string mainHotkey, IEnumerable<NodeHotkey> others)
    {
        if (!TryNormalize(candidate, out var key))
        {
            return null;
        }

        if (TryNormalize(mainHotkey, out var main) && main == key)
        {
            return new HotkeyOwner(null);
        }

        foreach (var other in others)
        {
            if (TryNormalize(other.Hotkey, out var otherKey) && otherKey == key)
            {
                return new HotkeyOwner(other.Node);
            }
        }

        return null;
    }
}
