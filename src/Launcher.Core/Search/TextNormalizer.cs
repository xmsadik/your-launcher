using System.Globalization;

namespace YourLauncher.Core.Search;

/// <summary>
/// Turkish-aware, case- and diacritic-insensitive text normalization for search (spec §7).
/// <see cref="Normalize"/> is a strict 1:1 char mapping: the output always has exactly the same length
/// as the input, so a match index found in normalized text is directly usable as an index into the
/// original text (no separate index map is needed for highlighting).
/// </summary>
public static class TextNormalizer
{
    private static readonly CultureInfo TrTr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Lower-cases with Turkish casing rules (İ→i, I→ı) then folds Turkish and a few common Latin
    /// diacritics to their plain ASCII base letter. Never uses string.ToLower, which can change length
    /// for some cultures/characters - each char is mapped individually to keep the 1:1 guarantee.
    /// </summary>
    public static string Normalize(string s)
    {
        if (s.Length == 0)
        {
            return s;
        }

        var chars = new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            chars[i] = Fold(char.ToLower(s[i], TrTr));
        }

        return new string(chars);
    }

    private static char Fold(char c) => c switch
    {
        'ı' => 'i',
        'ş' => 's',
        'ğ' => 'g',
        'ü' => 'u',
        'ö' => 'o',
        'ç' => 'c',
        // A few common Latin diacritics folded too, as long as the mapping stays 1:1.
        'â' => 'a',
        'î' => 'i',
        'û' => 'u',
        'é' => 'e',
        'è' => 'e',
        'à' => 'a',
        _ => c,
    };
}
