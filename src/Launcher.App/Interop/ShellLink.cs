using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using YourLauncher.Core.Config;

namespace YourLauncher.App.Interop;

/// <summary>
/// <c>IShellLinkW</c>/<c>IPersistFile</c> (spec §10 item 6e: classic <c>[ComImport]</c> interfaces + the
/// <c>ShellLink</c> coclass, matching the classic-<c>DllImport</c> precedent already used for
/// <c>SHFILEINFO</c>/<c>NOTIFYICONDATA</c> elsewhere in this file's sibling, rather than the
/// source-generated COM interop attributes - those need a build-time source generator step this project
/// doesn't otherwise use, and the classic form is proven to build at 0 warnings here). Only the members
/// actually read by <see cref="ShellLinkResolver"/> are declared; the vtable layout still has to match the
/// real interface exactly, so unused members are kept as placeholders in their original slot.
/// </summary>
[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out WIN32_FIND_DATAW pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[Guid("0000010B-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

/// <summary>The shell's ShellLink coclass (CLSID_ShellLink) - <c>new ShellLinkCoClass()</c> and casting to <see cref="IShellLinkW"/>/<see cref="IPersistFile"/> is how unmanaged code obtains it too.</summary>
[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkCoClass
{
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WIN32_FIND_DATAW
{
    public uint dwFileAttributes;
    public FILETIME ftCreationTime;
    public FILETIME ftLastAccessTime;
    public FILETIME ftLastWriteTime;
    public uint nFileSizeHigh;
    public uint nFileSizeLow;
    public uint dwReserved0;
    public uint dwReserved1;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string cFileName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
    public string cAlternateFileName;
}

/// <summary>
/// Resolves a <c>.lnk</c> file into the <see cref="ShellLinkInfo"/> Core's <see cref="DropMapper"/> maps
/// into a node (spec §6/§10 item 6d): <see cref="IPersistFile.Load"/> + <see cref="IShellLinkW.GetPath"/>
/// with <c>SLGP_RAWPATH</c> - deliberately never <see cref="IShellLinkW.Resolve"/>, which would silently
/// search the file system for a moved target and could block on an unreachable network path; the raw
/// stored path (env vars/relative segments intact) is what gets written into config.json either way.
/// </summary>
internal static class ShellLinkResolver
{
    private const uint STGM_READ = 0x00000000;
    private const uint SLGP_RAWPATH = 0x0004;

    /// <summary>A corrupt/unreadable .lnk yields an empty <see cref="ShellLinkInfo"/> - DropMapper then treats it like an advertised shortcut and ShellExecutes the .lnk itself, instead of the drop handler throwing.</summary>
    public static ShellLinkInfo Resolve(string lnkPath)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            ((IPersistFile)link).Load(lnkPath, STGM_READ);

            var targetBuilder = new StringBuilder(260);
            link.GetPath(targetBuilder, targetBuilder.Capacity, out _, SLGP_RAWPATH);

            var argumentsBuilder = new StringBuilder(1024);
            link.GetArguments(argumentsBuilder, argumentsBuilder.Capacity);

            var workingDirBuilder = new StringBuilder(260);
            link.GetWorkingDirectory(workingDirBuilder, workingDirBuilder.Capacity);

            var iconLocationBuilder = new StringBuilder(260);
            link.GetIconLocation(iconLocationBuilder, iconLocationBuilder.Capacity, out var iconIndex);

            return new ShellLinkInfo(
                Target: targetBuilder.Length > 0 ? targetBuilder.ToString() : null,
                Arguments: argumentsBuilder.Length > 0 ? argumentsBuilder.ToString() : null,
                WorkingDirectory: workingDirBuilder.Length > 0 ? workingDirBuilder.ToString() : null,
                IconLocation: iconLocationBuilder.Length > 0 ? iconLocationBuilder.ToString() : null,
                IconIndex: iconLocationBuilder.Length > 0 ? iconIndex : null);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"[YourLauncher] Could not read shortcut '{lnkPath}': {ex.Message}");
            return default;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }
}
