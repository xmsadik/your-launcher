using YourLauncher.Core.Startup;

namespace YourLauncher.Core.Tests;

public class StartupSyncTests
{
    private const string ExePath = @"C:\Program Files\Your Launcher\YourLauncher.exe";

    [Fact]
    public void Decide_EnabledAndMissing_Writes()
    {
        Assert.Equal(StartupAction.Write, StartupSync.Decide(true, null, ExePath));
    }

    [Fact]
    public void Decide_EnabledAndSamePathQuoted_None()
    {
        var current = StartupSync.Quote(ExePath);
        Assert.Equal(StartupAction.None, StartupSync.Decide(true, current, ExePath));
    }

    [Fact]
    public void Decide_EnabledAndSamePathDifferentCase_None()
    {
        var current = StartupSync.Quote(ExePath.ToUpperInvariant());
        Assert.Equal(StartupAction.None, StartupSync.Decide(true, current, ExePath));
    }

    [Fact]
    public void Decide_EnabledAndUnquotedSamePath_None()
    {
        // A hand-edited Run value without quotes should still compare equal.
        Assert.Equal(StartupAction.None, StartupSync.Decide(true, ExePath, ExePath));
    }

    [Fact]
    public void Decide_EnabledAndDifferentPath_Writes()
    {
        var current = StartupSync.Quote(@"C:\Old Location\YourLauncher.exe");
        Assert.Equal(StartupAction.Write, StartupSync.Decide(true, current, ExePath));
    }

    [Fact]
    public void Decide_DisabledAndPresent_Deletes()
    {
        var current = StartupSync.Quote(ExePath);
        Assert.Equal(StartupAction.Delete, StartupSync.Decide(false, current, ExePath));
    }

    [Fact]
    public void Decide_DisabledAndMissing_None()
    {
        Assert.Equal(StartupAction.None, StartupSync.Decide(false, null, ExePath));
    }

    [Fact]
    public void Quote_WrapsInDoubleQuotes()
    {
        Assert.Equal("\"" + ExePath + "\"", StartupSync.Quote(ExePath));
    }
}
