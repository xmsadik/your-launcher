using YourLauncher.Core.Providers;

namespace YourLauncher.Core.Tests.Providers;

public class WindowMatcherTests
{
    private static WindowInfo Win(long handle, string title, string process) => new(handle, title, process, null);

    [Fact]
    public void IsSwitchable_PlainVisibleTitledWindow_IsListed()
    {
        Assert.True(WindowMatcher.IsSwitchable(true, false, "Notes", 0, false, "Notepad"));
    }

    [Theory]
    [InlineData(false, false, "T", 0u, false, "C")]            // not visible
    [InlineData(true, true, "T", 0u, false, "C")]              // cloaked (other virtual desktop / suspended UWP)
    [InlineData(true, false, "", 0u, false, "C")]              // no title
    [InlineData(true, false, "   ", 0u, false, "C")]           // blank title
    [InlineData(true, false, "T", 0u, true, "C")]              // owned (dialog / popup)
    [InlineData(true, false, "T", 0x80u, false, "C")]          // WS_EX_TOOLWINDOW
    [InlineData(true, false, "T", 0x08000000u, false, "C")]    // WS_EX_NOACTIVATE
    [InlineData(true, false, "Program Manager", 0u, false, "Progman")]
    public void IsSwitchable_Excluded(bool visible, bool cloaked, string title, uint exStyle, bool hasOwner, string className)
    {
        Assert.False(WindowMatcher.IsSwitchable(visible, cloaked, title, exStyle, hasOwner, className));
    }

    [Fact]
    public void IsSwitchable_CloakedWindow_ListedOnlyWhenOtherDesktopsAreIncluded()
    {
        // A GlazeWM-hidden / other-virtual-desktop window: cloaked, ordinary class.
        Assert.False(WindowMatcher.IsSwitchable(true, true, "Inbox", 0, false, "Outlook Host"));
        Assert.True(WindowMatcher.IsSwitchable(true, true, "Inbox", 0, false, "Outlook Host", includeCloaked: true));
    }

    [Fact]
    public void IsSwitchable_CloakedStoreAppGhosts_StayHiddenEvenWhenIncluded()
    {
        Assert.False(WindowMatcher.IsSwitchable(true, true, "Settings", 0, false, WindowMatcher.UwpCoreWindowClass, includeCloaked: true));
        Assert.False(WindowMatcher.IsSwitchable(true, true, "Calculator", 0, false, WindowMatcher.UwpFrameClass, includeCloaked: true, isEmptyUwpFrame: true));

        // A real Store app on another desktop still has its CoreWindow inside the frame.
        Assert.True(WindowMatcher.IsSwitchable(true, true, "Calculator", 0, false, WindowMatcher.UwpFrameClass, includeCloaked: true, isEmptyUwpFrame: false));
    }

    [Fact]
    public void IsSwitchable_VisibleUwpFrame_UnaffectedByGhostRule()
    {
        Assert.True(WindowMatcher.IsSwitchable(true, false, "Calculator", 0, false, WindowMatcher.UwpFrameClass));
    }

    [Fact]
    public void IsSwitchable_ToolWindowThatAsksForTaskbar_IsListed()
    {
        Assert.True(WindowMatcher.IsSwitchable(true, false, "T", WindowMatcher.WsExToolWindow | WindowMatcher.WsExAppWindow, false, "C"));
    }

    [Fact]
    public void Match_BlankQuery_ReturnsNothing()
    {
        Assert.Empty(WindowMatcher.Match("  ", [Win(1, "Notes", "notepad")]));
    }

    [Fact]
    public void Match_ByTitle_HighlightsTitle()
    {
        var matches = WindowMatcher.Match("notes", [Win(1, "Notes - draft", "notepad"), Win(2, "Calculator", "calc")]);

        var only = Assert.Single(matches);
        Assert.Equal(1, only.Window.Handle);
        Assert.NotEmpty(only.HighlightPositions);
    }

    [Fact]
    public void Match_ByProcessName_NoHighlight()
    {
        var matches = WindowMatcher.Match("chrome", [Win(1, "Inbox (3)", "chrome")]);

        var only = Assert.Single(matches);
        Assert.Empty(only.HighlightPositions);
    }

    [Fact]
    public void Match_TitleOutranksSameTextInProcessName()
    {
        var matches = WindowMatcher.Match("term", [Win(1, "Other", "term"), Win(2, "term", "other")]);

        Assert.Equal(new long[] { 2, 1 }, matches.Select(m => m.Window.Handle).ToArray());
    }

    [Fact]
    public void Match_TurkishCasingAndDiacritics()
    {
        var matches = WindowMatcher.Match("cizelge", [Win(1, "ÇİZELGE - Excel", "excel")]);

        Assert.Single(matches);
    }

    [Fact]
    public void Match_ShortQuery_IgnoresScatteredFuzzy()
    {
        // "nx" only matches "Notepad Next" as a scattered subsequence.
        Assert.Empty(WindowMatcher.Match("nx", [Win(1, "Notepad Next", "app")]));
    }

    [Fact]
    public void Match_SameProcessManyWindows_AllListed()
    {
        var matches = WindowMatcher.Match("chrome", [Win(1, "A", "chrome"), Win(2, "B", "chrome")]);

        Assert.Equal(2, matches.Count);
    }
}
