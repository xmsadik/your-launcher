using System.Globalization;
using System.Text;

namespace YourLauncher.Core.Diagnostics;

/// <summary>
/// Append-only <c>error.log</c> next to config.json for exceptions nothing else caught (the App's global
/// unhandled-exception handlers). Once the file reaches <see cref="MaxBytes"/> it is moved to
/// <c>error.old.log</c> (replacing any previous one), so the log never grows without bound.
/// <see cref="Write"/> never throws - a failing crash log must not become a second crash.
/// </summary>
public sealed class ErrorLog
{
    public const long MaxBytes = 512 * 1024;

    private readonly object _gate = new();

    public ErrorLog(string directory)
    {
        FilePath = Path.Combine(directory, "error.log");
        OldFilePath = Path.Combine(directory, "error.old.log");
    }

    public string FilePath { get; }

    public string OldFilePath { get; }

    /// <summary>Appends one entry (timestamp, source, full exception text); returns false if the file couldn't be written.</summary>
    public bool Write(string source, Exception exception, DateTime now)
    {
        var entry = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(" [").Append(source).AppendLine("]")
            .AppendLine(exception.ToString())
            .AppendLine()
            .ToString();

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var existing = new FileInfo(FilePath);
                if (existing.Exists && existing.Length >= MaxBytes)
                {
                    File.Move(FilePath, OldFilePath, overwrite: true);
                }

                File.AppendAllText(FilePath, entry, Encoding.UTF8);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
