using System.IO;
using YourLauncher.Core.Usage;

namespace YourLauncher.App.Services;

/// <summary>
/// App-level home for usage.json (spec §8.4): loads once at startup (a missing or corrupt file starts
/// empty via <see cref="UsageSerializer.Deserialize"/> - never crashes, never overwrites the bad file
/// until the next successful <see cref="Flush"/>), records a launch on every successful non-folder launch,
/// and saves atomically (temp file + move) debounced 2 seconds after the last change so a burst of
/// launches doesn't hit disk every time. All Core scoring/pruning logic lives in <see cref="UsageScorer"/>
/// (pure, unit tested); this class is only the file I/O + debounce wrapper around it.
/// </summary>
public sealed class UsageService : IDisposable
{
    private const int DebounceMs = 2000;

    private readonly string _path;
    private readonly object _gate = new();
    private readonly UsageData _data;
    private Timer? _timer;
    private bool _dirty;

    public UsageService(string configDirectory)
    {
        _path = Path.Combine(configDirectory, "usage.json");
        _data = Load();
    }

    private UsageData Load()
    {
        try
        {
            return File.Exists(_path) ? UsageSerializer.Deserialize(File.ReadAllText(_path)) : new UsageData();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UsageData();
        }
    }

    /// <summary>Records one launch of <paramref name="nodeId"/> and schedules a debounced save.</summary>
    public void RecordLaunch(string nodeId)
    {
        lock (_gate)
        {
            UsageScorer.Record(_data, nodeId, DateTime.UtcNow);
            _dirty = true;
        }

        ScheduleFlush();
    }

    /// <summary>The search tie-breaker <see cref="SearchService"/> is constructed with (spec §8.4).</summary>
    public double GetScore(string nodeId)
    {
        lock (_gate)
        {
            return UsageScorer.Score(_data, nodeId, DateTime.UtcNow);
        }
    }

    /// <summary>Drops entries for ids no longer in the tree (called once at startup, and after a delete/import).</summary>
    public void Prune(IReadOnlyCollection<string> existingIds)
    {
        lock (_gate)
        {
            UsageScorer.Prune(_data, existingIds);
            _dirty = true;
        }

        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        lock (_gate)
        {
            _timer ??= new Timer(_ => Flush());
            _timer.Change(DebounceMs, Timeout.Infinite);
        }
    }

    /// <summary>Writes usage.json now if there's anything unsaved. Called on the debounce timer and once more on app exit (spec §10 item 10: a Windows shutdown that skips OnExit may still lose up to the last 2s of usage - documented in README).</summary>
    public void Flush()
    {
        string json;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            json = UsageSerializer.Serialize(_data);
            _dirty = false;
        }

        try
        {
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                _dirty = true; // best-effort - retry on the next flush rather than silently losing the write.
            }
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        Flush();
    }
}
