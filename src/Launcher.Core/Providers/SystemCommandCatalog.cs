using YourLauncher.Core.Search;

namespace YourLauncher.Core.Providers;

/// <summary>One built-in system command: stable id, match keywords, glyph and whether it needs the confirm bar.</summary>
/// <param name="Id">Stable ("sys:lock"); usage history keys off it, never rename.</param>
/// <param name="Keywords">English and Turkish synonyms matched at weight 0.8 next to the (localized) title.</param>
/// <param name="Glyph">Segoe Fluent Icons code point.</param>
/// <param name="ConfirmLaunch">True for commands that end the session or destroy data.</param>
public sealed record SystemCommandDef(string Id, string Glyph, bool ConfirmLaunch, IReadOnlyList<string> Keywords);

/// <summary>A command that matched a query: its definition, the score and the highlight indices into its title.</summary>
public sealed record SystemCommandMatch(SystemCommandDef Command, int Score, IReadOnlyList<int> HighlightPositions);

/// <summary>
/// The system-command catalogue and its matching (no Win32 here, so it is unit-testable). Titles are not
/// stored: they are user-visible text and live in the app's resources, passed in as <c>titleOf</c>. Matching
/// reuses <see cref="FuzzyScorer"/> and <see cref="TextNormalizer"/> exactly like node search, so "kilitle",
/// "KİLİTLE" and "kilıtle" behave alike. Turkish keywords are included so Turkish users find the commands
/// before the UI itself is translated.
/// </summary>
public static class SystemCommandCatalog
{
    public const string Lock = "sys:lock";
    public const string Sleep = "sys:sleep";
    public const string Hibernate = "sys:hibernate";
    public const string SignOut = "sys:signout";
    public const string Restart = "sys:restart";
    public const string ShutDown = "sys:shutdown";
    public const string EmptyRecycleBin = "sys:recyclebin";

    public static IReadOnlyList<SystemCommandDef> All { get; } =
    [
        new(Lock, "\uE72E", false, ["lock screen", "lock computer", "kilitle", "ekranı kilitle", "bilgisayarı kilitle"]),
        new(Sleep, "\uE708", false, ["standby", "suspend", "uyku", "uyut", "uyku modu"]),
        new(Hibernate, "\uE708", false, ["hazırda beklet", "hazirda bekle", "hibernasyon"]),
        new(SignOut, "\uE77B", true, ["log off", "logoff", "log out", "logout", "sign off", "oturumu kapat", "çıkış yap", "oturum kapat"]),
        new(Restart, "\uE72C", true, ["reboot", "yeniden başlat", "yeniden baslat", "bilgisayarı yeniden başlat"]),
        new(ShutDown, "\uE7E8", true, ["shutdown", "power off", "turn off", "kapat", "bilgisayarı kapat", "kapatma"]),
        new(EmptyRecycleBin, "\uE74D", true, ["recycle bin", "trash", "bin", "geri dönüşüm", "geri dönüşüm kutusu", "çöp kutusu", "çöpü boşalt"]),
    ];

    /// <summary>
    /// Commands matching <paramref name="query"/>, best first. Empty/blank query returns nothing (the list
    /// must not be flooded with system commands before the user typed anything). The scattered Fuzzy tier is
    /// ignored for queries shorter than 3 chars - "lk" should not offer "Lock" next to every app.
    /// </summary>
    /// <param name="titleOf">Localized title for a command id.</param>
    /// <param name="isAvailable">Optional filter, e.g. hibernate only when the machine allows it.</param>
    public static IReadOnlyList<SystemCommandMatch> Match(string query, Func<string, string> titleOf, Func<string, bool>? isAvailable = null)
    {
        var normalizedQuery = TextNormalizer.Normalize(query).Trim();
        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        var matches = new List<SystemCommandMatch>();
        foreach (var command in All)
        {
            if (isAvailable is not null && !isAvailable(command.Id))
            {
                continue;
            }

            var best = Best(command, normalizedQuery, titleOf(command.Id));
            if (best is not null)
            {
                matches.Add(best);
            }
        }

        matches.Sort((a, b) => b.Score.CompareTo(a.Score));
        return matches;
    }

    private static SystemCommandMatch? Best(SystemCommandDef command, string normalizedQuery, string title)
    {
        var allowFuzzy = normalizedQuery.Length >= 3;
        SystemCommandMatch? best = null;

        Consider(title, 1.0, highlight: true);
        foreach (var keyword in command.Keywords)
        {
            Consider(keyword, 0.8, highlight: false);
        }

        return best;

        void Consider(string text, double weight, bool highlight)
        {
            var result = FuzzyScorer.Score(normalizedQuery, TextNormalizer.Normalize(text));
            if (result is null || (result.Value.Tier == MatchTier.Fuzzy && !allowFuzzy))
            {
                return;
            }

            var score = (int)(result.Value.Score * weight);
            if (best is null || score > best.Score)
            {
                best = new SystemCommandMatch(command, score, highlight ? result.Value.Positions : []);
            }
        }
    }
}
