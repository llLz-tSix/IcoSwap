using System.Runtime.InteropServices;
using IcoSwap.Core.Native;

namespace IcoSwap.Core.Icons;

/// <summary>
/// Извлекает иконки из библиотек .icl/.dll: 32-битные PE (через LoadLibraryEx как data file,
/// код не исполняется) и старые 16-битные NE .icl (разбор файла вручную).
/// </summary>
public static class IconLibraryReader
{
    /// <param name="Name">Имя ресурса (если строковое) или null.</param>
    /// <param name="Index">Порядковый номер группы иконок в библиотеке, с 0.</param>
    public sealed record LibraryIcon(int Index, string? Name, byte[] IcoData);

    public static List<LibraryIcon> Extract(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 64 || bytes[0] != 'M' || bytes[1] != 'Z')
            throw new InvalidDataException("Файл не является библиотекой иконок (нет заголовка MZ)");
        int hdr = BitConverter.ToInt32(bytes, 0x3C);
        if (hdr > 0 && hdr + 2 < bytes.Length && bytes[hdr] == 'N' && bytes[hdr + 1] == 'E')
            return ExtractNe(bytes, hdr);
        return ExtractPe(path);
    }

    // ---------------- PE ----------------

    private static List<LibraryIcon> ExtractPe(string path)
    {
        var module = NativeMethods.LoadLibraryEx(path, IntPtr.Zero,
            NativeMethods.LOAD_LIBRARY_AS_DATAFILE | NativeMethods.LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (module == IntPtr.Zero)
            throw new InvalidDataException($"Не удалось открыть библиотеку (код {Marshal.GetLastWin32Error()})");
        try
        {
            var names = new List<IntPtr>();
            var nameStrings = new List<string?>();
            NativeMethods.EnumResourceNames(module, NativeMethods.RT_GROUP_ICON, (_, _, name, _) =>
            {
                if (IsIntResource(name))
                {
                    names.Add(name);
                    nameStrings.Add(null);
                }
                else
                {
                    var s = Marshal.PtrToStringUni(name);
                    nameStrings.Add(s);
                    names.Add(Marshal.StringToHGlobalUni(s)); // строка живёт только внутри колбэка
                }
                return true;
            }, IntPtr.Zero);

            var result = new List<LibraryIcon>();
            for (int i = 0; i < names.Count; i++)
            {
                try
                {
                    var grp = ReadResource(module, names[i], NativeMethods.RT_GROUP_ICON);
                    if (grp == null) continue;
                    var ico = BuildIco(grp, id => ReadResource(module, id, NativeMethods.RT_ICON));
                    if (ico != null) result.Add(new LibraryIcon(i, nameStrings[i], ico));
                }
                finally
                {
                    if (nameStrings[i] != null) Marshal.FreeHGlobal(names[i]);
                }
            }
            return result;
        }
        finally { NativeMethods.FreeLibrary(module); }
    }

    private static bool IsIntResource(IntPtr p) => ((ulong)p >> 16) == 0;

    private static byte[]? ReadResource(IntPtr module, IntPtr name, IntPtr type)
    {
        var info = NativeMethods.FindResource(module, name, type);
        if (info == IntPtr.Zero) return null;
        var size = NativeMethods.SizeofResource(module, info);
        var handle = NativeMethods.LoadResource(module, info);
        var ptr = NativeMethods.LockResource(handle);
        if (ptr == IntPtr.Zero || size == 0) return null;
        var data = new byte[size];
        Marshal.Copy(ptr, data, 0, (int)size);
        return data;
    }

    /// <summary>Собирает .ico из GRPICONDIR (записи по 14 байт) и отдельных RT_ICON.</summary>
    internal static byte[]? BuildIco(byte[] grp, Func<IntPtr, byte[]?> loadIcon)
    {
        if (grp.Length < 6) return null;
        int count = BitConverter.ToUInt16(grp, 4);
        var entries = new List<IcoFile.Entry>();
        for (int i = 0; i < count; i++)
        {
            int off = 6 + i * 14;
            if (off + 14 > grp.Length) break;
            int w = grp[off] == 0 ? 256 : grp[off];
            int h = grp[off + 1] == 0 ? 256 : grp[off + 1];
            int bpp = BitConverter.ToUInt16(grp, off + 6);
            int id = BitConverter.ToUInt16(grp, off + 12);
            var data = loadIcon(id);
            if (data == null || data.Length == 0) continue;
            entries.Add(new IcoFile.Entry(w, h, bpp, data));
        }
        return entries.Count == 0 ? null : IcoFile.WriteRaw(entries);
    }

    // ---------------- NE (16-битные .icl) ----------------

    private const ushort NE_RT_ICON = 0x8003;
    private const ushort NE_RT_GROUP_ICON = 0x800E;

    private static List<LibraryIcon> ExtractNe(byte[] f, int ne)
    {
        int resTable = ne + BitConverter.ToUInt16(f, ne + 0x24);
        int shift = BitConverter.ToUInt16(f, resTable);
        int p = resTable + 2;

        var icons = new Dictionary<int, byte[]>();
        var groups = new List<(int id, string? name, byte[] data)>();

        while (p + 2 <= f.Length)
        {
            ushort typeId = BitConverter.ToUInt16(f, p);
            if (typeId == 0) break;
            int count = BitConverter.ToUInt16(f, p + 2);
            p += 8;
            for (int i = 0; i < count; i++, p += 12)
            {
                int offset = BitConverter.ToUInt16(f, p) << shift;
                int length = BitConverter.ToUInt16(f, p + 2) << shift;
                ushort rid = BitConverter.ToUInt16(f, p + 6);
                if (offset <= 0 || offset + length > f.Length) continue;
                var data = f.AsSpan(offset, length).ToArray();
                if (typeId == NE_RT_ICON && (rid & 0x8000) != 0)
                    icons[rid & 0x7FFF] = data;
                else if (typeId == NE_RT_GROUP_ICON)
                {
                    string? name = null;
                    if ((rid & 0x8000) == 0)
                    {
                        int so = resTable + rid;
                        if (so < f.Length)
                        {
                            int len = f[so];
                            if (so + 1 + len <= f.Length)
                                name = System.Text.Encoding.Latin1.GetString(f, so + 1, len);
                        }
                    }
                    groups.Add((rid & 0x7FFF, name, data));
                }
            }
        }

        var result = new List<LibraryIcon>();
        for (int i = 0; i < groups.Count; i++)
        {
            var ico = BuildIco(groups[i].data, id => icons.TryGetValue((int)id, out var d) ? d : null);
            if (ico != null) result.Add(new LibraryIcon(i, groups[i].name, ico));
        }
        return result;
    }
}
