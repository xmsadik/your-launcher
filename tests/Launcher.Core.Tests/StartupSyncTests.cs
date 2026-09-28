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
    public void Decide_EnabledAndSameCommand_None()
    {
        var current = StartupSync.Command(ExePath);
        Assert.Equal(StartupAction.None, StartupSync.Decide(true, current, ExePath));
    }

    [Fact]
    public void Decide_EnabledAndSameCommandDifferentCase_None()
    {
        var current = StartupSync.Command(ExePath.ToUpperInvariant());
        Assert.Equal(StartupAction.None, StartupSync.Decide(true, current, ExePath));
    }

    [Fact]
    public void Decide_EnabledAndOldValueWithoutSilentArg_Writes()
    {
        // Older builds wrote just the quoted path; it gets upgraded so sign-in starts stay silent.
        Assert.Equal(StartupAction.Write, StartupSync.Decide(true, "\"" + ExePath + "\"", ExePath));
    }

    [Fact]
    public void Decide_EnabledAndDifferentPath_Writes()
    {
        var current = StartupSync.Command(@"C:\Old Location\YourLauncher.exe");
        Assert.Equal(StartupAction.Write, StartupSync.Decide(true, current, ExePath));
    }

    [Fact]
    public void Decide_DisabledAndPresent_Deletes()
    {
        var current = StartupSync.Command(ExePath);
        Assert.Equal(StartupAction.Delete, StartupSync.Decide(false, current, ExePath));
    }

    [Fact]
    public void Decide_DisabledAndMissing_None()
    {
        Assert.Equal(StartupAction.None, StartupSync.Decide(false, null, ExePath));
    }

    [Fact]
    public void Command_QuotesPathAndAppendsSilentArg()
    {
        Assert.Equal("\"" + ExePath + "\" --silent", StartupSync.Command(ExePath));
    }
}
