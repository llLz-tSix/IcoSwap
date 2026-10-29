using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace IcoSwap.Core.Native;

/// <summary>Обёртка над IShellLinkW/IPersistFile для чтения и записи IconLocation в .lnk.</summary>
public sealed class ShellLink : IDisposable
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    private const uint STGM_READ = 0x0;
    private const uint STGM_READWRITE = 0x2;
    private const uint SLGP_RAWPATH = 0x4;

    private readonly IShellLinkW _link;
    private readonly string _path;

    private ShellLink(string path, bool write)
    {
        _path = path;
        _link = (IShellLinkW)new CShellLink();
        ((IPersistFile)_link).Load(path, (int)(write ? STGM_READWRITE : STGM_READ));
    }

    public static ShellLink Open(string path, bool write = false) => new(path, write);

    /// <summary>Создать новый ярлык (используется в тестах).</summary>
    public static void Create(string lnkPath, string target, string? iconPath = null, int iconIndex = 0)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(target);
            if (iconPath != null) link.SetIconLocation(iconPath, iconIndex);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    public string TargetPath
    {
        get
        {
            var sb = new StringBuilder(1024);
            try { _link.GetPath(sb, sb.Capacity, IntPtr.Zero, SLGP_RAWPATH); }
            catch { return ""; }
            return Environment.ExpandEnvironmentVariables(sb.ToString());
        }
    }

    public string Arguments
    {
        get
        {
            var sb = new StringBuilder(2048);
            try { _link.GetArguments(sb, sb.Capacity); } catch { return ""; }
            return sb.ToString();
        }
    }

    public (string Path, int Index) IconLocation
    {
        get
        {
            var sb = new StringBuilder(1024);
            _link.GetIconLocation(sb, sb.Capacity, out var idx);
            return (sb.ToString(), idx);
        }
        set => _link.SetIconLocation(value.Path, value.Index);
    }

    public void Save() => ((IPersistFile)_link).Save(_path, true);

    public void Dispose() => Marshal.FinalReleaseComObject(_link);
}
