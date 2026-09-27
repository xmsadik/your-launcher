using System.Runtime.InteropServices;

namespace YourLauncher.App.Interop;

/// <summary>Minimal Win32 P/Invoke surface needed for the global hotkey and multi-monitor positioning.</summary>
internal static partial class Win32
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int WM_HOTKEY = 0x0312;

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public const uint WM_APP = 0x8000;
    public const uint WM_NULL = 0x0000;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint NIN_BALLOONUSERCLICK = 0x0400 + 5; // WM_USER + 5

    public const uint ASFW_ANY = 0xFFFFFFFF;
    public const byte VK_MENU = 0x12;
    public const int VK_LBUTTON = 0x01;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>Polled by the panel's Deactivated handler (spec §6/§10 item 6a) to tell a drag-from-Explorer apart from an ordinary click on another window - the high bit of the return value is set while the key/button is currently down.</summary>
    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    // ---- Icons (Phase 4): SHGetFileInfo/ExtractIconEx/SHDefExtractIconW pull in a fixed-size string
    // field (SHFILEINFO.szDisplayName) that the LibraryImport source generator can't marshal on its own,
    // so these use the classic DllImport rather than the LibraryImport style used above.
    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_LARGEICON = 0x000000000;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    public const uint SHGFI_ICONLOCATION = 0x000001000;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);

    [DllImport("shell32.dll", EntryPoint = "SHDefExtractIconW", CharSet = CharSet.Unicode)]
    public static extern int SHDefExtractIconW(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, IntPtr phiconSmall, uint nIconSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>System DPI (Windows 10 1607+), used once at startup to size icon extraction (spec §7.1) before any window/HWND exists to ask <c>VisualTreeHelper.GetDpi</c> instead.</summary>
    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    // ---- Tray icon (Phase 5, own Shell_NotifyIcon interop rather than WinForms - spec §10 item 6). The
    // fixed-size string fields in NOTIFYICONDATAW need the classic DllImport marshaller, same reasoning as
    // SHFILEINFO above. ----
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_INFO = 0x00000010;

    public const uint NIIF_INFO = 0x00000001;
    public const uint NIIF_WARNING = 0x00000002;
    public const uint NIIF_NOSOUND = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersionOrTimeout;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint dwProcessId);

    [LibraryImport("user32.dll")]
    public static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    // ---- Per-monitor DPI (spec §10 item 10: fix only the centering term) and DWM window chrome (§10 item 11). ----
    public const int MDT_EFFECTIVE_DPI = 0;

    [LibraryImport("Shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMWCP_ROUND = 2;
    public const int DWMSBT_TRANSIENTWINDOW = 3;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
