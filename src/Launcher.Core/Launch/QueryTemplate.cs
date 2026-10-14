using System.Text;
using YourLauncher.Core.Model;

namespace YourLauncher.Core.Launch;

/// <summary>
/// The <c>{query}</c> placeholder of parameterized items: a URL target, a command or an app's arguments
/// may contain it, and the text typed after the item's keyword ("jira ABC-123") replaces it at launch time.
///
/// Substitution always happens AFTER environment-variable expansion (see <see cref="CommandLineBuilder"/>),
/// so a query such as "%PATH%" reaches the target as those literal characters instead of being expanded.
/// Each target has its own escaping, so what the user typed arrives as ONE literal value:
/// URL = percent-encoded; PowerShell = single-quoted literal; cmd = quoted argument with every cmd
/// metacharacter caret-escaped; app arguments = one quoted command-line argument (CommandLineToArgvW rules).
/// All of them add the surrounding quotes themselves, so the placeholder must not be wrapped in quotes.
/// </summary>
public static class QueryTemplate
{
    public const string Placeholder = "{query}";

    /// <summary>True when the node's URL, command or app arguments contain <see cref="Placeholder"/>.</summary>
    public static bool IsQueryNode(Node node) => node switch
    {
        UrlNode url => Contains(url.Target),
        CommandNode command => Contains(command.Command),
        AppNode app => Contains(app.Arguments),
        _ => false,
    };

    /// <summary>
    /// The word that puts the search box into "ask for the query" mode for this node: its first keyword that
    /// is a single word, else its name when that is a single word. A node with neither can still be matched
    /// by search text, but cannot be pre-filled (Enter then launches it with an empty query).
    /// </summary>
    public static bool TryGetTrigger(Node node, out string trigger)
    {
        foreach (var keyword in node.Keywords)
        {
            var candidate = keyword.Trim();
            if (candidate.Length > 0 && !HasWhitespace(candidate))
            {
                trigger = candidate;
                return true;
            }
        }

        var name = node.Name.Trim();
        if (name.Length > 0 && !HasWhitespace(name))
        {
            trigger = name;
            return true;
        }

        trigger = "";
        return false;
    }

    /// <summary>Human-readable resolved target for the result row (URL percent-encoded as it will be opened; others show the raw query).</summary>
    public static string Preview(Node node, string query) => node switch
    {
        UrlNode url => ForUrl(url.Target, query),
        CommandNode command => Apply(command.Command, query, static q => q),
        AppNode app => (app.Target + " " + Apply(app.Arguments ?? "", query, static q => q)).TrimEnd(),
        _ => "",
    };

    /// <summary>Replaces every placeholder with <paramref name="escape"/>(query). A single pass: placeholder text inside the query itself is never expanded again.</summary>
    public static string Apply(string template, string query, Func<string, string> escape) =>
        template.Contains(Placeholder, StringComparison.Ordinal)
            ? template.Replace(Placeholder, escape(query), StringComparison.Ordinal)
            : template;

    /// <summary>URL: <see cref="Uri.EscapeDataString"/> - everything except unreserved characters is percent-encoded, so "&amp;", "#", "+" and spaces stay inside the one parameter.</summary>
    public static string ForUrl(string template, string query) => Apply(template, query, Uri.EscapeDataString);

    /// <summary>
    /// pwsh / powershell: a single-quoted string literal, e.g. <c>O'Neil</c> becomes <c>'O''Neil'</c>. Nothing
    /// is interpreted inside it ($, `, ", @...). PowerShell also treats the typographic quotes U+2018..U+201B
    /// as single quotes (Word and chat apps produce them on paste), so those are doubled as well.
    /// </summary>
    public static string ForPowerShell(string query)
    {
        var builder = new StringBuilder(query.Length + 2);
        builder.Append('\'');
        foreach (var c in query)
        {
            builder.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛')
            {
                builder.Append(c);
            }
        }

        return builder.Append('\'').ToString();
    }

    /// <summary>
    /// cmd.exe (inside the <c>/s /c "..."</c> wrapper): the query is first quoted as a Windows command-line
    /// argument, then EVERY cmd metacharacter - including the quotes themselves - is escaped with a caret, so
    /// cmd sees no quoting at all and strips the carets, handing the program the exact argument. Escaped:
    /// <c>" ^ &amp; | &lt; &gt; ( ) ! %</c>. Line breaks cannot be passed through cmd and become spaces.
    /// The caret in front of '%' also stops %VAR% expansion: cmd looks up the name "VAR^" and, finding
    /// nothing, leaves the text alone.
    /// </summary>
    public static string ForCmd(string query)
    {
        var argument = QuoteArgument(query.Replace('\r', ' ').Replace('\n', ' '));
        var builder = new StringBuilder(argument.Length + 8);
        foreach (var c in argument)
        {
            if (c is '"' or '^' or '&' or '|' or '<' or '>' or '(' or ')' or '!' or '%')
            {
                builder.Append('^');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// One Windows command-line argument that CommandLineToArgvW (and the C runtime) parses back to exactly
    /// <paramref name="value"/>: empty becomes <c>""</c>; text with a space, tab or quote is wrapped in quotes,
    /// every quote gets a backslash, and backslashes in front of a quote (or of the closing quote) are doubled.
    /// Anything else is returned unchanged.
    /// </summary>
    public static string QuoteArgument(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (value.AsSpan().IndexOfAny(" \t\n\v\"") < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 4);
        builder.Append('"');
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }

    private static bool Contains(string? text) => text is not null && text.Contains(Placeholder, StringComparison.Ordinal);

    private static bool HasWhitespace(string text) => text.Any(char.IsWhiteSpace);
}
