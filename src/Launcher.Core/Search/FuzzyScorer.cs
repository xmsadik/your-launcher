namespace YourLauncher.Core.Search;

/// <summary>Match quality, best to worst. Ordinal order matters: used to pick the "worst" tier across tokens.</summary>
public enum MatchTier
{
    Exact,
    Prefix,
    WordStart,
    Substring,
    Fuzzy,
}

/// <summary>A single field's match result: its score, tier, and the (ascending) matched char indices in the normalized text.</summary>
public readonly record struct MatchResult(int Score, MatchTier Tier, int[] Positions);

/// <summary>
/// Deterministic fuzzy scorer (spec §7). Tiers, best to worst: Exact, Prefix, WordStart (acronym / greedy
/// word-prefix matching, e.g. "visco" against "Visual Studio Code"), Substring, Fuzzy (scattered,
/// in-order chars). A multi-word query is split into space-separated tokens that must ALL match
/// independently; scores are summed and matched positions unioned.
/// </summary>
public static class FuzzyScorer
{
    public static MatchResult? Score(string normalizedQuery, string normalizedText, bool[]? wordStarts = null)
    {
        var query = normalizedQuery.Trim();
        if (query.Length == 0)
        {
            return null;
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        var starts = wordStarts ?? ComputeWordStarts(normalizedText);

        var totalScore = 0;
        var worstTier = MatchTier.Exact;
        var positions = new SortedSet<int>();

        foreach (var token in tokens)
        {
            var result = ScoreToken(token, normalizedText, starts);
            if (result is null)
            {
                // All tokens must match somewhere in the text (spec: "keep this simple").
                return null;
            }

            totalScore += result.Value.Score;
            if (result.Value.Tier > worstTier)
            {
                worstTier = result.Value.Tier;
            }

            foreach (var p in result.Value.Positions)
            {
                positions.Add(p);
            }
        }

        return new MatchResult(totalScore, worstTier, positions.ToArray());
    }

    /// <summary>
    /// Word-start flags for a text: true at index 0, after a separator (space - _ . / \ (), or at a
    /// lower→upper camelCase boundary. Callers with case information (the original, un-normalized name)
    /// get real camelCase detection; called on already-lower-cased text (the default for every other
    /// field), the camelCase check never fires and this degrades gracefully to separator-only.
    /// </summary>
    public static bool[] ComputeWordStarts(string text)
    {
        var flags = new bool[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            if (i == 0)
            {
                flags[i] = true;
                continue;
            }

            var prev = text[i - 1];
            var cur = text[i];
            if (prev is ' ' or '-' or '_' or '.' or '/' or '\\' or '(')
            {
                flags[i] = true;
            }
            else if (char.IsLower(prev) && char.IsUpper(cur))
            {
                flags[i] = true;
            }
        }

        return flags;
    }

    private static MatchResult? ScoreToken(string token, string text, bool[] wordStarts)
    {
        if (token.Length == 0 || text.Length == 0)
        {
            return null;
        }

        if (text == token)
        {
            return new MatchResult(1000, MatchTier.Exact, CreateRange(0, token.Length));
        }

        if (text.StartsWith(token, StringComparison.Ordinal))
        {
            var bonus = Math.Max(0, 50 - (text.Length - token.Length));
            return new MatchResult(800 + bonus, MatchTier.Prefix, CreateRange(0, token.Length));
        }

        var wordStartMatch = TryMatchWordStart(token, text, wordStarts);
        if (wordStartMatch is not null)
        {
            return wordStartMatch;
        }

        var idx = text.IndexOf(token, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var bonus = wordStarts[idx] ? 50 : 0;
            var score = Math.Max(1, 400 + bonus - Math.Min(idx, 30));
            return new MatchResult(score, MatchTier.Substring, CreateRange(idx, token.Length));
        }

        return TryFuzzyMatch(token, text);
    }

    /// <summary>
    /// True if the token can be split into consecutive runs, each a literal prefix match starting exactly
    /// at a word-start index, in increasing order (e.g. "visco" -> "Vis" at word-start 0 + "Co" at
    /// word-start 14 for "Visual Studio Code"). Backtracking DP so an earlier greedy run-length choice
    /// that turns out to be a dead end can be shortened; failures are memoized to keep this bounded.
    /// </summary>
    private static MatchResult? TryMatchWordStart(string token, string text, bool[] wordStarts)
    {
        var wsIndices = new List<int>(wordStarts.Length);
        for (var i = 0; i < wordStarts.Length; i++)
        {
            if (wordStarts[i])
            {
                wsIndices.Add(i);
            }
        }

        if (wsIndices.Count == 0)
        {
            return null;
        }

        var runs = new List<(int Start, int Length)>();
        var failed = new HashSet<(int TokenPos, int WsSearchIdx)>();

        if (!Search(0, 0))
        {
            return null;
        }

        var positions = new List<int>();
        foreach (var (start, length) in runs)
        {
            for (var i = 0; i < length; i++)
            {
                positions.Add(start + i);
            }
        }

        var gapCount = Math.Max(0, runs.Count - 1);
        var bonus = Math.Max(0, 30 - gapCount * 10) + Math.Max(0, 20 - positions[0]);
        return new MatchResult(600 + bonus, MatchTier.WordStart, positions.ToArray());

        bool Search(int tokenPos, int wsSearchIdx)
        {
            if (tokenPos == token.Length)
            {
                return true;
            }

            if (wsSearchIdx >= wsIndices.Count)
            {
                return false;
            }

            var key = (tokenPos, wsSearchIdx);
            if (failed.Contains(key))
            {
                return false;
            }

            for (var wi = wsSearchIdx; wi < wsIndices.Count; wi++)
            {
                var ws = wsIndices[wi];
                var maxLen = 0;
                while (maxLen < token.Length - tokenPos && ws + maxLen < text.Length &&
                       text[ws + maxLen] == token[tokenPos + maxLen])
                {
                    maxLen++;
                }

                if (maxLen == 0)
                {
                    continue;
                }

                for (var len = maxLen; len >= 1; len--)
                {
                    var nextTextPos = ws + len;
                    var nextWsIdx = wi + 1;
                    while (nextWsIdx < wsIndices.Count && wsIndices[nextWsIdx] < nextTextPos)
                    {
                        nextWsIdx++;
                    }

                    runs.Add((ws, len));
                    if (Search(tokenPos + len, nextWsIdx))
                    {
                        return true;
                    }

                    runs.RemoveAt(runs.Count - 1);
                }
            }

            failed.Add(key);
            return false;
        }
    }

    /// <summary>
    /// Scattered, in-order fuzzy match: leftmost forward pass finds the earliest valid position for each
    /// char, then a backward pass tightens each position as close as possible to the next one (always
    /// moving right, so ordering is preserved) to minimize the total gap.
    /// </summary>
    private static MatchResult? TryFuzzyMatch(string token, string text)
    {
        var positions = new int[token.Length];
        var searchFrom = 0;
        for (var i = 0; i < token.Length; i++)
        {
            var idx = text.IndexOf(token[i], searchFrom);
            if (idx < 0)
            {
                return null;
            }

            positions[i] = idx;
            searchFrom = idx + 1;
        }

        for (var i = token.Length - 2; i >= 0; i--)
        {
            positions[i] = text.LastIndexOf(token[i], positions[i + 1] - 1);
        }

        var gapPenalty = 0;
        for (var i = 1; i < positions.Length; i++)
        {
            gapPenalty += positions[i] - positions[i - 1] - 1;
        }

        var score = Math.Max(1, 200 - gapPenalty);
        return new MatchResult(score, MatchTier.Fuzzy, positions);
    }

    private static int[] CreateRange(int start, int length)
    {
        var arr = new int[length];
        for (var i = 0; i < length; i++)
        {
            arr[i] = start + i;
        }

        return arr;
    }
}
