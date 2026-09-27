namespace YourLauncher.Core.Search;

/// <summary>
/// Shortens a breadcrumb path ("A › B › C") from the start so its most specific part - the end - stays
/// visible: whole leading segments are replaced by "… › " first ("… › B › C"), and only if even the last
/// segment alone doesn't fit is it cut character-wise ("…C-tail"). Width is measured by the caller
/// (<paramref name="measure"/>), so this stays free of WPF text layout.
/// </summary>
public static class PathTrimmer
{
    public const string Separator = " › ";
    private const string Ellipsis = "…";

    public static string TrimStart(string text, double maxWidth, Func<string, double> measure)
    {
        if (string.IsNullOrEmpty(text) || measure(text) <= maxWidth)
        {
            return text;
        }

        var segments = text.Split(Separator);
        for (var skip = 1; skip < segments.Length; skip++)
        {
            var candidate = Ellipsis + Separator + string.Join(Separator, segments.Skip(skip));
            if (measure(candidate) <= maxWidth)
            {
                return candidate;
            }
        }

        // Even "… › <last>" is too wide (or there were no separators): keep the tail of the last segment.
        var last = segments[^1];
        for (var start = 1; start < last.Length; start++)
        {
            var candidate = Ellipsis + last[start..];
            if (measure(candidate) <= maxWidth)
            {
                return candidate;
            }
        }

        return measure(Ellipsis) <= maxWidth ? Ellipsis : "";
    }
}
