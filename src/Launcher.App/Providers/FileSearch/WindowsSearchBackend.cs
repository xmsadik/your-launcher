using System.Data.OleDb;
using System.Diagnostics;
using YourLauncher.Core.Providers;

namespace YourLauncher.App.Providers.FileSearch;

/// <summary>
/// File search through the Windows Search index (<c>SystemIndex</c> via the "Search.CollatorDSO" OLE DB
/// provider that ships with Windows). Needs no capability, so it also works in the MSIX build; it only sees
/// what the user has indexed. The statement comes from <see cref="FileSearchText.BuildWindowsSearchSql"/>.
/// </summary>
public sealed class WindowsSearchBackend : IFileSearchBackend
{
    private const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";

    private bool _failureLogged;

    public string Name => "Windows Search";

    /// <summary>Always "available": whether the service is running is only known by trying, and a failed try is just an empty result.</summary>
    public bool IsAvailable() => true;

    /// <summary>
    /// The statement runs on a pool thread and may keep running for a while after a cancel (Windows Search
    /// does not always abort a scan at once), so cancellation is two-fold: the command is cancelled, and the
    /// caller stops waiting immediately. <see cref="Search"/> never throws, so the abandoned task cannot
    /// surface as an unobserved exception.
    /// </summary>
    public Task<IReadOnlyList<string>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
        Task.Run(() => Search(query, maxResults, cancellationToken)).WaitAsync(cancellationToken);

    private IReadOnlyList<string> Search(string query, int maxResults, CancellationToken ct)
    {
        var sql = FileSearchText.BuildWindowsSearchSql(query, maxResults);
        if (sql is null)
        {
            return [];
        }

        try
        {
            using var connection = new OleDbConnection(ConnectionString);
            connection.Open();
            using var command = new OleDbCommand(sql, connection);

            // Cancelling the token aborts the running statement instead of letting it finish unobserved.
            using var registration = ct.Register(() =>
            {
                try
                {
                    command.Cancel();
                }
                catch (Exception)
                {
                    // The command may already be done or disposed.
                }
            });

            var paths = new List<string>(maxResults);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetValue(0) is string path && path.Length > 0)
                {
                    paths.Add(path);
                }
            }

            _failureLogged = false; // recovered: a later outage is worth a new log line.
            return paths;
        }
        catch (Exception ex)
        {
            // Service stopped/disabled, provider not registered, a cancelled statement, ... : no results, and
            // only one line of diagnostics so typing never spams the output.
            if (ct.IsCancellationRequested)
            {
                return []; // the aborted statement's own error, not a real failure.
            }

            if (!_failureLogged)
            {
                _failureLogged = true;
                Debug.WriteLine($"[YourLauncher] Windows Search unavailable: {ex.Message}");
            }

            return [];
        }
    }
}
