using System.Text;
using YourLauncher.Core.Plugins;

namespace YourLauncher.Core.Tests.Plugins;

public class PluginManifestParserTests
{
    [Fact]
    public void Parse_FullManifest_ReadsEveryField()
    {
        var result = PluginManifestParser.Parse(
            """
            { "name": "GitHub", "keyword": "gh", "command": "pwsh", "args": ["-NoProfile", "-File", "search.ps1"],
              "workingDirectory": "C:\\work", "timeoutMs": 5000, "debounceMs": 300, "glyph": "x", "icon": "gh.ico" }
            """,
            "github-folder");

        Assert.True(result.Success);
        var m = result.Manifest!;
        Assert.Equal("GitHub", m.Name);
        Assert.Equal("gh", m.Keyword);
        Assert.Equal("pwsh", m.Command);
        Assert.Equal(["-NoProfile", "-File", "search.ps1"], m.Args);
        Assert.Equal("C:\\work", m.WorkingDirectory);
        Assert.Equal(5000, m.TimeoutMs);
        Assert.Equal(300, m.DebounceMs);
        Assert.Equal("x", m.Glyph);
        Assert.Equal("gh.ico", m.Icon);
    }

    [Fact]
    public void Parse_MinimalManifest_AppliesDefaults()
    {
        var m = PluginManifestParser.Parse("""{ "keyword": "uu", "command": "pwsh" }""", "uuid").Manifest!;

        Assert.Equal("uuid", m.Name);
        Assert.Empty(m.Args);
        Assert.Null(m.WorkingDirectory);
        Assert.Equal(PluginProtocol.DefaultTimeoutMs, m.TimeoutMs);
        Assert.Equal(PluginProtocol.DefaultDebounceMs, m.DebounceMs);
        Assert.Null(m.Glyph);
        Assert.Null(m.Icon);
    }

    [Fact]
    public void Parse_UnknownFieldsCommentsTrailingCommasAndCase_AreTolerated()
    {
        var result = PluginManifestParser.Parse(
            """
            {
              // a comment
              "Keyword": "x", "COMMAND": "node", "future": { "a": 1 },
            }
            """,
            "f");

        Assert.True(result.Success);
        Assert.Equal("x", result.Manifest!.Keyword);
    }

    [Theory]
    [InlineData("""{ "command": "pwsh" }""", "keyword")]
    [InlineData("""{ "keyword": "  ", "command": "pwsh" }""", "keyword")]
    [InlineData("""{ "keyword": "a b", "command": "pwsh" }""", "spaces")]
    [InlineData("""{ "keyword": "a" }""", "command")]
    [InlineData("""{ "keyword": "a", "command": "" }""", "command")]
    [InlineData("""{ "keyword": "a", "command": "x", "args": "notalist" }""", "not valid")]
    [InlineData("""{ "keyword": "a", "command": "x", "args": ["ok", null] }""", "args")]
    [InlineData("""{ "keyword": 5, "command": "x" }""", "not valid")]
    [InlineData("{ not json", "not valid")]
    [InlineData("null", "empty")]
    public void Parse_Invalid_IsRejectedWithReason(string json, string reasonContains)
    {
        var result = PluginManifestParser.Parse(json, "f");

        Assert.False(result.Success);
        Assert.Contains(reasonContains, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_OutOfRangeNumbers_AreClamped()
    {
        var m = PluginManifestParser.Parse(
            """{ "keyword": "a", "command": "x", "timeoutMs": 999999999, "debounceMs": -5 }""", "f").Manifest!;

        Assert.Equal(PluginProtocol.MaxTimeoutMs, m.TimeoutMs);
        Assert.Equal(0, m.DebounceMs);

        var low = PluginManifestParser.Parse("""{ "keyword": "a", "command": "x", "timeoutMs": 1 }""", "f").Manifest!;
        Assert.Equal(PluginProtocol.MinTimeoutMs, low.TimeoutMs);
    }
}

public class PluginOutputParserTests
{
    [Fact]
    public void Parse_AllThreeActionTypes()
    {
        var items = PluginOutputParser.Parse(
            """
            { "items": [
              { "title": "Open", "subtitle": "s", "icon": "i.png", "action": { "type": "open", "target": "https://example.com" } },
              { "title": "Copy", "action": { "type": "copy", "text": "abc" } },
              { "title": "Run", "action": { "type": "RUN", "command": "notepad.exe", "args": ["a.txt"] } }
            ] }
            """);

        Assert.Equal(3, items.Count);
        Assert.Equal(PluginActionKind.Open, items[0].Action.Kind);
        Assert.Equal("https://example.com", items[0].Action.Target);
        Assert.Equal("s", items[0].Subtitle);
        Assert.Equal("i.png", items[0].Icon);
        Assert.Equal(PluginActionKind.Copy, items[1].Action.Kind);
        Assert.Equal("abc", items[1].Action.Text);
        Assert.Equal("", items[1].Subtitle);
        Assert.Equal(PluginActionKind.Run, items[2].Action.Kind);
        Assert.Equal("notepad.exe", items[2].Action.Command);
        Assert.Equal(["a.txt"], items[2].Action.Args);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "items": "x" }""")]
    [InlineData("""{ "items": [ { "title": 5 } ] }""")]
    public void Parse_GarbageOrWrongShape_YieldsNothing(string stdout)
    {
        Assert.Empty(PluginOutputParser.Parse(stdout));
    }

    [Fact]
    public void Parse_BadRowsAreSkipped_GoodRowsKept()
    {
        var items = PluginOutputParser.Parse(
            """
            { "items": [
              null,
              { "title": "", "action": { "type": "copy", "text": "x" } },
              { "title": "no action" },
              { "title": "bad type", "action": { "type": "format-disk", "target": "c:" } },
              { "title": "open without target", "action": { "type": "open" } },
              { "title": "copy without text", "action": { "type": "copy" } },
              { "title": "run without command", "action": { "type": "run" } },
              { "title": "good", "action": { "type": "copy", "text": "ok" } }
            ] }
            """);

        var item = Assert.Single(items);
        Assert.Equal("good", item.Title);
    }

    [Fact]
    public void Parse_MoreThanMaxItems_IsTruncated()
    {
        var sb = new StringBuilder("{\"items\":[");
        for (var i = 0; i < 80; i++)
        {
            sb.Append(i > 0 ? "," : "").Append("{\"title\":\"t").Append(i).Append("\",\"action\":{\"type\":\"copy\",\"text\":\"x\"}}");
        }

        sb.Append("]}");

        var items = PluginOutputParser.Parse(sb.ToString());

        Assert.Equal(PluginProtocol.MaxItems, items.Count);
        Assert.Equal("t0", items[0].Title);
    }

    [Fact]
    public void Parse_OversizeOutput_YieldsNothing()
    {
        var padding = new string(' ', PluginProtocol.MaxOutputBytes);
        var json = "{\"items\":[{\"title\":\"t\",\"action\":{\"type\":\"copy\",\"text\":\"x\"}}]}" + padding;

        Assert.Empty(PluginOutputParser.Parse(json));
    }

    [Fact]
    public void Parse_UnknownFieldsAndBom_AreTolerated()
    {
        var items = PluginOutputParser.Parse(
            "\uFEFF{ \"extra\": 1, \"items\": [ { \"title\": \"t\", \"mystery\": true, \"action\": { \"type\": \"copy\", \"text\": \"x\", \"z\": 0 } } ] }");

        Assert.Single(items);
    }

    [Fact]
    public void BuildRequest_IsOneLineWithQueryAndVersion()
    {
        var line = PluginProtocol.BuildRequest("héllo \"q\"\n2");

        Assert.DoesNotContain('\n', line);
        Assert.Equal("{\"query\":\"h\\u00E9llo \\u0022q\\u0022\\n2\",\"version\":1}", line);
    }
}

public class PluginQueryMatcherTests
{
    [Theory]
    [InlineData("gh foo", "gh", "foo")]
    [InlineData("GH foo", "gh", "foo")]
    [InlineData("gh ", "gh", "")]
    [InlineData("gh    foo bar", "gh", "foo bar")]
    [InlineData("İş x", "iş", "x")]   // Turkish dotted/dotless casing
    [InlineData("ISI x", "ısı", "x")]
    public void TryMatch_Claims(string query, string keyword, string expectedInner)
    {
        Assert.True(PluginQueryMatcher.TryMatch(keyword, query, out var inner));
        Assert.Equal(expectedInner, inner);
    }

    [Theory]
    [InlineData("gh", "gh")]       // still typing the keyword
    [InlineData("ghx foo", "gh")]  // different word
    [InlineData("g foo", "gh")]
    [InlineData("", "gh")]
    [InlineData("xgh foo", "gh")]
    public void TryMatch_DoesNotClaim(string query, string keyword)
    {
        Assert.False(PluginQueryMatcher.TryMatch(keyword, query, out _));
    }
}

public class PluginDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "yl-plugins-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Plugin(string folder, string? json)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        if (json is not null)
        {
            File.WriteAllText(Path.Combine(dir, "plugin.json"), json);
        }
    }

    [Fact]
    public void Scan_MissingDirectory_IsEmptyAndQuiet()
    {
        var result = PluginDiscovery.Scan(_root);

        Assert.Empty(result.Plugins);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Scan_LoadsValid_ReportsInvalid_IgnoresFoldersWithoutManifest()
    {
        Plugin("a-good", """{ "keyword": "a", "command": "x" }""");
        Plugin("b-broken", "{ nope");
        Plugin("c-nokeyword", """{ "command": "x" }""");
        Plugin("d-shared", null);

        var result = PluginDiscovery.Scan(_root);

        var loaded = Assert.Single(result.Plugins);
        Assert.Equal("a", loaded.Manifest.Keyword);
        Assert.Equal(Path.Combine(_root, "a-good"), loaded.Directory);
        Assert.Equal(2, result.Problems.Count);
        Assert.StartsWith("b-broken:", result.Problems[0]);
        Assert.StartsWith("c-nokeyword:", result.Problems[1]);
    }

    [Fact]
    public void Scan_DuplicateKeyword_FirstFolderByNameWins()
    {
        Plugin("zeta", """{ "keyword": "GH", "command": "x" }""");
        Plugin("alpha", """{ "keyword": "gh", "command": "x" }""");

        var result = PluginDiscovery.Scan(_root);

        var loaded = Assert.Single(result.Plugins);
        Assert.EndsWith("alpha", loaded.Directory);
        var problem = Assert.Single(result.Problems);
        Assert.StartsWith("zeta:", problem);
        Assert.Contains("alpha", problem);
    }
}
