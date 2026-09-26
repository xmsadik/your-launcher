using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YourLauncher.App.Interop;
using YourLauncher.Core.Icons;
using YourLauncher.Core.Launch;
using YourLauncher.Core.Model;

namespace YourLauncher.App.Services;

/// <summary>
/// Extracts and caches row/picker icons (Phase 4, spec §4.4 / §2, revised §7). All actual extraction work
/// (P/Invoke + bitmap decode) runs on the thread pool via <see cref="Task.Run(Action)"/>, gated by a
/// <see cref="SemaphoreSlim"/>(2) rather than a dedicated STA thread (spec §7.3) - every extraction is
/// wrapped in try/catch and falls back to null (the row then shows its default glyph instead).
///
/// Two caches, both keyed by content rather than by call site so equivalent requests share one bitmap:
/// <list type="bullet">
/// <item><description><see cref="_cache"/>: <see cref="IconKey"/> → the image ultimately shown for that
/// node/spec. A <see cref="Task{ImageSource}"/> (not a plain <see cref="ImageSource"/>) so
/// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})"/> dedupes concurrent
/// requests for the same key and a completed task with a null result is itself a cached "extraction
/// failed, don't retry" answer.</description></item>
/// <item><description><see cref="_locationCache"/>: the shell's own icon location (the file that actually
/// holds the icon resource, e.g. imageres.dll for a generic file type, plus the index within it) → the
/// loaded bitmap. This is what makes every ".txt" row share one bitmap instead of re-extracting the same
/// system icon per node (spec §7.3).</description></item>
/// </list>
/// </summary>
public sealed class IconService
{
    private const int MaxExeIconCount = 1024;
    private const int ExeIconBatchSize = 20;

    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _cache = new();
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _locationCache = new();
    private readonly SemaphoreSlim _gate = new(2);
    private readonly string _configDirectory;
    private readonly int _targetPx;

    /// <param name="configDirectory">Base directory a relative "file" icon value is resolved against (spec §7.5).</param>
    /// <param name="dpiScale">The panel window's DPI scale, used once to pick the extraction size (spec §7.1: 32px at ≤125%, 48px above).</param>
    public IconService(string configDirectory, double dpiScale)
    {
        _configDirectory = configDirectory;
        _targetPx = dpiScale > 1.25 ? 48 : 32;
    }

    /// <summary>Synchronous, non-blocking peek: true only if this key's icon has already finished loading (successfully or not).</summary>
    public bool TryGetCached(string key, out ImageSource? image)
    {
        if (_cache.TryGetValue(key, out var task) && task.IsCompletedSuccessfully)
        {
            image = task.Result;
            return true;
        }

        image = null;
        return false;
    }

    /// <summary>Null immediately for a glyph/emoji spec (spec §7.15 - <see cref="IconKey.For"/> returns null, nothing to load).</summary>
    public Task<ImageSource?> GetAsync(IconSpec? spec, Node node)
    {
        var key = IconKey.For(spec, node);
        return key is null
            ? Task.FromResult<ImageSource?>(null)
            : _cache.GetOrAdd(key, _ => LoadGatedAsync(spec, node));
    }

    /// <summary>
    /// Extracts every icon in an exe/dll for the picker's Exe/DLL tab (spec §2/§7.11), capped at 1024.
    /// <paramref name="onBatch"/> is invoked from a background thread as each batch of
    /// <see cref="ExeIconBatchSize"/> icons finishes (spec: "fill the grid progressively") - the caller
    /// (the picker ViewModel) is responsible for marshaling back to the UI thread before touching bound
    /// collections.
    /// </summary>
    public async Task<IReadOnlyList<ImageSource>> ExtractAllAsync(
        string exeOrDllPath,
        Action<IReadOnlyList<(int Index, ImageSource Image)>>? onBatch = null,
        CancellationToken cancellationToken = default)
    {
        var path = EnvExpander.Expand(exeOrDllPath) ?? "";
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return Array.Empty<ImageSource>();
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ExtractAllCore(path, onBatch, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Clears every cached icon. // Phase 5: call this from the config-reload/FileSystemWatcher path once one exists - nothing calls it yet.</summary>
    public void Invalidate()
    {
        _cache.Clear();
        _locationCache.Clear();
    }

    /// <summary>Drops one key (spec §4 "Applying: ... invalidate that key") so a row can never show a stale bitmap under a key that happens to match what it had before.</summary>
    public void InvalidateKey(string key) => _cache.TryRemove(key, out _);

    private async Task<ImageSource?> LoadGatedAsync(IconSpec? spec, Node node)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => LoadCore(spec, node)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ImageSource? LoadCore(IconSpec? spec, Node node)
    {
        try
        {
            if (spec is not null)
            {
                return spec.Kind switch
                {
                    IconKind.File => LoadFileIcon(spec),
                    IconKind.Exe => LoadExeIcon(spec),
                    _ => null,
                };
            }

            return node switch
            {
                AppNode app => LoadAutoIcon(app.Target),
                PathNode path => LoadAutoIcon(path.Target),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Custom "file" icons (spec §2/§7.6): .ico via IconBitmapDecoder (pick the frame closest to the
    // target size); everything else via BitmapImage/StreamSource, both over a FileStream so the file
    // itself is never left locked once loading finishes.
    // ------------------------------------------------------------------------------------------------

    private ImageSource? LoadFileIcon(IconSpec spec)
    {
        var path = ResolveFilePath(spec.Value);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        return string.Equals(Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase)
            ? LoadIcoFile(path)
            : LoadRasterFile(path);
    }

    /// <summary>A relative "file" icon value (spec §7.5: config stores <c>icons\&lt;hash&gt;.&lt;ext&gt;</c>) resolves against the config directory; an absolute, hand-edited path is used as-is.</summary>
    private string? ResolveFilePath(string value)
    {
        var expanded = EnvExpander.Expand(value);
        if (string.IsNullOrEmpty(expanded))
        {
            return null;
        }

        return Path.IsPathRooted(expanded) ? expanded : Path.Combine(_configDirectory, expanded);
    }

    private ImageSource? LoadIcoFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            return null;
        }

        var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - _targetPx)).First();
        frame.Freeze();
        return frame;
    }

    private ImageSource? LoadRasterFile(string path)
    {
        var image = new BitmapImage();
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 48;
            image.StreamSource = stream;
            image.EndInit();
        }

        image.Freeze();
        return image;
    }

    // ------------------------------------------------------------------------------------------------
    // Custom "exe" icons and auto app/path system icons (spec §2/§7.1).
    // ------------------------------------------------------------------------------------------------

    private ImageSource? LoadExeIcon(IconSpec spec)
    {
        var path = EnvExpander.Expand(spec.Value);
        return string.IsNullOrEmpty(path) ? null : LoadFromLocation(path, spec.Index ?? 0);
    }

    private ImageSource? LoadAutoIcon(string target)
    {
        var expanded = EnvExpander.Expand(target) ?? "";
        if (expanded.Length == 0)
        {
            return null;
        }

        if (TargetCheck.ShouldCheckExistence(expanded))
        {
            if (File.Exists(expanded) || Directory.Exists(expanded))
            {
                return LoadShellIconForPath(expanded, useFileAttributes: false);
            }

            // Missing local target: still show a type icon by extension, without pretending it exists.
            return LoadShellIconForPath(expanded, useFileAttributes: true);
        }

        // UNC/URI/shell target, or a bare name: resolve bare names via %PATH% (spec §7.2 - the icon and
        // the missing-target badge must agree on what a bare name like "notepad.exe" resolves to); a
        // UNC/URI target is never touched on disk, straight to the extension-only lookup.
        var resolved = LooksLikeBareName(expanded) ? PathResolver.FindOnPath(expanded) : null;
        return resolved is not null
            ? LoadShellIconForPath(resolved, useFileAttributes: false)
            : LoadShellIconForPath(expanded, useFileAttributes: true);
    }

    private static bool LooksLikeBareName(string value) =>
        !value.Contains('\\') && !value.Contains('/') && !value.Contains(':');

    private ImageSource? LoadShellIconForPath(string path, bool useFileAttributes)
    {
        var flags = Win32.SHGFI_ICONLOCATION;
        uint attributes = 0;
        if (useFileAttributes)
        {
            flags |= Win32.SHGFI_USEFILEATTRIBUTES;
            attributes = Win32.FILE_ATTRIBUTE_NORMAL; // shell reads only the extension in this mode - no disk access (spec §7.2).
        }

        var shfi = new Win32.SHFILEINFO();
        var ok = Win32.SHGetFileInfo(path, attributes, ref shfi, (uint)Marshal.SizeOf<Win32.SHFILEINFO>(), flags) != IntPtr.Zero;
        if (ok && !string.IsNullOrEmpty(shfi.szDisplayName)
            && LoadFromLocation(shfi.szDisplayName, shfi.iIcon) is { } fromLocation)
        {
            return fromLocation;
        }

        // Fallback: ask for a ready-made HICON directly (older/unusual shell namespace extensions can
        // fail SHGFI_ICONLOCATION, or report a location that isn't an extractable file (GIL_NOTFILENAME),
        // but still answer SHGFI_ICON).
        var directFlags = Win32.SHGFI_ICON | Win32.SHGFI_LARGEICON | (useFileAttributes ? Win32.SHGFI_USEFILEATTRIBUTES : 0);
        var shfi2 = new Win32.SHFILEINFO();
        if (Win32.SHGetFileInfo(path, attributes, ref shfi2, (uint)Marshal.SizeOf<Win32.SHFILEINFO>(), directFlags) != IntPtr.Zero
            && shfi2.hIcon != IntPtr.Zero)
        {
            return BitmapFromHIcon(shfi2.hIcon);
        }

        return null;
    }

    /// <summary>Shared bitmap cache keyed by the shell's own icon location (spec §7.3): every node whose target shares an icon location (e.g. all ".txt" files) loads the bitmap once.</summary>
    private ImageSource? LoadFromLocation(string locationFile, int iconIndex)
    {
        var key = $"loc|{locationFile.ToLowerInvariant()}|{iconIndex}";
        return _locationCache.GetOrAdd(key, _ => Task.FromResult(ExtractIconCore(locationFile, iconIndex))).Result;
    }

    private ImageSource? ExtractIconCore(string path, int index)
    {
        if (Win32.SHDefExtractIconW(path, index, 0, out var hIcon, IntPtr.Zero, (uint)_targetPx) == 0 && hIcon != IntPtr.Zero)
        {
            return BitmapFromHIcon(hIcon);
        }

        var large = new IntPtr[1];
        if (Win32.ExtractIconEx(path, index, large, null, 1) > 0 && large[0] != IntPtr.Zero)
        {
            return BitmapFromHIcon(large[0]);
        }

        return null;
    }

    private static ImageSource? BitmapFromHIcon(IntPtr hIcon)
    {
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            Win32.DestroyIcon(hIcon);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Exe/DLL icon picker tab (spec §2/§7.11).
    // ------------------------------------------------------------------------------------------------

    private List<ImageSource> ExtractAllCore(
        string path,
        Action<IReadOnlyList<(int Index, ImageSource Image)>>? onBatch,
        CancellationToken cancellationToken)
    {
        var totalCount = Win32.ExtractIconEx(path, -1, null, null, 0);
        if (totalCount <= 0)
        {
            return new List<ImageSource>();
        }

        var count = Math.Min(totalCount, MaxExeIconCount);
        var results = new List<ImageSource>(count);
        var batch = new List<(int, ImageSource)>(ExeIconBatchSize);

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var image = ExtractIconCore(path, i);
            if (image is not null)
            {
                results.Add(image);
                batch.Add((i, image));
            }

            if (batch.Count >= ExeIconBatchSize || i == count - 1)
            {
                if (batch.Count > 0)
                {
                    onBatch?.Invoke(batch.ToList());
                    batch.Clear();
                }
            }
        }

        return results;
    }
}
