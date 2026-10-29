using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using IcoSwap.Core.Apply;
using IcoSwap.Core.Native;
using IcoSwap.Core.Targets;
using SystemIcons = IcoSwap.Core.Targets.SystemIcons;

namespace IcoSwap.Core;

/// <summary>Текущая иконка цели в виде PNG (для превью «до»).</summary>
public static class ShellImage
{
    public static byte[]? GetCurrentIconPng(IconTarget target, int size = 64)
    {
        try
        {
            using var bmp = GetCurrentIcon(target, size);
            if (bmp == null) return null;
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch { return null; }
    }

    public static Bitmap? GetCurrentIcon(IconTarget t, int size)
    {
        var id = t.Id;
        if (id.StartsWith("ext:"))
            return FromParsingName(ProbeFile(id[4..]), size);
        if (id.StartsWith("shell:"))
        {
            var loc = RegistryBackupRead("HKLM", SystemIcons.ShellIconsKey, id.EndsWith("open") ? "4" : "3");
            return loc != null ? FromIconLocation(loc, size) : FromParsingName(ProbeFolder(), size);
        }
        if (id.StartsWith("sys:"))
        {
            var def = SystemIcons.Find(id[4..]);
            if (def == null) return null;
            var (clsid, value) = def.Values[0];
            var loc = RegistryBackupRead("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\" + clsid + @"\DefaultIcon", value)
                      ?? SystemIconHandler.SystemDefault(clsid, value);
            return loc != null ? FromIconLocation(loc, size) : FromParsingName("::" + clsid, size);
        }
        if (id.StartsWith("kf:"))
        {
            var def = SystemIcons.FindKnownFolder(id[3..]);
            var path = def != null ? SystemIcons.GetKnownFolderPath(def) : null;
            return path != null ? FromParsingName(path, size) : null;
        }
        if (t.Path != null) return FromParsingName(t.Path, size);
        return null;
    }

    private static string? RegistryBackupRead(string hive, string key, string value)
    {
        try { return RegistryBackup.ReadString(hive, key, value); } catch { return null; }
    }

    /// <summary>Пустой файл с нужным расширением — по нему Проводник отдаёт иконку типа.</summary>
    private static string ProbeFile(string ext)
    {
        var dir = Path.Combine(AppPaths.Temp, "probe");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "file" + FileTypeRegistry.NormalizeExt(ext));
        if (!File.Exists(file)) File.WriteAllBytes(file, Array.Empty<byte>());
        return file;
    }

    private static string ProbeFolder()
    {
        var dir = Path.Combine(AppPaths.Temp, "probe", "folder");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>"путь,индекс" (индекс может быть отрицательным id ресурса).</summary>
    public static Bitmap? FromIconLocation(string location, int size)
    {
        location = Environment.ExpandEnvironmentVariables(location.Trim());
        int index = 0;
        var comma = location.LastIndexOf(',');
        string path = location;
        if (comma > 0 && int.TryParse(location[(comma + 1)..].Trim(), out var idx))
        {
            path = location[..comma];
            index = idx;
        }
        path = path.Trim().Trim('"');
        var icons = new IntPtr[1];
        var ids = new uint[1];
        var n = NativeMethods.PrivateExtractIcons(path, index, size, size, icons, ids, 1, 0);
        if (n == 0 || n == 0xFFFFFFFF || icons[0] == IntPtr.Zero) return null;
        try
        {
            using var icon = Icon.FromHandle(icons[0]);
            return icon.ToBitmap();
        }
        finally { NativeMethods.DestroyIcon(icons[0]); }
    }

    public static Bitmap? FromParsingName(string name, int size)
    {
        NativeMethods.SHCreateItemFromParsingName(name, IntPtr.Zero, typeof(NativeMethods.IShellItemImageFactory).GUID, out var factory);
        try
        {
            var hr = factory.GetImage(new NativeMethods.SIZE(size, size), NativeMethods.SIIGBF.IconOnly | NativeMethods.SIIGBF.BiggerSizeOk, out var hbm);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try { return FromHBitmap(hbm); }
            finally { NativeMethods.DeleteObject(hbm); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    /// <summary>HBITMAP (DIB-секция 32 бит, premultiplied alpha) → Bitmap с альфой.</summary>
    private static Bitmap? FromHBitmap(IntPtr hbm)
    {
        var ds = new NativeMethods.DIBSECTION();
        if (NativeMethods.GetObject(hbm, Marshal.SizeOf<NativeMethods.DIBSECTION>(), ref ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32)
            return Image.FromHbitmap(hbm);
        int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
        bool bottomUp = ds.dsBmih.biHeight > 0;
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new byte[stride];
        var outRow = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            int srcY = bottomUp ? h - 1 - y : y;
            Marshal.Copy(ds.dsBm.bmBits + srcY * stride, row, 0, stride);
            for (int x = 0; x < w; x++)
            {
                byte b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2], a = row[x * 4 + 3];
                if (a > 0 && a < 255)
                {
                    // снимаем premultiply
                    b = (byte)Math.Min(255, b * 255 / a);
                    g = (byte)Math.Min(255, g * 255 / a);
                    r = (byte)Math.Min(255, r * 255 / a);
                }
                outRow[x * 4] = b; outRow[x * 4 + 1] = g; outRow[x * 4 + 2] = r; outRow[x * 4 + 3] = a;
            }
            Marshal.Copy(outRow, 0, data.Scan0 + y * data.Stride, w * 4);
        }
        result.UnlockBits(data);
        return result;
    }
}
