using YourLauncher.Core.Config;

namespace YourLauncher.Core.Tests;

public class ExampleConfigSmokeTest
{
    [Fact]
    public void ExampleConfigJson_ParsesSuccessfully()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config.example.json");
        Assert.True(File.Exists(path), $"Expected to find config.example.json at {Path.GetFullPath(path)}");

        var json = File.ReadAllText(path);
        var result = ConfigSerializer.Deserialize(json);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Config);
        Assert.NotEmpty(result.Config!.Root.Children);
    }
}
