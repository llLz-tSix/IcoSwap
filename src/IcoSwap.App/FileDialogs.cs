using System.Runtime.InteropServices;

namespace IcoSwap.App;

/// <summary>
/// Классический диалог выбора файла/папки Windows (IFileOpenDialog).
/// Используется вместо WinRT FolderPicker/FileOpenPicker: те в некоторых контекстах запуска
/// (повышенные права, запуск из упакованного приложения) молча не открываются.
/// </summary>
internal static class FileDialogs
{
    public static string? PickFolder(IntPtr owner, string title) => Show(owner, title, pickFolders: true, filters: null);

    public static string? PickFile(IntPtr owner, string title, params (string Name, string Pattern)[] filters) =>
        Show(owner, title, pickFolders: false, filters);

    private static string? Show(IntPtr owner, string title, bool pickFolders, (string Name, string Pattern)[]? filters)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogRcw();
        try
        {
            dialog.GetOptions(out var options);
            options |= FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST;
            if (pickFolders) options |= FOS_PICKFOLDERS;
            else options |= FOS_FILEMUSTEXIST;
            dialog.SetOptions(options);
            dialog.SetTitle(title);
            if (filters is { Length: > 0 })
                dialog.SetFileTypes((uint)filters.Length, filters.Select(f => new COMDLG_FILTERSPEC { pszName = f.Name, pszSpec = f.Pattern }).ToArray());

            int hr = dialog.Show(owner);
            if (hr == unchecked((int)0x800704C7)) return null; // отмена
            Marshal.ThrowExceptionForHR(hr);
            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                return path;
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }

    private const uint FOS_PICKFOLDERS = 0x20;
    private const uint FOS_FORCEFILESYSTEM = 0x40;
    private const uint FOS_PATHMUSTEXIST = 0x800;
    private const uint FOS_FILEMUSTEXIST = 0x1000;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRcw { }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr hwndOwner);
        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
