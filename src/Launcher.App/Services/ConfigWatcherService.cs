using System.IO;
using System.Security.Cryptography;
using System.Windows.Threading;

namespace YourLauncher.App.Services;

/// <summary>
/// Watches config.json for external edits (spec §4, revised §10 item 3) and, once a change looks genuinely
/// external (not our own last write echoing back through the OS), tells the caller so it can reload -
/// this class never parses or applies the reload itself, since whether that's safe to do *right now*
/// depends on which panel page is showing (deferred while Editor/TypePicker/IconPicker are open, spec §10
/// item 5 - that decision belongs to <c>MainViewModel</c>, not here).
/// </summary>
public sealed class ConfigWatcherService : IDisposable
{
    private const int DebounceMs = 200;
    private const int MaxReadRetries = 5;
    private const int ReadRetryDelayMs = 100;

    private readonly ConfigService _configService;
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _debounceTimer;
    private readonly Dispatcher _dispatcher;

    /// <summary>Raised on the dispatcher thread once a debounced, hash-confirmed external change is seen.</summary>
    public event Action? ExternalChangeDetected;

    public ConfigWatcherService(ConfigService configService)
    {
        _configService = configService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        _debounceTimer.Tick += OnDebounceElapsed;

        _watcher = new FileSystemWatcher(configService.Directory)
        {
            Filter = "config.json",
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
        };
        _watcher.Changed += (_, _) => RestartDebounce();
        _watcher.Created += (_, _) => RestartDebounce();
        _watcher.Renamed += (_, _) => RestartDebounce(); // editors often save via write-temp-then-rename.
        _watcher.Error += (_, _) => RestartDebounce(); // e.g. an internal buffer overflow - just try again.
    }

    public void Start() => _watcher.EnableRaisingEvents = true;

    private void RestartDebounce()
    {
        _dispatcher.BeginInvoke(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    private async void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();

        var bytes = await ReadWithRetryAsync(_configService.ConfigPath).ConfigureAwait(true);
        if (bytes is null)
        {
            return; // still locked after every retry, or the file's gone - the next FSW event tries again.
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (string.Equals(hash, _configService.LastWrittenHash, StringComparison.OrdinalIgnoreCase))
        {
            return; // our own write, echoed back through the watcher - not an external change.
        }

        ExternalChangeDetected?.Invoke();
    }

    private static async Task<byte[]?> ReadWithRetryAsync(string path)
    {
        for (var attempt = 0; attempt < MaxReadRetries; attempt++)
        {
            try
            {
                return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null; // deleted out from under us - nothing to reload from.
            }
            catch (IOException)
            {
                await Task.Delay(ReadRetryDelayMs).ConfigureAwait(false);
            }
        }

        return null;
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounceTimer.Stop();
        _debounceTimer.Tick -= OnDebounceElapsed;
    }
}
