using CommunityToolkit.Mvvm.ComponentModel;
using YourLauncher.App.Services;
using YourLauncher.Core.Config;
using YourLauncher.Core.Icons;
using YourLauncher.Core.Model;

namespace YourLauncher.App.ViewModels;

public enum EditorMode
{
    Add,
    Edit,
}

/// <summary>Command shell choice shown in the editor's ComboBox; <see cref="Default"/> maps to CommandNode.Shell == null (spec §4.3: "resolve from settings.defaultShell").</summary>
public enum ShellChoice
{
    Default,
    Pwsh,
    Powershell,
    Cmd,
}

/// <summary>
/// Backs the in-panel add/edit form (spec §9, Phase 3). Built from a node copy (for Add: a fresh blank
/// node of the chosen <see cref="NodeKind"/>; for Edit: field values read out of the node being edited) -
/// nothing is applied to the real tree until <see cref="RequestSave"/> succeeds and <see cref="Saved"/>
/// fires; MainViewModel does the actual TreeOps/save/refresh.
///
/// Edit mode mutates and returns the *same* node instance passed to <see cref="ForEdit"/> (same id, same
/// object identity) - that's what "modifies in place" means here; MainViewModel puts it back exactly
/// where the original object was, so position never changes either.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly Node? _originalNode;
    private readonly Func<string, string?> _fileDescriptionLookup;
    private string? _lastSuggestedName;

    public EditorMode Mode { get; }
    public NodeKind Kind { get; }

    /// <summary>
    /// Read-only icon preview for the Advanced section (spec §7.13 - a narrower affordance than
    /// launcher-spec.md §9 step 4's full inline icon editor, which Phase 3 already deferred to Ctrl+I;
    /// see tasks/todo.md's Phase 4 review for why). Shows a glyph/emoji, not the actual bitmap for a
    /// custom File/Exe icon - rendering that would need <see cref="Services.IconService"/> wired into the
    /// editor, which nothing else here needs.
    /// </summary>
    public string IconPreviewGlyph { get; }

    public string? IconPreviewEmoji { get; }

    public string IconDescription { get; }

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _keywordsText = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string? _nameError;

    /// <summary>Shared "Target" field for app/path/url (spec §4.3); meaning depends on <see cref="Kind"/>.</summary>
    [ObservableProperty]
    private string _target = "";

    // app-only
    [ObservableProperty]
    private string _arguments = "";

    [ObservableProperty]
    private bool _runAsAdmin;

    /// <summary>Every type except folder - shown outside Advanced so it's easy to spot on shutdown/restart-style items.</summary>
    [ObservableProperty]
    private bool _confirmLaunch;

    // app + command
    [ObservableProperty]
    private string _workingDirectory = "";

    // command-only
    [ObservableProperty]
    private string _command = "";

    [ObservableProperty]
    private ShellChoice _shell = ShellChoice.Default;

    [ObservableProperty]
    private WindowMode _window = WindowMode.Visible;

    [ObservableProperty]
    private bool _keepOpen = true;

    [ObservableProperty]
    private bool _isAdvancedExpanded;

    [ObservableProperty]
    private string _warningMessage = "";

    /// <summary>Raised by <see cref="RequestSave"/> once validation passes, with the node to persist.</summary>
    public event Action<Node>? Saved;

    /// <summary>Raised by <see cref="RequestCancel"/>.</summary>
    public event Action? Cancelled;

    private EditorViewModel(EditorMode mode, NodeKind kind, Node? originalNode, Func<string, string?> fileDescriptionLookup)
    {
        Mode = mode;
        Kind = kind;
        _originalNode = originalNode;
        _fileDescriptionLookup = fileDescriptionLookup;

        (IconPreviewGlyph, IconPreviewEmoji, IconDescription) = DescribeIcon(kind, originalNode?.Icon);

        if (originalNode is null)
        {
            return;
        }

        Name = originalNode.Name;
        KeywordsText = string.Join(", ", originalNode.Keywords);
        Description = originalNode.Description ?? "";
        ConfirmLaunch = originalNode.ConfirmLaunch;

        switch (originalNode)
        {
            case AppNode app:
                Target = app.Target;
                Arguments = app.Arguments ?? "";
                WorkingDirectory = app.WorkingDirectory ?? "";
                RunAsAdmin = app.RunAsAdmin;
                IsAdvancedExpanded = !string.IsNullOrEmpty(app.Arguments) || app.WorkingDirectory is not null || app.RunAsAdmin;
                break;

            case PathNode path:
                Target = path.Target;
                break;

            case CommandNode command:
                Command = command.Command;
                Shell = command.Shell switch
                {
                    ShellKind.Pwsh => ShellChoice.Pwsh,
                    ShellKind.Powershell => ShellChoice.Powershell,
                    ShellKind.Cmd => ShellChoice.Cmd,
                    _ => ShellChoice.Default,
                };
                WorkingDirectory = command.WorkingDirectory ?? "";
                Window = command.Window;
                KeepOpen = command.KeepOpen;
                RunAsAdmin = command.RunAsAdmin;
                IsAdvancedExpanded = command.WorkingDirectory is not null
                    || command.Window != WindowMode.Visible
                    || !command.KeepOpen
                    || command.RunAsAdmin;
                break;

            case UrlNode url:
                Target = url.Target;
                break;

            case FolderNode:
                break;
        }
    }

    public static EditorViewModel ForAdd(NodeKind kind, Func<string, string?> fileDescriptionLookup) =>
        new(EditorMode.Add, kind, null, fileDescriptionLookup);

    public static EditorViewModel ForEdit(Node node, Func<string, string?> fileDescriptionLookup) =>
        new(EditorMode.Edit, KindOf(node), node, fileDescriptionLookup);

    private static NodeKind KindOf(Node node) => node switch
    {
        FolderNode => NodeKind.Folder,
        AppNode => NodeKind.App,
        PathNode => NodeKind.Path,
        CommandNode => NodeKind.Command,
        UrlNode => NodeKind.Url,
        _ => throw new InvalidOperationException($"Unknown node type '{node.GetType()}'."),
    };

    private static string DefaultGlyphFor(NodeKind kind) => kind switch
    {
        NodeKind.Folder => IconGlyphs.Folder,
        NodeKind.App => IconGlyphs.App,
        NodeKind.Path => IconGlyphs.Path,
        NodeKind.Command => IconGlyphs.Command,
        NodeKind.Url => IconGlyphs.Url,
        _ => IconGlyphs.Folder,
    };

    private static (string Glyph, string? Emoji, string Description) DescribeIcon(NodeKind kind, IconSpec? icon)
    {
        var defaultGlyph = DefaultGlyphFor(kind);
        return icon?.Kind switch
        {
            IconKind.Emoji when icon.Value.Length > 0 => (defaultGlyph, icon.Value, "Custom emoji"),
            IconKind.Glyph when IconGlyphs.IsValidCustomGlyph(icon.Value) => (icon.Value, null, "Custom glyph"),
            IconKind.File => (defaultGlyph, null, "Custom file icon"),
            IconKind.Exe => (defaultGlyph, null, "Custom exe icon"),
            _ => (defaultGlyph, null, "Automatic"),
        };
    }

    public bool IsFolder => Kind == NodeKind.Folder;
    public bool IsApp => Kind == NodeKind.App;
    public bool IsPath => Kind == NodeKind.Path;
    public bool IsCommand => Kind == NodeKind.Command;
    public bool IsUrl => Kind == NodeKind.Url;

    /// <summary>app/path show Target + Browse…; url shows Target with no browse button.</summary>
    public bool ShowTargetBrowse => IsApp || IsPath;

    public bool ShowAdvancedSection => IsApp || IsCommand;
    public bool ShowWorkingDirectory => IsApp || IsCommand;
    public bool ShowRunAsAdmin => IsApp || IsCommand;

    public bool ShowConfirmLaunch => !IsFolder;

    /// <summary>app/path focus Target first on open (so browse/paste then name auto-fills); everything else focuses Name.</summary>
    public bool FocusTargetFirst => IsApp || IsPath;

    /// <summary>Keep open is only meaningful when the command's window is visible (spec §4.3/§9).</summary>
    public bool KeepOpenEnabled => Window == WindowMode.Visible;

    partial void OnWindowChanged(WindowMode value) => OnPropertyChanged(nameof(KeepOpenEnabled));

    /// <summary>
    /// Called by the View when the Target field loses focus, or right after a Browse dialog sets it
    /// (spec §9 step 3): fills Name from <see cref="TargetNameHelper.SuggestName"/> if Name is still
    /// empty or still equals the last auto-suggestion (i.e. the user hasn't typed their own name yet).
    /// </summary>
    public void SuggestNameFromTarget()
    {
        if (Kind is not (NodeKind.App or NodeKind.Path or NodeKind.Url))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Target))
        {
            return;
        }

        if (!string.IsNullOrEmpty(Name) && Name != _lastSuggestedName)
        {
            return;
        }

        var suggestion = TargetNameHelper.SuggestName(Target, _fileDescriptionLookup);
        if (string.IsNullOrEmpty(suggestion))
        {
            return;
        }

        Name = suggestion;
        _lastSuggestedName = suggestion;
    }

    /// <summary>Enter (outside the command box) / Ctrl+Enter (inside it). Validates and, on success, raises <see cref="Saved"/>; on failure, sets <see cref="NameError"/> and returns without saving.</summary>
    public void RequestSave()
    {
        if (!TrySave(out var node))
        {
            return;
        }

        Saved?.Invoke(node);
    }

    public void RequestCancel() => Cancelled?.Invoke();

    private bool TrySave(out Node savedNode)
    {
        NameError = null;
        WarningMessage = "";

        var trimmedName = Name.Trim();
        if (trimmedName.Length == 0)
        {
            NameError = "Name is required";
            savedNode = null!;
            return false;
        }

        var keywords = KeywordsText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        var description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

        var node = Mode == EditorMode.Edit ? _originalNode! : CreateBlankNode();
        node.Name = trimmedName;
        node.Keywords = keywords;
        node.Description = description;
        node.ConfirmLaunch = !IsFolder && ConfirmLaunch;

        ApplyTypeSpecificFields(node);

        savedNode = node;
        return true;
    }

    private Node CreateBlankNode() => Kind switch
    {
        NodeKind.Folder => new FolderNode { Id = TreeOps.NewId() },
        NodeKind.App => new AppNode { Id = TreeOps.NewId() },
        NodeKind.Path => new PathNode { Id = TreeOps.NewId() },
        NodeKind.Command => new CommandNode { Id = TreeOps.NewId() },
        NodeKind.Url => new UrlNode { Id = TreeOps.NewId() },
        _ => throw new InvalidOperationException($"Unknown node kind '{Kind}'."),
    };

    private void ApplyTypeSpecificFields(Node node)
    {
        switch (node)
        {
            case AppNode app:
                app.Target = Target.Trim();
                app.Arguments = string.IsNullOrWhiteSpace(Arguments) ? null : Arguments;
                app.WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory.Trim();
                app.RunAsAdmin = RunAsAdmin;
                WarnIfTargetMissing(app.Target);
                break;

            case PathNode path:
                path.Target = Target.Trim();
                WarnIfTargetMissing(path.Target);
                break;

            case CommandNode command:
                command.Command = Command;
                command.Shell = Shell switch
                {
                    ShellChoice.Pwsh => ShellKind.Pwsh,
                    ShellChoice.Powershell => ShellKind.Powershell,
                    ShellChoice.Cmd => ShellKind.Cmd,
                    _ => null,
                };
                command.WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory.Trim();
                command.Window = Window;
                command.KeepOpen = KeepOpen;
                command.RunAsAdmin = RunAsAdmin;
                break;

            case UrlNode url:
                url.Target = PrependSchemeIfMissing(Target.Trim());
                break;

            case FolderNode:
                break;
        }
    }

    // Any absolute URI with a scheme (https:, mailto:, ms-settings:, …) is kept as-is. A single-letter
    // "scheme" is really a drive letter (C:\…), and "host:port" parses as a scheme too, so require a
    // multi-letter scheme and no leading digit after the colon.
    private static string PrependSchemeIfMissing(string target)
    {
        if (target.Length == 0)
        {
            return target;
        }

        var colon = target.IndexOf(':');
        var looksLikePort = colon >= 0 && colon + 1 < target.Length && char.IsAsciiDigit(target[colon + 1]);
        var hasScheme = !looksLikePort
            && Uri.TryCreate(target, UriKind.Absolute, out var uri)
            && uri.Scheme.Length > 1;
        return hasScheme ? target : "https://" + target;
    }

    /// <summary>
    /// Uses <see cref="TargetCheck.IsMissing"/> (spec §7.14) rather than a plain File/Directory.Exists
    /// check, so this warning and the row's missing-target badge (<see cref="ListItemViewModel"/>) always
    /// agree - including resolving a bare name like "notepad.exe" via %PATH% before deciding it's missing,
    /// and never flagging a UNC/URL target that's simply not worth a network round trip to check.
    /// </summary>
    private void WarnIfTargetMissing(string target)
    {
        if (TargetCheck.IsMissing(target))
        {
            WarningMessage = "Target not found — saved anyway.";
        }
    }
}
