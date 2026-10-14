using System.Diagnostics;
using System.IO;
using System.Text;
using YourLauncher.App.Services;
using YourLauncher.Core.Plugins;

namespace YourLauncher.App.Plugins;

/// <summary>
/// Runs one plugin query as a short-lived child process (the "script filter" model): start, write one JSON
/// line to stdin, close it, read stdout to the end, parse. Plugins are separate programs so a crash, hang or
/// memory leak in one can never take the launcher down; everything that can go wrong here ends in "zero items".
/// <para>
/// The process tree (not just the root: <c>pwsh -File x.ps1</c> or a <c>cmd /c</c> wrapper spawns children) is
/// killed on timeout, on cancellation (the user typed another key) and when the plugin prints more than
/// <see cref="PluginProtocol.MaxOutputBytes"/>. Runs on a pool thread - the caller (MainViewModel's async
/// provider loop) already is on one - and never touches the UI.
/// </para>
/// </summary>
internal static class PluginRunner
{
    /// <summary>stderr is only a debugging aid; a plugin that spams it must not grow our memory.</summary>
    private const int MaxStderrChars = 8 * 1024;

    // No BOM: Process would otherwise prepend EF BB BF to the request line and break naive JSON parsers.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<IReadOnlyList<PluginItem>> RunAsync(LoadedPlugin plugin, string query, CancellationToken cancellationToken)
    {
        var manifest = plugin.Manifest;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(manifest.TimeoutMs);
        var token = linked.Token;

        using var process = new Process { StartInfo = BuildStartInfo(plugin, query) };
        try
        {
            if (!process.Start())
            {
                return [];
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Debug.WriteLine($"[YourLauncher] Plugin '{manifest.Name}' could not start: {ex.Message}");
            return [];
        }

        Task<string>? stderrTask = null;
        try
        {
            var stdoutTask = ReadStdoutAsync(process.StandardOutput, token);
            stderrTask = ReadStderrAsync(process.StandardError, token);

            await WriteRequestAsync(process, query).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            if (stdout is null)
            {
                Debug.WriteLine($"[YourLauncher] Plugin '{manifest.Name}' printed more than {PluginProtocol.MaxOutputBytes} bytes; ignored.");
                return []; // The finally block kills it - it may still be writing.
            }

            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (stderr.Length > 0)
            {
                Debug.WriteLine($"[YourLauncher] Plugin '{manifest.Name}' stderr: {stderr}");
            }

            if (process.ExitCode != 0)
            {
                Debug.WriteLine($"[YourLauncher] Plugin '{manifest.Name}' exited with code {process.ExitCode}.");
                return [];
            }

            return PluginOutputParser.Parse(stdout);
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine($"[YourLauncher] Plugin '{manifest.Name}' timed out after {manifest.TimeoutMs} ms.");
                return [];
            }

            throw; // The user typed again: let the caller drop the (now stale) batch.
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"[YourLauncher] Plugin '{manifest.Name}' failed: {ex.Message}");
            return [];
        }
        finally
        {
            KillTree(process);
            if (stderrTask is not null)
            {
                // Observe it on every exit path (early return, timeout, cancel) so a faulted/cancelled read never
                // surfaces as an UnobservedTaskException.
                try
                {
                    await stderrTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    /// <summary>The icon/exe/command paths a plugin gives may be relative; relative means "next to plugin.json".</summary>
    public static string ResolveAgainst(string pluginDirectory, string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(pluginDirectory, path));

    private static ProcessStartInfo BuildStartInfo(LoadedPlugin plugin, string query)
    {
        var manifest = plugin.Manifest;

        // The plugin runs outside the Store package, where a package-redirected %APPDATA% folder is only
        // visible at its physical LocalCache path - so everything handed to the child uses that path.
        var directory = PackageContext.ToPhysicalPath(plugin.Directory, PluginDiscovery.ManifestFileName);

        // "search.ps1"-style bare names go through PATH like any command; "bin\tool.exe" or "./tool" is a file
        // shipped inside the plugin folder, so it is resolved against it (the working directory may differ).
        var command = manifest.Command.IndexOfAny(['\\', '/']) >= 0
            ? ResolveAgainst(directory, manifest.Command)
            : manifest.Command;

        var startInfo = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = manifest.WorkingDirectory is { } wd ? ResolveAgainst(directory, wd) : directory,
        };

        foreach (var arg in manifest.Args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment["YOURLAUNCHER_QUERY"] = query;
        startInfo.Environment["YOURLAUNCHER_PLUGIN_DIR"] = directory;
        return startInfo;
    }

    private static async Task WriteRequestAsync(Process process, string query)
    {
        try
        {
            await process.StandardInput.WriteLineAsync(PluginProtocol.BuildRequest(query)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The plugin exited (or ignores stdin and closed it) before we finished writing - it may well have
            // used the env var instead, so carry on and judge it by its output.
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Reads stdout to EOF; null when the plugin printed more than the cap (treated as broken).</summary>
    private static async Task<string?> ReadStdoutAsync(StreamReader reader, CancellationToken token)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        var bytes = 0L;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (read == 0)
            {
                return builder.ToString();
            }

            bytes += Encoding.UTF8.GetByteCount(buffer, 0, read);
            if (bytes > PluginProtocol.MaxOutputBytes)
            {
                return null;
            }

            builder.Append(buffer, 0, read);
        }
    }

    private static async Task<string> ReadStderrAsync(StreamReader reader, CancellationToken token)
    {
        var builder = new StringBuilder();
        var buffer = new char[2048];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (read == 0)
            {
                return builder.ToString().Trim();
            }

            // Keep draining after the cap: an undrained pipe would block the plugin on a full stderr buffer.
            if (builder.Length < MaxStderrChars)
            {
                builder.Append(buffer, 0, Math.Min(read, MaxStderrChars - builder.Length));
            }
        }
    }

    /// <summary>Best effort: the process may already be gone, or (rarely) refuse to die while we are not elevated.</summary>
    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or AggregateException)
        {
            Debug.WriteLine($"[YourLauncher] Could not kill plugin process: {ex.Message}");
        }
    }
}
