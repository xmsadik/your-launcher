using YourLauncher.Core.Config;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests;

/// <summary>config.json is hand-edited, so node properties must serialize in a readable order.</summary>
public class ConfigPropertyOrderTests
{
    [Fact]
    public void NodeProperties_AreWritten_IdTypeNameFirst_ChildrenLast()
    {
        var config = new LauncherConfig();
        var folder = new FolderNode { Id = "f1", Name = "Dev" };
        folder.Children.Add(new AppNode { Id = "a1", Name = "Notepad", Target = @"C:\Windows\notepad.exe" });
        config.Root.Children.Add(folder);

        var json = ConfigSerializer.Serialize(config);

        AssertInOrder(json, "\"id\": \"f1\"", "\"type\": \"folder\"", "\"name\": \"Dev\"", "\"keywords\"", "\"children\"");
        AssertInOrder(json, "\"id\": \"a1\"", "\"type\": \"app\"", "\"name\": \"Notepad\"", "\"target\"", "\"keywords\"", "\"description\"", "\"icon\"");
    }

    private static void AssertInOrder(string json, params string[] fragments)
    {
        var start = json.IndexOf(fragments[0], StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{fragments[0]}' not found");
        var last = start;
        foreach (var fragment in fragments.Skip(1))
        {
            var index = json.IndexOf(fragment, last, StringComparison.Ordinal);
            Assert.True(index > last, $"'{fragment}' should come after position {last}");
            last = index;
        }
    }
}
