using YourLauncher.Core.Config;
using YourLauncher.Core.Hotkeys;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Tests;

public class HotkeyConflictsTests
{
    private static AppNode App(string id, string? hotkey) => new() { Id = id, Name = id, Target = "a.exe", Hotkey = hotkey };

    [Theory]
    [InlineData("Ctrl+Alt+J", "Ctrl+Alt+J")]
    [InlineData("alt+control+j", "Ctrl+Alt+J")]
    [InlineData("Shift + Ctrl + F5", "Ctrl+Shift+F5")]
    [InlineData("Windows+Alt+Space", "Alt+Win+SPACE")]
    [InlineData("ctrl+ctrl+x", "Ctrl+X")]
    public void TryNormalize_ProducesCanonicalOrderAndCase(string text, string expected)
    {
        Assert.True(HotkeyConflicts.TryNormalize(text, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("+")]
    public void TryNormalize_RejectsMissingOrAmbiguousKey(string? text) =>
        Assert.False(HotkeyConflicts.TryNormalize(text, out _));

    [Fact]
    public void Find_SameCombinationInDifferentOrderAndCase_IsAConflict_FirstComeWins()
    {
        var a = App("a", "Ctrl+Alt+J");
        var b = App("b", "alt+ctrl+j");
        var c = App("c", "Ctrl+Alt+K");

        var conflicts = HotkeyConflicts.Find("Alt+Space", [new NodeHotkey(a, a.Hotkey!), new NodeHotkey(b, b.Hotkey!), new NodeHotkey(c, c.Hotkey!)]);

        var conflict = Assert.Single(conflicts);
        Assert.Same(b, conflict.Node);
        Assert.Same(a, conflict.Other);
    }

    [Fact]
    public void Find_ItemEqualToMainHotkey_ConflictsWithMain()
    {
        var a = App("a", "space+ALT");

        var conflict = Assert.Single(HotkeyConflicts.Find("Alt+Space", [new NodeHotkey(a, a.Hotkey!)]));

        Assert.Same(a, conflict.Node);
        Assert.Null(conflict.Other);
    }

    [Fact]
    public void Find_NoConflicts_AndInvalidHotkeysAreIgnored()
    {
        var a = App("a", "Ctrl+Alt+J");
        var b = App("b", "Ctrl+Alt+K");
        var junk1 = App("j1", "Ctrl+Alt");
        var junk2 = App("j2", "Ctrl+Alt");

        Assert.Empty(HotkeyConflicts.Find("Alt+Space", [new NodeHotkey(a, a.Hotkey!), new NodeHotkey(b, b.Hotkey!)]));
        Assert.Empty(HotkeyConflicts.Find("Alt+Space", [new NodeHotkey(junk1, junk1.Hotkey!), new NodeHotkey(junk2, junk2.Hotkey!)]));
    }

    [Fact]
    public void Find_ThreeWayDuplicate_ReportsBothLaterOnes()
    {
        var items = new[] { App("a", "Ctrl+1"), App("b", "Ctrl+1"), App("c", "ctrl+1") };

        var conflicts = HotkeyConflicts.Find("Alt+Space", items.Select(n => new NodeHotkey(n, n.Hotkey!)));

        Assert.Equal(new[] { "b", "c" }, conflicts.Select(c => c.Node.Id).ToArray());
        Assert.All(conflicts, c => Assert.Same(items[0], c.Other));
    }

    [Fact]
    public void FindOwner_ChecksMainAndOthers()
    {
        var other = App("o", "Ctrl+Alt+J");
        var others = new[] { new NodeHotkey(other, other.Hotkey!) };

        Assert.Null(HotkeyConflicts.FindOwner("Ctrl+Alt+K", "Alt+Space", others));
        Assert.Same(other, HotkeyConflicts.FindOwner("alt+ctrl+J", "Alt+Space", others)!.Node);
        Assert.Null(HotkeyConflicts.FindOwner("Alt+Space", "Alt+Space", others)!.Node);
        Assert.NotNull(HotkeyConflicts.FindOwner("ALT+SPACE", "Alt+Space", others));
    }

    [Fact]
    public void Collect_ReturnsTreeOrder_SkippingFoldersSeparatorsAndBlank()
    {
        var folder = new FolderNode { Id = "f", Name = "F", Hotkey = "Ctrl+9" };
        var inner = App("inner", "Ctrl+2");
        folder.Children.Add(inner);
        var root = new FolderNode { Id = "root", Name = "Root" };
        root.Children.Add(App("first", "Ctrl+1"));
        root.Children.Add(folder);
        root.Children.Add(new SeparatorNode { Id = "s", Hotkey = "Ctrl+8" });
        root.Children.Add(App("blank", "  "));
        root.Children.Add(App("last", "Ctrl+3"));

        var collected = HotkeyConflicts.Collect(root);

        Assert.Equal(new[] { "first", "inner", "last" }, collected.Select(h => h.Node.Id).ToArray());
    }

    // ---- model / serialization ----

    [Fact]
    public void Hotkey_BlankIsStoredAsNull_AndTrimmed()
    {
        var node = App("a", "  Ctrl+J ");
        Assert.Equal("Ctrl+J", node.Hotkey);

        node.Hotkey = "   ";
        Assert.Null(node.Hotkey);
    }

    [Fact]
    public void Hotkey_OmittedFromJsonWhenNullOrBlank_RoundTripsWhenSet_AndSitsBeforeConfirmLaunch()
    {
        var config = new LauncherConfig();
        config.Root.Children.Add(App("none", null));
        config.Root.Children.Add(App("blank", ""));
        config.Root.Children.Add(new AppNode { Id = "set", Name = "set", Target = "a.exe", Hotkey = "Ctrl+Alt+J", ConfirmLaunch = true });

        var json = ConfigSerializer.Serialize(config);

        Assert.Equal(2, json.Split("\"hotkey\"").Length - 1); // settings.hotkey + the one node that has one
        Assert.True(json.IndexOf("\"hotkey\": \"Ctrl+Alt+J\"", StringComparison.Ordinal) < json.IndexOf("\"confirmLaunch\"", StringComparison.Ordinal));

        var reloaded = ConfigSerializer.Deserialize(json);
        Assert.True(reloaded.Success);
        Assert.Null(reloaded.Config!.Root.Children[0].Hotkey);
        Assert.Null(reloaded.Config.Root.Children[1].Hotkey);
        Assert.Equal("Ctrl+Alt+J", reloaded.Config.Root.Children[2].Hotkey);
    }

    [Fact]
    public void Hotkey_EmptyStringInHandEditedJson_ReadsAsNull()
    {
        const string json = """
            { "version": 1, "settings": {}, "root": { "id": "root", "type": "folder", "name": "Root", "children": [
              { "id": "a", "type": "url", "name": "A", "target": "https://a", "hotkey": "" } ] } }
            """;

        var result = ConfigSerializer.Deserialize(json);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Config!.Root.Children[0].Hotkey);
    }

    [Fact]
    public void Duplicate_DoesNotCopyTheHotkey_NorDoesADuplicatedFolderCopyItsChildrens()
    {
        var root = new FolderNode { Id = "root", Name = "Root" };
        var app = App("a", "Ctrl+Alt+J");
        var folder = new FolderNode { Id = "f", Name = "F" };
        var child = App("child", "Ctrl+Alt+K");
        folder.Children.Add(child);
        root.Children.Add(app);
        root.Children.Add(folder);

        var appCopy = TreeOps.Duplicate(root, app)!;
        var folderCopy = (FolderNode)TreeOps.Duplicate(root, folder)!;

        Assert.Null(appCopy.Hotkey);
        Assert.Equal("Ctrl+Alt+J", app.Hotkey);
        Assert.Null(folderCopy.Children[0].Hotkey);
        Assert.Equal("Ctrl+Alt+K", child.Hotkey);
        Assert.Empty(HotkeyConflicts.Find("Alt+Space", HotkeyConflicts.Collect(root)));
    }
}
