using System.Security.Cryptography;

namespace YourLauncher.Core.Icons;

/// <summary>
/// Copies a user-picked custom icon file into the config directory's <c>icons\</c> subfolder (spec §4.4:
/// "custom icons copied to the icons folder so they survive the source file being deleted/moved"),
/// content-addressed so re-importing the same bytes reuses the existing copy instead of duplicating it.
/// </summary>
public static class IconFileStore
{
    /// <summary>Image formats the icon picker's File tab and this store accept (spec §4 step 4).</summary>
    public static readonly IReadOnlyCollection<string> AllowedExtensions =
        new[] { ".png", ".ico", ".jpg", ".jpeg", ".bmp", ".gif" };

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into <paramref name="iconsDir"/> (created if missing) as
    /// <c>{first 16 hex chars of SHA-256}{ext}</c> (lowercase extension). If a file with that exact name
    /// already exists (i.e. identical content was imported before), it's reused instead of copied again.
    /// Returns the full destination path.
    /// </summary>
    /// <exception cref="ArgumentException">The source file's extension isn't one of <see cref="AllowedExtensions"/>.</exception>
    public static string Import(string sourcePath, string iconsDir)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
        {
            throw new ArgumentException(
                $"Unsupported icon file extension '{ext}'. Allowed: {string.Join(", ", AllowedExtensions)}.",
                nameof(sourcePath));
        }

        Directory.CreateDirectory(iconsDir);

        var hash = ComputeContentHash(sourcePath);
        var destPath = Path.Combine(iconsDir, hash + ext);

        if (!File.Exists(destPath))
        {
            File.Copy(sourcePath, destPath, overwrite: false);
        }

        return destPath;
    }

    private static string ComputeContentHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }
}
