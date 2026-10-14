using System.Text;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Launch;

/// <summary>
/// Builds a <see cref="LaunchPlan"/> per node type, per spec §8.2. Environment variables in target,
/// workingDirectory and command are expanded here, at plan-build time (spec §8.3). A parameterized item's
/// <c>{query}</c> placeholder (<see cref="QueryTemplate"/>) is filled in AFTER that expansion, so the typed
/// text is never environment-expanded; a null query means "nothing typed" and substitutes an empty value.
/// </summary>
public static class CommandLineBuilder
{
    public static LaunchPlan BuildForApp(AppNode node, string? query = null)
    {
        var target = EnvExpander.Expand(node.Target) ?? "";
        var workingDirectory = EnvExpander.Expand(node.WorkingDirectory) ?? TryGetDirectoryName(target);
        var arguments = EnvExpander.Expand(node.Arguments);
        if (arguments is not null)
        {
            arguments = QueryTemplate.Apply(arguments, query ?? "", QueryTemplate.QuoteArgument);
        }

        return new LaunchPlan
        {
            FileName = target,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            Verb = node.RunAsAdmin ? "runas" : null,
        };
    }

    /// <summary>
    /// Opens a path exactly as given, with NO environment expansion. For paths found on disk (file search):
    /// a real file named like "a%TEMP%b" must not be rewritten the way a hand-typed path item is.
    /// </summary>
    public static LaunchPlan BuildForLiteralPath(string path) => new()
    {
        FileName = path,
        UseShellExecute = true,
    };

    public static LaunchPlan BuildForPath(PathNode node) => new()
    {
        FileName = EnvExpander.Expand(node.Target) ?? "",
        UseShellExecute = true,
    };

    public static LaunchPlan BuildForUrl(UrlNode node, string? query = null) => new()
    {
        FileName = QueryTemplate.ForUrl(EnvExpander.Expand(node.Target) ?? "", query ?? ""),
        UseShellExecute = true,
    };

    /// <summary>
    /// Resolves node.shell ?? settings.defaultShell, falling back from "pwsh" to "powershell" when pwsh
    /// isn't found on PATH. <paramref name="isOnPath"/> is injectable so tests don't depend on the
    /// actual machine's PATH.
    /// </summary>
    public static ShellKind ResolveShell(CommandNode node, Settings settings, Func<string, bool>? isOnPath = null)
    {
        var shell = node.Shell ?? settings.DefaultShell;
        if (shell == ShellKind.Pwsh)
        {
            var probe = isOnPath ?? IsExecutableOnPath;
            if (!probe("pwsh"))
            {
                return ShellKind.Powershell;
            }
        }

        return shell;
    }

    public static LaunchPlan BuildForCommand(CommandNode node, Settings settings, Func<string, bool>? isOnPath = null, string? query = null)
    {
        var shell = ResolveShell(node, settings, isOnPath);
        var command = EnvExpander.Expand(node.Command) ?? "";
        command = QueryTemplate.Apply(command, query ?? "", shell == ShellKind.Cmd ? QueryTemplate.ForCmd : QueryTemplate.ForPowerShell);
        var workingDirectory = EnvExpander.Expand(node.WorkingDirectory);
        var hidden = node.Window == WindowMode.Hidden;
        var keepOpenVisible = !hidden && node.KeepOpen;

        return shell switch
        {
            ShellKind.Pwsh => BuildPowerShellPlan("pwsh", command, hidden, keepOpenVisible, workingDirectory, node.RunAsAdmin),
            ShellKind.Powershell => BuildPowerShellPlan("powershell", command, hidden, keepOpenVisible, workingDirectory, node.RunAsAdmin),
            ShellKind.Cmd => BuildCmdPlan(command, hidden, keepOpenVisible, workingDirectory, node.RunAsAdmin),
            _ => throw new ArgumentOutOfRangeException(nameof(node), shell, "Unknown shell kind."),
        };
    }

    private static LaunchPlan BuildPowerShellPlan(string exeName, string command, bool hidden, bool keepOpen, string? workingDirectory, bool runAsAdmin)
    {
        // -EncodedCommand takes base64(UTF-16LE) of the script text instead of a quoted -Command string.
        // This is bulletproof against quoting hazards (embedded ", &&, %, $, newlines) that would
        // otherwise require fragile re-escaping of arbitrary user-entered command text.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        var args = new List<string> { "-NoLogo" };
        if (hidden)
        {
            // Hidden commands never stay open (there's no window to keep) and must not prompt for input.
            args.Add("-NonInteractive");
            args.Add("-WindowStyle");
            args.Add("Hidden");
        }
        else if (keepOpen)
        {
            args.Add("-NoExit");
        }

        args.Add("-EncodedCommand");
        args.Add(encoded);

        return new LaunchPlan
        {
            FileName = exeName,
            ArgumentList = args,
            WorkingDirectory = workingDirectory,
            // runas requires ShellExecute; CreateNoWindow only takes effect without ShellExecute, so a
            // hidden + admin command relies solely on -WindowStyle Hidden above.
            UseShellExecute = runAsAdmin,
            CreateNoWindow = hidden && !runAsAdmin,
            Verb = runAsAdmin ? "runas" : null,
            WindowStyle = hidden ? LaunchWindowStyle.Hidden : LaunchWindowStyle.Normal,
        };
    }

    private static LaunchPlan BuildCmdPlan(string command, bool hidden, bool keepOpen, string? workingDirectory, bool runAsAdmin)
    {
        var flag = (!hidden && keepOpen) ? "/k" : "/c";

        // "/s" plus a single outer quote pair makes cmd strip only that outer pair and pass everything
        // between them through verbatim — including any inner quotes in the user's command text. Without
        // /s, cmd's normal quote-stripping heuristic can mangle commands that themselves contain quotes.
        var arguments = $"/s {flag} \"{command}\"";

        return new LaunchPlan
        {
            FileName = "cmd.exe",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = runAsAdmin,
            CreateNoWindow = hidden && !runAsAdmin,
            Verb = runAsAdmin ? "runas" : null,
            WindowStyle = hidden ? LaunchWindowStyle.Hidden : LaunchWindowStyle.Normal,
        };
    }

    private static string? TryGetDirectoryName(string target)
    {
        try
        {
            var dir = Path.GetDirectoryName(target);
            return string.IsNullOrEmpty(dir) ? null : dir;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsExecutableOnPath(string exeName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".exe;.cmd;.bat").Split(';');

        foreach (var dir in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            foreach (var ext in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(dir, exeName + ext);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
