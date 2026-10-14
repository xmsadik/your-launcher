using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;

namespace YourLauncher.App.Providers.FileSearch;

/// <summary>
/// File search through voidtools Everything's IPC (a running Everything is detected per query). The query is
/// a WM_COPYDATA "QUERY2W" request sent to Everything's hidden window; Everything answers with a WM_COPYDATA
/// to a window of ours, which is a message-only <see cref="HwndSource"/> created on the UI thread (a window
/// needs a message pump; the pool thread that runs <c>SearchAsync</c> has none). Replies are matched to
/// requests by the unique <c>reply_copydata_message</c> number we choose, so a late reply to a cancelled query
/// can never be mistaken for the current one.
/// <para>
/// The protocol is spoken directly instead of through Everything's SDK DLL: no redistribution terms, no
/// separate x64/arm64 binaries. Only the full path is requested, in Everything's default (name) order. The
/// search text is passed through as typed, so Everything's own syntax (<c>ext:pdf</c>, <c>"exact phrase"</c>)
/// works.
/// </para>
/// </summary>
public sealed class EverythingBackend : IFileSearchBackend
{
    private const int TimeoutMs = 1500;

    // everything_ipc.h
    private const uint CopyDataQuery2W = 18;         // EVERYTHING_IPC_COPYDATA_QUERY2W
    private const uint RequestFullPathAndName = 0x4; // EVERYTHING_IPC_QUERY2_REQUEST_FULL_PATH_AND_NAME
    private const uint SortNameAscending = 1;        // EVERYTHING_IPC_SORT_NAME_ASCENDING
    private const int ListHeaderSize = 20;           // totitems, numitems, offset, request_flags, sort_type
    private const int ItemSize = 8;                  // flags, data_offset

    private const int WmCopyData = 0x004A;
    private static readonly IntPtr HwndMessage = new(-3);

    /// <summary>1.4 / 1.3 use the plain class, the 1.5 alpha appends its version.</summary>
    private static readonly string[] WindowClasses =
    [
        "EVERYTHING_TASKBAR_NOTIFICATION",
        "EVERYTHING_TASKBAR_NOTIFICATION_(1.5a)",
    ];

    private readonly Dispatcher _dispatcher;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<IReadOnlyList<string>>> _pending = new();
    private Task<IntPtr>? _replyWindow;
    private int _nextRequest = 0x594C0000; // arbitrary base, so a stray WM_COPYDATA from elsewhere is unlikely to match.

    /// <param name="dispatcher">The UI thread's dispatcher: it owns the reply window.</param>
    public EverythingBackend(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public string Name => "Everything";

    public bool IsAvailable() => FindEverything() != IntPtr.Zero;

    public async Task<IReadOnlyList<string>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var target = FindEverything();
        if (target == IntPtr.Zero || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var replyWindow = await GetReplyWindowAsync().ConfigureAwait(false);

            var id = (uint)Interlocked.Increment(ref _nextRequest);
            var reply = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = reply;
            try
            {
                if (!SendQuery(target, replyWindow, id, query, maxResults))
                {
                    return [];
                }

                return await reply.Task.WaitAsync(TimeSpan.FromMilliseconds(TimeoutMs), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }
        catch (TimeoutException)
        {
            Debug.WriteLine("[YourLauncher] Everything did not answer in time.");
            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[YourLauncher] Everything query failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// The reply window, created once. Concurrent first queries share one creation task (the Lazy-style
    /// compare-exchange below publishes it before it is queued), and a faulted creation is dropped so the next
    /// query retries instead of failing forever.
    /// </summary>
    private async Task<IntPtr> GetReplyWindowAsync()
    {
        var current = _replyWindow;
        if (current is null)
        {
            var created = new Task<Task<IntPtr>>(() => _dispatcher.InvokeAsync(CreateReplyWindow).Task);
            var proxy = created.Unwrap();
            current = Interlocked.CompareExchange(ref _replyWindow, proxy, null);
            if (current is null)
            {
                current = proxy;
                created.Start(TaskScheduler.Default); // only the winner starts the creation.
            }
        }

        try
        {
            return await current.ConfigureAwait(false);
        }
        catch
        {
            _ = Interlocked.CompareExchange(ref _replyWindow, null, current);
            throw;
        }
    }

    private static IntPtr FindEverything()
    {
        foreach (var windowClass in WindowClasses)
        {
            var hwnd = EverythingNative.FindWindowW(windowClass, null);
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }
        }

        return IntPtr.Zero;
    }

    private IntPtr CreateReplyWindow()
    {
        var source = new HwndSource(new HwndSourceParameters("YourLauncher.EverythingReply")
        {
            ParentWindow = HwndMessage, // message-only: invisible, no taskbar entry, receives messages only.
            WindowStyle = 0,
        });
        source.AddHook(ReplyWndProc);
        return source.Handle;
    }

    private IntPtr ReplyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmCopyData)
        {
            return IntPtr.Zero;
        }

        handled = true;
        var copy = Marshal.PtrToStructure<EverythingNative.CopyDataStruct>(lParam);
        if (_pending.TryGetValue((uint)copy.dwData, out var reply) && copy.lpData != IntPtr.Zero && copy.cbData > 0)
        {
            var bytes = new byte[copy.cbData];
            Marshal.Copy(copy.lpData, bytes, 0, bytes.Length);
            reply.TrySetResult(ParseList(bytes));
        }

        return 1; // TRUE: processed.
    }

    private static unsafe bool SendQuery(IntPtr target, IntPtr replyWindow, uint requestId, string query, int maxResults)
    {
        // EVERYTHING_IPC_QUERY2: seven DWORDs, then the null-terminated UTF-16 search text.
        var text = Encoding.Unicode.GetBytes(query + "\0");
        var data = new byte[7 * sizeof(uint) + text.Length];
        var header = new uint[]
        {
            (uint)replyWindow.ToInt64(), // reply_hwnd (window handles always fit 32 bits)
            requestId,                   // reply_copydata_message: the dwData of the reply
            0,                           // search_flags: case-insensitive, no regex
            0,                           // offset
            (uint)maxResults,
            RequestFullPathAndName,
            SortNameAscending,
        };
        Buffer.BlockCopy(header, 0, data, 0, 7 * sizeof(uint));
        Buffer.BlockCopy(text, 0, data, 7 * sizeof(uint), text.Length);

        fixed (byte* pointer = data)
        {
            var copy = new EverythingNative.CopyDataStruct
            {
                dwData = (UIntPtr)CopyDataQuery2W,
                cbData = (uint)data.Length,
                lpData = (IntPtr)pointer,
            };

            // SendMessageTimeout: a hung Everything must not hang the search for good.
            var sent = EverythingNative.SendMessageTimeoutW(
                target, WmCopyData, replyWindow, ref copy, EverythingNative.SMTO_ABORTIFHUNG, TimeoutMs, out var result);
            return sent != IntPtr.Zero && result != IntPtr.Zero;
        }
    }

    /// <summary>
    /// Reads an EVERYTHING_IPC_LIST2 that was requested with FULL_PATH_AND_NAME only: per item a DWORD flags,
    /// a DWORD offset (from the start of the list) to "DWORD length, WCHAR path[length + 1]". Every offset is
    /// range-checked, so a malformed or differently-versioned reply yields fewer rows instead of an exception.
    /// </summary>
    internal static IReadOnlyList<string> ParseList(byte[] list)
    {
        var paths = new List<string>();
        if (list.Length < ListHeaderSize)
        {
            return paths;
        }

        var count = BitConverter.ToUInt32(list, 4); // numitems
        for (uint i = 0; i < count; i++)
        {
            var itemPosition = ListHeaderSize + (long)i * ItemSize;
            if (itemPosition + ItemSize > list.Length)
            {
                break;
            }

            var dataOffset = BitConverter.ToUInt32(list, (int)itemPosition + 4);
            if (dataOffset + 4L > list.Length)
            {
                continue;
            }

            var length = BitConverter.ToUInt32(list, (int)dataOffset);
            var start = dataOffset + 4L;
            if (length == 0 || start + length * 2L > list.Length)
            {
                continue;
            }

            paths.Add(Encoding.Unicode.GetString(list, (int)start, (int)length * 2));
        }

        return paths;
    }
}

/// <summary>The three user32 pieces the Everything IPC needs; kept next to its only user instead of in the shared Win32 class.</summary>
internal static partial class EverythingNative
{
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct CopyDataStruct
    {
        public UIntPtr dwData;
        public uint cbData;
        public IntPtr lpData;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SendMessageTimeoutW(
        IntPtr hWnd, int msg, IntPtr wParam, ref CopyDataStruct lParam, uint flags, uint timeout, out IntPtr result);
}
