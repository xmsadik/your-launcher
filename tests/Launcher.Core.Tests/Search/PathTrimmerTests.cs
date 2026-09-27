using YourLauncher.Core.Search;

namespace YourLauncher.Core.Tests.Search;

public class PathTrimmerTests
{
    // One unit per char keeps the expected results easy to read.
    private static double Measure(string s) => s.Length;

    [Fact]
    public void Fits_ReturnedUnchanged()
    {
        Assert.Equal("A › B", PathTrimmer.TrimStart("A › B", 5, Measure));
    }

    [Fact]
    public void TooWide_DropsLeadingSegmentsFirst()
    {
        const string path = "Chrome bookmarks › Bookmarks bar › Work › Müşteri";

        Assert.Equal("… › Work › Müşteri", PathTrimmer.TrimStart(path, 18, Measure));
        Assert.Equal("… › Müşteri", PathTrimmer.TrimStart(path, 17, Measure));
    }

    [Fact]
    public void LastSegmentTooWide_KeepsItsTail()
    {
        Assert.Equal("…ghij", PathTrimmer.TrimStart("abc › abcdefghij", 5, Measure));
    }

    [Fact]
    public void NoSeparator_KeepsTail()
    {
        Assert.Equal("…6789", PathTrimmer.TrimStart("0123456789", 5, Measure));
    }

    [Fact]
    public void NoRoomAtAll_ReturnsEmpty()
    {
        Assert.Equal("", PathTrimmer.TrimStart("abc", 0, Measure));
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Equal("", PathTrimmer.TrimStart("", 10, Measure));
    }
}
