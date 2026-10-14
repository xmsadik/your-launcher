using System.Diagnostics;
using System.Text;
using YourLauncher.Core.Launch;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests;

/// <summary>Escaping of the {query} placeholder per target (design rule 6): the typed text must arrive as one literal value.</summary>
public class QueryTemplateTests
{
    private static readonly Func<string, bool> PwshAvailable = _ => true;

    private static string DecodeEncodedCommand(LaunchPlan plan)
    {
        var list = plan.ArgumentList!.ToList();
        return Encoding.Unicode.GetString(Convert.FromBase64String(list[list.IndexOf("-EncodedCommand") + 1]));
    }

    // ---- IsQueryNode / TryGetTrigger ----

    [Fact]
    public void IsQueryNode_TrueForUrlCommandAndAppArgumentsWithPlaceholder()
    {
        Assert.True(QueryTemplate.IsQueryNode(new UrlNode { Target = "https://x/?q={query}" }));
        Assert.True(QueryTemplate.IsQueryNode(new CommandNode { Command = "echo {query}" }));
        Assert.True(QueryTemplate.IsQueryNode(new AppNode { Target = "a.exe", Arguments = "--find {query}" }));
    }

    [Fact]
    public void IsQueryNode_FalseWithoutPlaceholderOrForOtherFields()
    {
        Assert.False(QueryTemplate.IsQueryNode(new UrlNode { Target = "https://x" }));
        Assert.False(QueryTemplate.IsQueryNode(new AppNode { Target = "{query}.exe", Arguments = null }));
        Assert.False(QueryTemplate.IsQueryNode(new PathNode { Target = @"C:\{query}" }));
        Assert.False(QueryTemplate.IsQueryNode(new CommandNode { Command = "echo {QUERY}" })); // exact, case-sensitive
        Assert.False(QueryTemplate.IsQueryNode(new FolderNode { Name = "{query}" }));
    }

    [Fact]
    public void TryGetTrigger_PrefersFirstSingleWordKeyword_ThenSingleWordName()
    {
        var node = new UrlNode { Name = "Jira", Target = "{query}", Keywords = { "two words", "  j  ", "jira" } };
        Assert.True(QueryTemplate.TryGetTrigger(node, out var trigger));
        Assert.Equal("j", trigger);

        var byName = new UrlNode { Name = "Jira", Target = "{query}" };
        Assert.True(QueryTemplate.TryGetTrigger(byName, out trigger));
        Assert.Equal("Jira", trigger);

        var none = new UrlNode { Name = "Search Jira", Target = "{query}" };
        Assert.False(QueryTemplate.TryGetTrigger(none, out _));
    }

    // ---- Apply ----

    [Fact]
    public void Apply_ReplacesEveryPlaceholder_AndNeverRescansTheQuery()
    {
        var result = QueryTemplate.Apply("a {query} b {query}", "{query}", q => q);
        Assert.Equal("a {query} b {query}", result);

        Assert.Equal("a X b X", QueryTemplate.Apply("a {query} b {query}", "X", q => q));
    }

    // ---- URL ----

    [Theory]
    [InlineData("ABC-123", "https://j/browse/ABC-123")]
    [InlineData("a b", "https://j/browse/a%20b")]
    [InlineData("a&b=c#d+e", "https://j/browse/a%26b%3Dc%23d%2Be")]
    [InlineData("\"quoted\"", "https://j/browse/%22quoted%22")]
    [InlineData("%PATH%", "https://j/browse/%25PATH%25")]
    [InlineData("ş ı ğ", "https://j/browse/%C5%9F%20%C4%B1%20%C4%9F")]
    [InlineData(@"c:\dir\", "https://j/browse/c%3A%5Cdir%5C")]
    [InlineData("", "https://j/browse/")]
    public void Url_PercentEncodesTheQuery(string query, string expected)
    {
        var plan = CommandLineBuilder.BuildForUrl(new UrlNode { Target = "https://j/browse/{query}" }, query);
        Assert.Equal(expected, plan.FileName);
    }

    [Fact]
    public void Url_WithoutQuery_SubstitutesEmpty()
    {
        var plan = CommandLineBuilder.BuildForUrl(new UrlNode { Target = "https://x/?q={query}&l=1" });
        Assert.Equal("https://x/?q=&l=1", plan.FileName);
    }

    [Fact]
    public void Url_WithoutPlaceholder_IsUnchanged()
    {
        var plan = CommandLineBuilder.BuildForUrl(new UrlNode { Target = "https://x/a?b=c" }, "ignored");
        Assert.Equal("https://x/a?b=c", plan.FileName);
    }

    // ---- environment expansion happens first ----

    [Fact]
    public void Query_IsNeverEnvironmentExpanded_ButTemplateIs()
    {
        Environment.SetEnvironmentVariable("YL_QUERY_TEST", "expanded");
        try
        {
            var url = CommandLineBuilder.BuildForUrl(new UrlNode { Target = "https://%YL_QUERY_TEST%/{query}" }, "%YL_QUERY_TEST%");
            Assert.Equal("https://expanded/%25YL_QUERY_TEST%25", url.FileName);

            var app = CommandLineBuilder.BuildForApp(new AppNode { Target = "a.exe", Arguments = "%YL_QUERY_TEST% {query}" }, "%YL_QUERY_TEST%");
            Assert.Equal("expanded %YL_QUERY_TEST%", app.Arguments);

            var pwsh = CommandLineBuilder.BuildForCommand(
                new CommandNode { Command = "echo %YL_QUERY_TEST% {query}", Shell = ShellKind.Pwsh }, new Settings(), PwshAvailable, "%YL_QUERY_TEST%");
            Assert.Equal("echo expanded '%YL_QUERY_TEST%'", DecodeEncodedCommand(pwsh));
        }
        finally
        {
            Environment.SetEnvironmentVariable("YL_QUERY_TEST", null);
        }
    }

    // ---- PowerShell ----

    [Theory]
    [InlineData("ABC-123", "'ABC-123'")]
    [InlineData("", "''")]
    [InlineData("O'Neil", "'O''Neil'")]
    [InlineData("''", "''''''")]
    [InlineData("a b", "'a b'")]
    [InlineData("say \"hi\"", "'say \"hi\"'")]
    [InlineData("$env:USERNAME `n $(calc)", "'$env:USERNAME `n $(calc)'")]
    [InlineData("x; calc & y | z", "'x; calc & y | z'")]
    [InlineData("%PATH%", "'%PATH%'")]
    [InlineData(@"c:\dir\", @"'c:\dir\'")]
    [InlineData("it\u2019s", "'it\u2019\u2019s'")] // typographic quote also ends a PowerShell string
    public void PowerShell_SingleQuotedLiteral(string query, string expected) =>
        Assert.Equal(expected, QueryTemplate.ForPowerShell(query));

    [Theory]
    [InlineData(ShellKind.Pwsh)]
    [InlineData(ShellKind.Powershell)]
    public void PowerShellCommand_SubstitutesIntoEncodedCommand(ShellKind shell)
    {
        var node = new CommandNode { Command = "Write-Output {query}; Write-Output {query}", Shell = shell };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings(), PwshAvailable, "O'Neil");

        Assert.Equal("Write-Output 'O''Neil'; Write-Output 'O''Neil'", DecodeEncodedCommand(plan));
    }

    // ---- cmd ----

    [Theory]
    [InlineData("ABC-123", "ABC-123")]
    [InlineData("", "^\"^\"")]
    [InlineData("a b", "^\"a b^\"")]
    [InlineData("a&b", "a^&b")]
    [InlineData("a|b<c>d", "a^|b^<c^>d")]
    [InlineData("a^b", "a^^b")]
    [InlineData("%PATH%", "^%PATH^%")]
    [InlineData("(x)!", "^(x^)^!")]
    [InlineData("say \"hi\"", "^\"say \\^\"hi\\^\"^\"")]
    [InlineData(@"c:\dir\", @"c:\dir\")]
    [InlineData(@"c:\my dir\", "^\"c:\\my dir\\\\^\"")]
    [InlineData("line1\r\nline2", "^\"line1  line2^\"")]
    public void Cmd_QuotesThenCaretEscapesEveryMetacharacter(string query, string expected) =>
        Assert.Equal(expected, QueryTemplate.ForCmd(query));

    [Fact]
    public void CmdCommand_SubstitutesInsideTheSlashSWrapper()
    {
        var node = new CommandNode { Command = "echo {query}", Shell = ShellKind.Cmd, KeepOpen = false };
        var plan = CommandLineBuilder.BuildForCommand(node, new Settings(), PwshAvailable, "a & b");

        Assert.Equal("cmd.exe", plan.FileName);
        Assert.Equal("/s /c \"echo ^\"a ^& b^\"\"", plan.Arguments);
    }

    // ---- app arguments (CommandLineToArgvW) ----

    [Theory]
    [InlineData("ABC-123", "ABC-123")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\tb", "\"a\tb\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"c:\dir\", @"c:\dir\")]
    [InlineData(@"c:\my dir\", "\"c:\\my dir\\\\\"")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")] // backslash before a quote: doubled, plus the quote's own
    [InlineData("a & b | c", "\"a & b | c\"")]
    [InlineData("%PATH%", "%PATH%")]
    public void QuoteArgument_FollowsCommandLineToArgvWRules(string value, string expected) =>
        Assert.Equal(expected, QueryTemplate.QuoteArgument(value));

    [Fact]
    public void AppArguments_GetAQuotedArgument_AndNullArgumentsStayNull()
    {
        var app = CommandLineBuilder.BuildForApp(new AppNode { Target = "a.exe", Arguments = "--find {query} --x" }, "two words");
        Assert.Equal("--find \"two words\" --x", app.Arguments);

        Assert.Null(CommandLineBuilder.BuildForApp(new AppNode { Target = "a.exe" }, "q").Arguments);
    }

    [Fact]
    public void AppArguments_WithoutQuery_BecomeOneEmptyArgument()
    {
        var app = CommandLineBuilder.BuildForApp(new AppNode { Target = "a.exe", Arguments = "--find {query}" });
        Assert.Equal("--find \"\"", app.Arguments);
    }

    // ---- real round trip through cmd.exe and the Windows argv parser ----

    // Few but dense cases: each one spawns real processes (slow on a managed machine), so every query
    // packs several hazards - metacharacters, %VAR%, quotes, spaces and backslashes before quotes.
    public static IEnumerable<object[]> NastyQueries() =>
    [
        ["simple"],
        ["a & calc | b < c > d ^caret^ , ; = ( ) !bang!"],
        ["%PATH% 50% off %USERNAME% now"],
        ["say \"hi there\" \"\"\" a\\\"b"],
        ["c:\\my dir\\"],
    ];

    /// <summary>Runs the plan for real: cmd.exe -> powershell.exe -File, which reports the arguments it received (Base64 of each), so the caret/quote layering is proven rather than assumed.</summary>
    [Theory]
    [MemberData(nameof(NastyQueries))]
    public void Cmd_RoundTrip_ProgramReceivesTheQueryAsOneLiteralArgument(string query)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var script = Path.Combine(Path.GetTempPath(), $"yl-args-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, "[Console]::Out.Write((($args | ForEach-Object { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($_)) }) -join ','))");
        try
        {
            var node = new CommandNode
            {
                Command = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{script}\" first {{query}} last",
                Shell = ShellKind.Cmd,
                Window = WindowMode.Hidden,
            };
            var plan = CommandLineBuilder.BuildForCommand(node, new Settings(), PwshAvailable, query);

            var psi = new ProcessStartInfo(plan.FileName, plan.Arguments!)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(30000));

            var received = output.Split(',').Select(b => Encoding.UTF8.GetString(Convert.FromBase64String(b))).ToArray();
            Assert.Equal(new[] { "first", query, "last" }, received);
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Theory]
    [MemberData(nameof(NastyQueries))]
    public void App_RoundTrip_ProgramReceivesTheQueryAsOneLiteralArgument(string query)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var script = Path.Combine(Path.GetTempPath(), $"yl-args-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, "[Console]::Out.Write((($args | ForEach-Object { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($_)) }) -join ','))");
        try
        {
            var app = CommandLineBuilder.BuildForApp(
                new AppNode { Target = "powershell", Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" first {{query}} last" }, query);

            var psi = new ProcessStartInfo(app.FileName, app.Arguments!)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(30000));

            var received = output.Split(',').Select(b => Encoding.UTF8.GetString(Convert.FromBase64String(b))).ToArray();
            Assert.Equal(new[] { "first", query, "last" }, received);
        }
        finally
        {
            File.Delete(script);
        }
    }
}
