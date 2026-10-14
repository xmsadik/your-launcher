using System.Runtime.InteropServices;

namespace YourLauncher.App.Interop;

/// <summary>
/// Just enough of <c>IShellItem</c>/<c>IEnumShellItems</c> to list the shell's virtual
/// <c>shell:AppsFolder</c> (every app that has a Start-menu entry, packaged ones included). Classic
/// <c>[ComImport]</c> declarations like <c>ShellLink.cs</c>; the vtable order has to match the real interface,
/// so unused members stay as placeholders. No PackageManager involved - that API needs the
/// <c>packageQuery</c> capability, which the Store manifest deliberately does not declare.
/// </summary>
[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetParent(out IShellItem ppsi);
    [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport]
[Guid("70629033-E363-4A28-A567-0DB78006E6D7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    /// <summary>S_OK while an item was returned, S_FALSE (1) at the end.</summary>
    [PreserveSig] int Next(uint celt, out IShellItem rgelt, out uint pceltFetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumShellItems ppenum);
}

internal static class AppsFolder
{
    private const uint SIGDN_NORMALDISPLAY = 0x00000000;
    private const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;

    private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid IidEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");
    private static readonly Guid BhidEnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint dwFileAttributes, ref Win32.SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    private const uint SHGFI_PIDL = 0x00000008;

    /// <summary>
    /// A ready-made HICON (caller destroys it) for a shell item addressed by parsing name, e.g.
    /// <c>shell:AppsFolder\&lt;AUMID&gt;</c>: such an item has no file path for the usual extension-based icon
    /// lookup, so it is resolved to a PIDL first. Zero when the item does not exist or has no icon.
    /// </summary>
    public static IntPtr GetIconHandle(string parsingName)
    {
        if (SHParseDisplayName(parsingName, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            var info = new Win32.SHFILEINFO();
            var ok = SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf<Win32.SHFILEINFO>(), SHGFI_PIDL | Win32.SHGFI_ICON | Win32.SHGFI_LARGEICON) != IntPtr.Zero;
            return ok ? info.hIcon : IntPtr.Zero;
        }
        finally
        {
            CoTaskMemFree(pidl);
        }
    }

    /// <summary>
    /// (display name, parsing name) of every item in <c>shell:AppsFolder</c>. For a packaged app the parsing
    /// name is its AppUserModelID; for classic desktop apps it is a path or a plain AUMID. <paramref name="wantParsingName"/>
    /// lets the caller reject items before their (slow) display name is read.
    /// Must run on an STA thread. Returns an empty list if the folder cannot be opened.
    /// </summary>
    public static List<(string DisplayName, string ParsingName)> Enumerate(Func<string, bool>? wantParsingName = null)
    {
        var result = new List<(string, string)>();

        var iidItem = IidShellItem;
        if (SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref iidItem, out var folderPtr) != 0 || folderPtr == IntPtr.Zero)
        {
            return result;
        }

        var folder = (IShellItem)Marshal.GetObjectForIUnknown(folderPtr);
        Marshal.Release(folderPtr);

        IEnumShellItems? enumerator = null;
        try
        {
            var bhid = BhidEnumItems;
            var iidEnum = IidEnumShellItems;
            if (folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out var enumPtr) != 0 || enumPtr == IntPtr.Zero)
            {
                return result;
            }

            enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown(enumPtr);
            Marshal.Release(enumPtr);

            while (enumerator.Next(1, out var item, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    // Parsing name first: it is cheap, whereas the display name of a packaged app is resolved
                    // from the package's resources and dominates the scan - so skip it for rejected items.
                    var parsing = DisplayName(item, SIGDN_PARENTRELATIVEPARSING);
                    if (string.IsNullOrWhiteSpace(parsing) || (wantParsingName is not null && !wantParsingName(parsing)))
                    {
                        continue;
                    }

                    var name = DisplayName(item, SIGDN_NORMALDISPLAY);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        result.Add((name, parsing));
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }

            Marshal.ReleaseComObject(folder);
        }

        return result;
    }

    private static string DisplayName(IShellItem item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out var ptr) != 0 || ptr == IntPtr.Zero)
        {
            return "";
        }

        try
        {
            return Marshal.PtrToStringUni(ptr) ?? "";
        }
        finally
        {
            CoTaskMemFree(ptr);
        }
    }
}
