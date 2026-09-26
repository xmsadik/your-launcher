using System.Text;
using YourLauncher.Core.Launch;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests;

public class CommandLineBuilderTests
{
    private static readonly Func<string, bool> PwshAvailable = _ => true;
    private static readonly Func<string, bool> PwshMissing = _ => false;

    private static string DecodeEncodedCommand(LaunchPlan plan)
    {
        Assert.NotNull(plan.ArgumentList);
        var list = plan.ArgumentList!;
        var index = list.ToList().IndexOf("-EncodedCommand");
        Assert.True(index >= 0 && index + 1 < list.Count, "Expected -EncodedCommand followed by a payload.");
        var bytes = Convert.FromBase64String(list[index + 1]);
        return Encoding.Unicode.GetString(bytes);
    }

    // ---- pwsh / powershell matrix: visible+keepOpen, visible+!keepOpen, hidden ----

    [Theory]
    [InlineData(ShellKind.Pwsh, "pwsh")]
    [InlineData(ShellKind.Powershell, "powershell")]
    public void PowerShellCommand_Visible_KeepOpen_AddsNoExit(ShellKind shell, string expectedExe)
    {
        var node = new CommandNode { Command = "Get-Date", Shell = shell, Window = WindowMode.Visible, KeepOpen = true };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings(), PwshAvailable);

        Assert.Equal(expectedExe, plan.FileName);
        Assert.Contains("-NoExit", plan.ArgumentList!);
        Assert.DoesNotContain("-NonInteractive", plan.ArgumentList!);
        Assert.False(plan.CreateNoWindow);
        Assert.Equal(LaunchWindowStyle.Normal, plan.WindowStyle);
        Assert.Equal("Get-Date", DecodeEncodedCommand(plan));
    }

    [Theory]
    [InlineData(ShellKind.Pwsh, "pwsh")]
    [InlineData(ShellKind.Powershell, "powershell")]
    public void PowerShellCommand_Visible_NoKeepOpen_OmitsNoExit(ShellKind shell, string expectedExe)
    {
        var node = new CommandNode { Command = "Get-Date", Shell = shell, Window = WindowMode.Visible, KeepOpen = false };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings(), PwshAvailable);

        Assert.Equal(expectedExe, plan.FileName);
        Assert.DoesNotContain("-NoExit", plan.ArgumentList!);
        Assert.DoesNotContain("-NonInteractive", plan.ArgumentList!);
        Assert.False(plan.CreateNoWindow);
    }

    [Theory]
    [InlineData(ShellKind.Pwsh, true)]
    [InlineData(ShellKind.Pwsh, false)]
    [InlineData(ShellKind.Powershell, true)]
    [InlineData(ShellKind.Powershell, false)]
    public void PowerShellCommand_Hidden_NeverAddsNoExit_RegardlessOfKeepOpen(ShellKind shell, bool keepOpen)
    {
        var node = new CommandNode { Command = "Get-Date", Shell = shell, Window = WindowMode.Hidden, KeepOpen = keepOpen };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings(), PwshAvailable);

        Assert.DoesNotContain("-NoExit", plan.ArgumentList!);
        Assert.Contains("-NonInteractive", plan.ArgumentList!);
        Assert.Contains("-WindowStyle", plan.ArgumentList!);
        Assert.Contains("Hidden", plan.ArgumentList!);
        Assert.True(plan.CreateNoWindow);
        Assert.Equal(LaunchWindowStyle.Hidden, plan.WindowStyle);
    }

    [Fact]
    public void PowerShellCommand_RunAsAdmin_UsesShellExecuteAndRunasVerb_AndSkipsCreateNoWindow()
    {
        var node = new CommandNode { Command = "Get-Date", Shell = ShellKind.Pwsh, Window = WindowMode.Hidden, RunAsAdmin = true };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings(), PwshAvailable);

        Assert.True(plan.UseShellExecute);
        Assert.Equal("runas", plan.Verb);
        // ShellExecute ignores CreateNoWindow; hiding relies on -WindowStyle Hidden instead.
        Assert.False(plan.CreateNoWindow);
        Assert.Equal(LaunchWindowStyle.Hidden, plan.WindowStyle);
    }

    // ---- cmd matrix ----

    [Fact]
    public void CmdCommand_Visible_KeepOpen_UsesSlashK()
    {
        var node = new CommandNode { Command = "echo hi", Shell = ShellKind.Cmd, Window = WindowMode.Visible, KeepOpen = true };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings());

        Assert.Equal("cmd.exe", plan.FileName);
        Assert.Equal("/s /k \"echo hi\"", plan.Arguments);
        Assert.False(plan.CreateNoWindow);
    }

    [Fact]
    public void CmdCommand_Visible_NoKeepOpen_UsesSlashC()
    {
        var node = new CommandNode { Command = "echo hi", Shell = ShellKind.Cmd, Window = WindowMode.Visible, KeepOpen = false };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings());

        Assert.Equal("/s /c \"echo hi\"", plan.Arguments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CmdCommand_Hidden_AlwaysUsesSlashC_RegardlessOfKeepOpen(bool keepOpen)
    {
        var node = new CommandNode { Command = "echo hi", Shell = ShellKind.Cmd, Window = WindowMode.Hidden, KeepOpen = keepOpen };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings());

        Assert.Equal("/s /c \"echo hi\"", plan.Arguments);
        Assert.True(plan.CreateNoWindow);
    }

    [Fact]
    public void CmdCommand_WithEmbeddedQuotes_PassesThroughVerbatimViaSlashSTrick()
    {
        const string command = "echo \"hello world\" & echo done";
        var node = new CommandNode { Command = command, Shell = ShellKind.Cmd, Window = WindowMode.Visible, KeepOpen = false };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings());

        Assert.Equal($"/s /c \"{command}\"", plan.Arguments);
    }

    // ---- Shell resolution / fallback ----

    [Fact]
    public void ResolveShell_PwshRequestedButMissingFromPath_FallsBackToPowershell()
    {
        var node = new CommandNode { Command = "Get-Date", Shell = ShellKind.Pwsh };
        var resolved = CommandLineBuilder.ResolveShell(node, new Settings(), PwshMissing);

        Assert.Equal(ShellKind.Powershell, resolved);
    }

    [Fact]
    public void ResolveShell_PwshAvailable_StaysOnPwsh()
    {
        var node = new CommandNode { Command = "Get-Date", Shell = ShellKind.Pwsh };
        var resolved = CommandLineBuilder.ResolveShell(node, new Settings(), PwshAvailable);

        Assert.Equal(ShellKind.Pwsh, resolved);
    }

    [Fact]
    public void ResolveShell_NodeShellNull_FallsBackToSettingsDefaultShell()
    {
        var node = new CommandNode { Command = "echo hi", Shell = null };
        var settings = new Settings { DefaultShell = ShellKind.Cmd };
        var resolved = CommandLineBuilder.ResolveShell(node, settings, PwshAvailable);

        Assert.Equal(ShellKind.Cmd, resolved);
    }

    [Fact]
    public void BuildForCommand_PwshFallback_ProducesPowershellPlan()
    {
        var node = new CommandNode { Command = "Get-Date", Shell = ShellKind.Pwsh, Window = WindowMode.Visible, KeepOpen = true };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings(), PwshMissing);

        Assert.Equal("powershell", plan.FileName);
    }

    // ---- Env var expansion, quoting hazards, spaces ----

    [Fact]
    public void BuildForCommand_ExpandsEnvironmentVariablesInCommandText()
    {
        const string command = "Write-Output %TEMP% $env:USERNAME \"quoted && text\"";
        var expected = Environment.ExpandEnvironmentVariables(command);
        var node = new CommandNode { Command = command, Shell = ShellKind.Pwsh, Window = WindowMode.Visible };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings(), PwshAvailable);

        Assert.Equal(expected, DecodeEncodedCommand(plan));
        // %TEMP% is a real env var so it must actually have been substituted, not left literal.
        Assert.DoesNotContain("%TEMP%", DecodeEncodedCommand(plan));
        // $env:USERNAME is PowerShell syntax, not a %NAME% token, so ExpandEnvironmentVariables leaves it alone.
        Assert.Contains("$env:USERNAME", DecodeEncodedCommand(plan));
    }

    [Fact]
    public void BuildForCommand_ExpandsWorkingDirectory()
    {
        var node = new CommandNode { Command = "dir", Shell = ShellKind.Cmd, WorkingDirectory = "%TEMP%" };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings());

        Assert.Equal(Environment.ExpandEnvironmentVariables("%TEMP%"), plan.WorkingDirectory);
    }

    // ---- app / path / url ----

    [Fact]
    public void BuildForApp_DefaultsWorkingDirectoryToTargetDirectory_WhenNotSpecified()
    {
        var node = new AppNode { Target = "C:\\Program Files\\My App\\app.exe" };
        var plan = CommandLineBuilder.BuildForApp(node);

        Assert.Equal("C:\\Program Files\\My App\\app.exe", plan.FileName);
        Assert.Equal("C:\\Program Files\\My App", plan.WorkingDirectory);
        Assert.True(plan.UseShellExecute);
        Assert.Null(plan.Verb);
    }

    [Fact]
    public void BuildForApp_ExpandsEnvVarsInTargetArgumentsAndWorkingDirectory()
    {
        var node = new AppNode
        {
            Target = "%WINDIR%\\notepad.exe",
            Arguments = "%TEMP%\\file.txt",
            WorkingDirectory = "%TEMP%",
        };
        var plan = CommandLineBuilder.BuildForApp(node);

        Assert.Equal(Environment.ExpandEnvironmentVariables("%WINDIR%\\notepad.exe"), plan.FileName);
        Assert.Equal(Environment.ExpandEnvironmentVariables("%TEMP%\\file.txt"), plan.Arguments);
        Assert.Equal(Environment.ExpandEnvironmentVariables("%TEMP%"), plan.WorkingDirectory);
    }

    [Fact]
    public void BuildForApp_RunAsAdmin_SetsRunasVerb()
    {
        var node = new AppNode { Target = "C:\\a.exe", RunAsAdmin = true };
        var plan = CommandLineBuilder.BuildForApp(node);

        Assert.Equal("runas", plan.Verb);
        Assert.True(plan.UseShellExecute);
    }

    [Fact]
    public void BuildForPath_UsesShellExecute()
    {
        var node = new PathNode { Target = "%USERPROFILE%\\Documents" };
        var plan = CommandLineBuilder.BuildForPath(node);

        Assert.Equal(Environment.ExpandEnvironmentVariables("%USERPROFILE%\\Documents"), plan.FileName);
        Assert.True(plan.UseShellExecute);
    }

    [Fact]
    public void BuildForUrl_UsesShellExecute()
    {
        var node = new UrlNode { Target = "https://example.com" };
        var plan = CommandLineBuilder.BuildForUrl(node);

        Assert.Equal("https://example.com", plan.FileName);
        Assert.True(plan.UseShellExecute);
    }
}
