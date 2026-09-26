using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests;

public class ConfigSerializerTests
{
    private static LauncherConfig BuildNestedConfig()
    {
        // root -> level1 -> level2 -> level3 -> level4 (4 levels of folder nesting below root),
        // with one of each leaf node type inside the deepest folder.
        var level4 = new FolderNode
        {
            Id = "level4",
            Name = "Level 4",
            Children = new List<Node>
            {
                new AppNode
                {
                    Id = "app1",
                    Name = "Notepad",
                    Target = "C:\\Windows\\notepad.exe",
                    Arguments = "test.txt",
                    WorkingDirectory = "C:\\Temp",
                    RunAsAdmin = false,
                },
                new PathNode
                {
                    Id = "path1",
                    Name = "Documents",
                    Target = "%USERPROFILE%\\Documents",
                },
                new CommandNode
                {
                    Id = "cmd1",
                    Name = "Build",
                    Command = "git pull && npm run build",
                    Shell = ShellKind.Pwsh,
                    Window = WindowMode.Visible,
                    KeepOpen = true,
                    RunAsAdmin = false,
                },
                new UrlNode
                {
                    Id = "url1",
                    Name = "Anthropic",
                    Target = "https://www.anthropic.com",
                },
            },
        };

        var level3 = new FolderNode { Id = "level3", Name = "Level 3", Children = new List<Node> { level4 } };
        var level2 = new FolderNode { Id = "level2", Name = "Level 2", Children = new List<Node> { level3 } };
        var level1 = new FolderNode { Id = "level1", Name = "Level 1", Children = new List<Node> { level2 } };
        var root = new FolderNode { Id = "root", Name = "Root", Children = new List<Node> { level1 } };

        return new LauncherConfig
        {
            Version = 1,
            Settings = new Settings
            {
                Hotkey = "Alt+Space",
                Theme = Theme.Dark,
                StartWithWindows = true,
                CloseAfterLaunch = true,
                MaxVisibleItems = 8,
                DefaultShell = ShellKind.Pwsh,
                RememberLastLocation = false,
                ShowHintBar = true,
            },
            Root = root,
        };
    }

    [Fact]
    public void RoundTrip_PreservesAllFiveNodeTypesAndFourLevelsOfNesting()
    {
        var original = BuildNestedConfig();

        var json = ConfigSerializer.Serialize(original);
        var result = ConfigSerializer.Deserialize(json);

        Assert.True(result.Success, result.Error);
        var config = result.Config!;

        Assert.Equal(1, config.Version);
        Assert.Equal(Theme.Dark, config.Settings.Theme);
        Assert.Equal(ShellKind.Pwsh, config.Settings.DefaultShell);

        var level1 = Assert.IsType<FolderNode>(Assert.Single(config.Root.Children));
        var level2 = Assert.IsType<FolderNode>(Assert.Single(level1.Children));
        var level3 = Assert.IsType<FolderNode>(Assert.Single(level2.Children));
        var level4 = Assert.IsType<FolderNode>(Assert.Single(level3.Children));

        Assert.Equal(4, level4.Children.Count);

        var app = Assert.IsType<AppNode>(level4.Children[0]);
        Assert.Equal("C:\\Windows\\notepad.exe", app.Target);
        Assert.Equal("test.txt", app.Arguments);

        var path = Assert.IsType<PathNode>(level4.Children[1]);
        Assert.Equal("%USERPROFILE%\\Documents", path.Target);

        var command = Assert.IsType<CommandNode>(level4.Children[2]);
        Assert.Equal("git pull && npm run build", command.Command);
        Assert.Equal(ShellKind.Pwsh, command.Shell);

        var url = Assert.IsType<UrlNode>(level4.Children[3]);
        Assert.Equal("https://www.anthropic.com", url.Target);
    }

    [Fact]
    public void Serialize_WritesCamelCaseTypeDiscriminatorAndEnumStrings()
    {
        var config = BuildNestedConfig();
        var json = ConfigSerializer.Serialize(config);

        Assert.Contains("\"type\": \"folder\"", json);
        Assert.Contains("\"defaultShell\": \"pwsh\"", json);
        Assert.Contains("\"theme\": \"dark\"", json);
        Assert.Contains("\"maxVisibleItems\": 8", json);
    }

    [Fact]
    public void Deserialize_AllowsCommentsAndTrailingCommas()
    {
        const string json = """
        {
            // this is a comment
            "version": 1,
            "settings": {
                "hotkey": "Alt+Space",
            },
            "root": {
                "id": "root",
                "type": "folder",
                "name": "Root",
                "children": [],
            },
        }
        """;

        var result = ConfigSerializer.Deserialize(json);

        Assert.True(result.Success, result.Error);
        Assert.Equal("Alt+Space", result.Config!.Settings.Hotkey);
        Assert.Empty(result.Config.Root.Children);
    }

    [Fact]
    public void Deserialize_IgnoresUnknownProperties()
    {
        const string json = """
        {
            "version": 1,
            "somethingFromTheFuture": { "nested": true },
            "settings": { "hotkey": "Alt+Space", "unknownSetting": 42 },
            "root": { "id": "root", "type": "folder", "name": "Root", "children": [], "extra": "ignored" }
        }
        """;

        var result = ConfigSerializer.Deserialize(json);

        Assert.True(result.Success, result.Error);
        Assert.Equal("Alt+Space", result.Config!.Settings.Hotkey);
    }

    [Fact]
    public void Deserialize_MissingOptionalFields_UsesDefaultsWithoutThrowing()
    {
        const string json = """{ "root": { "id": "root", "type": "folder", "name": "Root" } } """;

        var result = ConfigSerializer.Deserialize(json);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Config!.Settings);
        Assert.Equal("Alt+Space", result.Config.Settings.Hotkey);
        Assert.Empty(result.Config.Root.Children);
    }

    [Theory]
    [InlineData("{ not valid json ")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    public void Deserialize_CorruptOrWrongShapedJson_ReturnsFailureInsteadOfThrowing(string badJson)
    {
        var result = ConfigSerializer.Deserialize(badJson);

        Assert.False(result.Success);
        Assert.Null(result.Config);
        Assert.NotNull(result.Error);
    }
}
