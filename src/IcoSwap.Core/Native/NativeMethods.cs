using System.Runtime.InteropServices;
using System.Text;

namespace IcoSwap.Core.Native;

internal static class NativeMethods
{
    // --- Shell notifications ---
    public const int SHCNE_UPDATEDIR = 0x00001000;
    public const int SHCNE_UPDATEITEM = 0x00002000;
    public const int SHCNE_UPDATEIMAGE = 0x00008000;
    public const int SHCNE_ASSOCCHANGED = 0x08000000;
    public const uint SHCNF_IDLIST = 0x0000;
    public const uint SHCNF_DWORD = 0x0003;
    public const uint SHCNF_PATHW = 0x0005;
    public const uint SHCNF_FLUSH = 0x1000;
    public const uint SHCNF_FLUSHNOWAIT = 0x3000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, string? dwItem1, string? dwItem2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken);

    // --- Icons ---
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint PrivateExtractIcons(string szFileName, int nIconIndex, int cxIcon, int cyIcon,
        [Out] IntPtr[]? phicon, [Out] uint[]? piconid, uint nIcons, uint flags);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern int GetObject(IntPtr h, int c, ref DIBSECTION pv);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0, dsBitfields1, dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    // --- Resources (ICL / DLL) ---
    public const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    public const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;
    public static readonly IntPtr RT_ICON = 3;
    public static readonly IntPtr RT_GROUP_ICON = 14;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll")]
    public static extern bool FreeLibrary(IntPtr hModule);

    public delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll")]
    public static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    // --- INI (.url) ---
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool WritePrivateProfileString(string lpAppName, string? lpKeyName, string? lpString, string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern uint GetPrivateProfileString(string lpAppName, string lpKeyName, string lpDefault,
        StringBuilder lpReturnedString, uint nSize, string lpFileName);

    // --- Shell items ---
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx, cy;
        public SIZE(int x, int y) { cx = x; cy = y; }
    }

    [Flags]
    public enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }
}
