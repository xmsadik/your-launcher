using YourLauncher.Core.Diagnostics;

namespace YourLauncher.Core.Tests;

public sealed class ErrorLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "yl-errorlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Write_CreatesDirectoryAndAppendsTimestampSourceAndException()
    {
        var log = new ErrorLog(_dir);

        Assert.True(log.Write("Dispatcher", Thrown("boom"), new DateTime(2026, 9, 28, 10, 30, 5)));
        Assert.True(log.Write("Task", Thrown("second"), new DateTime(2026, 9, 28, 10, 31, 0)));

        var text = File.ReadAllText(log.FilePath);
        Assert.Contains("2026-09-28 10:30:05 [Dispatcher]", text);
        Assert.Contains("System.InvalidOperationException: boom", text);
        Assert.Contains(nameof(Thrown), text); // stack trace is included
        Assert.Contains("2026-09-28 10:31:00 [Task]", text);
        Assert.Contains("second", text);
    }

    [Fact]
    public void Write_FileAtLimit_RotatesToOldLogAndStartsFresh()
    {
        var log = new ErrorLog(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(log.FilePath, new string('x', (int)ErrorLog.MaxBytes));
        File.WriteAllText(log.OldFilePath, "previous old log");

        Assert.True(log.Write("Dispatcher", Thrown("after rotate"), DateTime.Now));

        Assert.Equal(ErrorLog.MaxBytes, new FileInfo(log.OldFilePath).Length);
        var fresh = File.ReadAllText(log.FilePath);
        Assert.Contains("after rotate", fresh);
        Assert.DoesNotContain("xxxx", fresh);
    }

    [Fact]
    public void Write_FileBelowLimit_DoesNotRotate()
    {
        var log = new ErrorLog(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(log.FilePath, "earlier entry\n");

        log.Write("Dispatcher", Thrown("later"), DateTime.Now);

        Assert.False(File.Exists(log.OldFilePath));
        var text = File.ReadAllText(log.FilePath);
        Assert.StartsWith("earlier entry", text);
        Assert.Contains("later", text);
    }

    [Fact]
    public void Write_FileLocked_ReturnsFalseInsteadOfThrowing()
    {
        var log = new ErrorLog(_dir);
        Directory.CreateDirectory(_dir);
        using var locked = new FileStream(log.FilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        Assert.False(log.Write("Dispatcher", Thrown("unwritable"), DateTime.Now));
    }
}
