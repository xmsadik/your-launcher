using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using YourLauncher.App.Interop;

namespace YourLauncher.App.Services;

/// <summary>
/// Named-mutex + named-pipe single instance guard (spec §1, revised §10 item 12). Keyed by a hash of the
/// (lowercased) config directory so a test run with <c>YOURLAUNCHER_CONFIG_DIR</c> set never collides with
/// a real, already-running instance that uses the default directory.
///
/// <see cref="TryAcquire"/> must run before any config/window work: the first instance owns the mutex and
/// starts the pipe server; a second instance connects to that pipe, asks it to show itself, and the caller
/// shuts down before ever creating a window (spec: "exit with code 0 before creating any window").
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private bool _mutexOwned;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    /// <summary>Raised on a background thread when another instance (or the tray icon of this one) asks to show the panel - the caller must marshal to the UI thread.</summary>
    public event Action? ShowRequested;

    public SingleInstanceService(string configDirectory)
    {
        var suffix = ComputeSuffix(configDirectory);
        _mutexName = $"Local\\YourLauncher-{suffix}"; // "Local\" = per session (spec §10 item 12).
        _pipeName = $"YourLauncher-{suffix}";
    }

    private static string ComputeSuffix(string configDirectory)
    {
        var bytes = Encoding.UTF8.GetBytes(configDirectory.ToLowerInvariant());
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return hash[..12].ToLowerInvariant();
    }

    /// <summary>True if this process now owns the mutex (first instance). Must be the very first thing done in OnStartup.</summary>
    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(true, _mutexName, out var createdNew);
            _mutexOwned = createdNew;
            return createdNew;
        }
        catch (AbandonedMutexException)
        {
            // The previous owner terminated without releasing it - we still hold it now (spec §10 item 12).
            _mutexOwned = true;
            return true;
        }
    }

    /// <summary>First instance only: starts accepting "show" pipe messages on a background loop.</summary>
    public void StartListening()
    {
        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoopAsync(_cts.Token));
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var message = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (string.Equals(message, "show", StringComparison.OrdinalIgnoreCase))
                {
                    ShowRequested?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Swallow teardown races on Dispose (spec §10 item 12) - the loop below spins up a fresh
                // server for the next connection unless we're shutting down.
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    /// <summary>Second-instance path: ask the running instance to show itself. Returns false (caller just exits) if it couldn't connect within ~2s - e.g. a hung first instance (spec §1).</summary>
    public bool NotifyExistingInstanceToShow()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);

            // So the first instance's SetForegroundWindow (called once it receives "show") isn't blocked
            // by the foreground-lock timeout, since the request originates from a different process
            // (spec §10 item 1).
            Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);

            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine("show");
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        try
        {
            _serverTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (Exception ex) when (ex is AggregateException or ObjectDisposedException)
        {
            // best-effort wait during shutdown
        }

        _cts?.Dispose();

        if (_mutexOwned)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // not owned (e.g. TryAcquire never succeeded) - nothing to release.
            }
        }

        _mutex?.Dispose();
    }
}
