namespace YourLauncher.Core.Bookmarks;

/// <summary>
/// A bookmark file could not be parsed (malformed Chromium JSON, or a Netscape/HTML export with no
/// bookmarks in it at all). The message is user-readable as-is - the caller (Settings page) shows it
/// directly on its own error line, nothing else changes.
/// </summary>
public sealed class BookmarkImportException : Exception
{
    public BookmarkImportException(string message)
        : base(message)
    {
    }

    public BookmarkImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
