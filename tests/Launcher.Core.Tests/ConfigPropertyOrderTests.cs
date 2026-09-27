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

    [Fact]
    public void ConfirmLaunch_OmittedWhenFalse_RoundTripsWhenTrue()
    {
        var config = new LauncherConfig();
        config.Root.Children.Add(new AppNode { Id = "plain", Name = "Notepad", Target = @"C:\Windows\notepad.exe" });
        config.Root.Children.Add(new AppNode { Id = "off", Name = "Shut down", Target = "shutdown.exe", Arguments = "/s /t 0", ConfirmLaunch = true });

        var json = ConfigSerializer.Serialize(config);

        Assert.Equal(1, json.Split("\"confirmLaunch\"").Length - 1); // only the flagged node writes it
        AssertInOrder(json, "\"id\": \"off\"", "\"confirmLaunch\": true", "\"keywords\"");

        var reloaded = ConfigSerializer.Deserialize(json);
        Assert.True(reloaded.Success);
        Assert.False(reloaded.Config!.Root.Children[0].ConfirmLaunch);
        Assert.True(reloaded.Config.Root.Children[1].ConfirmLaunch);
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
