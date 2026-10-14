using YourLauncher.Core.Providers;

namespace YourLauncher.Core.Tests.Providers;

public class SystemCommandCatalogTests
{
    private static readonly Dictionary<string, string> Titles = new()
    {
        [SystemCommandCatalog.Lock] = "Lock",
        [SystemCommandCatalog.Sleep] = "Sleep",
        [SystemCommandCatalog.Hibernate] = "Hibernate",
        [SystemCommandCatalog.SignOut] = "Sign out",
        [SystemCommandCatalog.Restart] = "Restart",
        [SystemCommandCatalog.ShutDown] = "Shut down",
        [SystemCommandCatalog.EmptyRecycleBin] = "Empty Recycle Bin",
    };

    private static IReadOnlyList<SystemCommandMatch> Match(string query, Func<string, bool>? available = null) =>
        SystemCommandCatalog.Match(query, id => Titles[id], available);

    [Fact]
    public void Catalogue_HasStableIdsAndEveryIdIsTitled()
    {
        var ids = SystemCommandCatalog.All.Select(c => c.Id).ToArray();

        Assert.Equal(
            new[] { "sys:lock", "sys:sleep", "sys:hibernate", "sys:signout", "sys:restart", "sys:shutdown", "sys:recyclebin" },
            ids);
        Assert.All(ids, id => Assert.True(Titles.ContainsKey(id)));
    }

    [Fact]
    public void Confirm_OnlyForDestructiveCommands()
    {
        var confirm = SystemCommandCatalog.All.Where(c => c.ConfirmLaunch).Select(c => c.Id).Order().ToArray();

        Assert.Equal(new[] { "sys:recyclebin", "sys:restart", "sys:shutdown", "sys:signout" }, confirm);
    }

    [Theory]
    [InlineData("lock", "sys:lock")]
    [InlineData("LOCK", "sys:lock")]
    [InlineData("kilitle", "sys:lock")]
    [InlineData("KİLİTLE", "sys:lock")]
    [InlineData("sleep", "sys:sleep")]
    [InlineData("uyku", "sys:sleep")]
    [InlineData("hibernate", "sys:hibernate")]
    [InlineData("hazırda beklet", "sys:hibernate")]
    [InlineData("sign out", "sys:signout")]
    [InlineData("logoff", "sys:signout")]
    [InlineData("oturumu kapat", "sys:signout")]
    [InlineData("restart", "sys:restart")]
    [InlineData("reboot", "sys:restart")]
    [InlineData("yeniden başlat", "sys:restart")]
    [InlineData("shut down", "sys:shutdown")]
    [InlineData("shutdown", "sys:shutdown")]
    [InlineData("empty recycle bin", "sys:recyclebin")]
    [InlineData("geri dönüşüm", "sys:recyclebin")]
    [InlineData("geri donusum", "sys:recyclebin")]
    public void Query_PutsExpectedCommandFirst(string query, string expectedId)
    {
        var matches = Match(query);

        Assert.NotEmpty(matches);
        Assert.Equal(expectedId, matches[0].Command.Id);
    }

    [Fact]
    public void Kapat_FindsShutDownAndSignOut()
    {
        var ids = Match("kapat").Select(m => m.Command.Id).ToArray();

        Assert.Contains("sys:shutdown", ids);
        Assert.Contains("sys:signout", ids);
        Assert.Equal("sys:shutdown", ids[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("random text")]
    [InlineData("notepad")]
    [InlineData("zzz")]
    [InlineData("chrome")]
    public void UnrelatedOrEmptyQuery_ReturnsNothing(string query)
    {
        Assert.Empty(Match(query));
    }

    [Fact]
    public void TwoCharFuzzy_IsIgnored()
    {
        // "lk" would scatter-match "Lock"; below 3 chars only the better tiers count (acronyms like "sd" still work).
        Assert.Empty(Match("lk"));
        Assert.Equal("sys:shutdown", Match("sd")[0].Command.Id);
        Assert.Contains(Match("shd"), m => m.Command.Id == "sys:shutdown");
    }

    [Fact]
    public void TitleMatch_ProducesHighlightPositions_KeywordMatchDoesNot()
    {
        var byTitle = Match("lock")[0];
        var byKeyword = Match("kilitle")[0];

        Assert.Equal(new[] { 0, 1, 2, 3 }, byTitle.HighlightPositions);
        Assert.Empty(byKeyword.HighlightPositions);
    }

    [Fact]
    public void UnavailableCommand_IsSkipped()
    {
        var matches = Match("hibernate", id => id != SystemCommandCatalog.Hibernate);

        Assert.DoesNotContain(matches, m => m.Command.Id == "sys:hibernate");
    }

    [Fact]
    public void Results_AreSortedByScoreDescending()
    {
        var matches = Match("re");

        Assert.Equal(matches.OrderByDescending(m => m.Score).Select(m => m.Command.Id), matches.Select(m => m.Command.Id));
    }
}
