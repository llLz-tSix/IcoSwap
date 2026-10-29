using System.Runtime.InteropServices;
using System.Text;
using IcoSwap.Core.Native;
using IcoSwap.Core.Targets;
using Microsoft.Win32;

namespace IcoSwap.Core.Apply;

/// <summary>Контекст применения: запись бэкапа и сохранение его на диск до любого изменения.</summary>
internal sealed class ApplyContext
{
    public required BackupRecord Record { get; init; }
    public required Action Persist { get; init; }
    public HashSet<string> ChangedItems { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ChangedDirs { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Сначала запоминает исходное значение (и сохраняет бэкап на диск), потом пишет новое.</summary>
    public void WriteRegistry(string hive, string keyPath, string valueName, string data)
    {
        if (RegistryBackup.EnsureCaptured(Record, hive, keyPath, valueName)) Persist();
        RegistryBackup.WriteString(hive, keyPath, valueName, data);
    }
}

internal interface ITargetHandler
{
    bool CanHandle(string targetId);
    void Apply(string targetId, string icoPath, ApplyContext ctx);
    void Restore(BackupRecord rec, Func<RegValueBackup, bool> keyStillUsed, ApplyContext ctx);
}

internal static class Handlers
{
    public static readonly ITargetHandler[] All =
    {
        new ShortcutHandler(), new FileTypeHandler(), new SystemIconHandler(), new FolderHandler(),
        new DriveHandler(), new ShellIconHandler(),
    };

    public static ITargetHandler For(string id) =>
        All.FirstOrDefault(h => h.CanHandle(id)) ?? throw new NotSupportedException("Неизвестный тип цели: " + id);

    public static string IconRef(string icoPath) => icoPath + ",0";

    public static void RestoreRegistry(BackupRecord rec, Func<RegValueBackup, bool> keyStillUsed,
        Func<RegValueBackup, (string, RegistryValueKind)?>? fallback = null)
    {
        if (rec.Registry == null) return;
        // В обратном порядке: сначала значения, записанные последними
        foreach (var b in Enumerable.Reverse(rec.Registry))
            RegistryBackup.Restore(b, keyStillUsed, fallback);
    }

    public static FileBackup CaptureFile(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) return new FileBackup { Path = path, Existed = false };
        return new FileBackup
        {
            Path = path,
            Existed = true,
            ContentBase64 = Convert.ToBase64String(File.ReadAllBytes(path)),
            Attributes = (int)fi.Attributes,
            LastWriteUtc = fi.LastWriteTimeUtc,
        };
    }

    public static void RestoreFile(FileBackup b)
    {
        if (b.Existed)
        {
            if (File.Exists(b.Path)) File.SetAttributes(b.Path, FileAttributes.Normal);
            File.WriteAllBytes(b.Path, Convert.FromBase64String(b.ContentBase64 ?? ""));
            File.SetLastWriteTimeUtc(b.Path, b.LastWriteUtc);
            File.SetAttributes(b.Path, (FileAttributes)b.Attributes);
        }
        else if (File.Exists(b.Path))
        {
            File.SetAttributes(b.Path, FileAttributes.Normal);
            File.Delete(b.Path);
        }
    }
}

/// <summary>Ярлыки .lnk (IShellLink) и интернет-ярлыки .url (ключ IconFile). Бэкап — файл целиком.</summary>
internal sealed class ShortcutHandler : ITargetHandler
{
    public bool CanHandle(string id) => id.StartsWith("lnk:", StringComparison.OrdinalIgnoreCase);

    public void Apply(string id, string icoPath, ApplyContext ctx)
    {
        var path = id[4..];
        if (!File.Exists(path)) throw new FileNotFoundException("Ярлык не найден", path);
        if (ctx.Record.File == null)
        {
            ctx.Record.File = Handlers.CaptureFile(path);
            ctx.Persist();
        }
        var attrs = File.GetAttributes(path);
        if (attrs.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        try
        {
            if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                if (!NativeMethods.WritePrivateProfileString("InternetShortcut", "IconFile", icoPath, path) ||
                    !NativeMethods.WritePrivateProfileString("InternetShortcut", "IconIndex", "0", path))
                    throw new IOException($"Не удалось записать .url (код {Marshal.GetLastWin32Error()})");
            }
            else
            {
                using var link = ShellLink.Open(path, write: true);
                link.IconLocation = (icoPath, 0);
                link.Save();
            }
        }
        finally
        {
            if (attrs.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attrs);
        }
        ctx.ChangedItems.Add(path);
    }

    public void Restore(BackupRecord rec, Func<RegValueBackup, bool> keyStillUsed, ApplyContext ctx)
    {
        if (rec.File == null) return;
        Handlers.RestoreFile(rec.File);
        ctx.ChangedItems.Add(rec.File.Path);
    }
}

/// <summary>Типы файлов: HKCU\Software\Classes\&lt;ProgID&gt;\DefaultIcon (без прав администратора).</summary>
internal sealed class FileTypeHandler : ITargetHandler
{
    public bool CanHandle(string id) => id.StartsWith("ext:", StringComparison.OrdinalIgnoreCase);

    public void Apply(string id, string icoPath, ApplyContext ctx)
    {
        var keyPath = FileTypeRegistry.GetUserIconKeyPath(id[4..]);
        ctx.WriteRegistry("HKCU", keyPath, "", Handlers.IconRef(icoPath));
    }

    public void Restore(BackupRecord rec, Func<RegValueBackup, bool> keyStillUsed, ApplyContext ctx) =>
        Handlers.RestoreRegistry(rec, keyStillUsed);
}

/// <summary>Системные значки: HKCU\...\Explorer\CLSID\{clsid}\DefaultIcon.</summary>
internal sealed class SystemIconHandler : ITargetHandler
{
    private const string Base = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\";

    public bool CanHandle(string id) => id.StartsWith("sys:", StringComparison.OrdinalIgnoreCase);

    public void Apply(string id, string icoPath, ApplyContext ctx)
    {
        var def = SystemIcons.Find(id[4..]) ?? throw new NotSupportedException("Неизвестный системный значок: " + id);
        var icon = Handlers.IconRef(icoPath);
        foreach (var (clsid, valueName) in def.Values)
        {
            var key = Base + clsid + @"\DefaultIcon";
            if (clsid == SystemIcons.RecycleBinClsid)
            {
                // Чтобы ключ HKCU не "заслонил" недостающие значения, переносим их из HKCR
                foreach (var v in new[] { "", "empty", "full" })
                {
                    if (RegistryBackup.ReadString("HKCU", key, v) != null) continue;
                    var sys = SystemDefault(clsid, v);
                    if (sys != null) ctx.WriteRegistry("HKCU", key, v, Environment.ExpandEnvironmentVariables(sys));
                }
                ctx.WriteRegistry("HKCU", key, valueName, icon);
                // Текущий вид корзины ("" = то, что показано сейчас)
                bool full = IsRecycleBinFull();
                if ((valueName == "full") == full) ctx.WriteRegistry("HKCU", key, "", icon);
            }
            else
            {
                ctx.WriteRegistry("HKCU", key, valueName, icon);
            }
        }
    }

    public void Restore(BackupRecord rec, Func<RegValueBackup, bool> keyStillUsed, ApplyContext ctx) =>
        Handlers.RestoreRegistry(rec, keyStillUsed, b =>
        {
            var clsid = b.KeyPath.Split('\\').Reverse().Skip(1).First();
            var v = SystemDefault(clsid, b.ValueName);
            return v != null ? (v, RegistryValueKind.ExpandString) : null;
        });

    internal static string? SystemDefault(string clsid, string valueName)
    {
        using var k = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid + @"\DefaultIcon");
        return k?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    // На x64 структура выровнена естественно (24 байта); с Pack=4 функция возвращала ошибку
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    private static bool IsRecycleBinFull()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 && info.i64NumItems > 0;
    }
}

/// <summary>Папки (особые и любые): IconResource в desktop.ini + атрибут ReadOnly у папки.</summary>
internal sealed class FolderHandler : ITargetHandler
{
    public bool CanHandle(string id) =>
        id.StartsWith("kf:", StringComparison.OrdinalIgnoreCase) || id.StartsWith("dir:", StringComparison.OrdinalIgnoreCase);

    public static string ResolveFolder(string id)
    {
        if (id.StartsWith("dir:", StringComparison.OrdinalIgnoreCase)) return id[4..];
        var def = SystemIcons.FindKnownFolder(id[3..]) ?? throw new NotSupportedException("Неизвестная папка: " + id);
        return SystemIcons.GetKnownFolderPath(def) ?? throw new DirectoryNotFoundException(def.Title);
    }

    public void Apply(string id, string icoPath, ApplyContext ctx)
    {
        var folder = ResolveFolder(id);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        var ini = Path.Combine(folder, "desktop.ini");
        if (ctx.Record.File == null)
        {
            ctx.Record.File = Handlers.CaptureFile(ini);
            ctx.Record.FolderPath = folder;
            ctx.Record.FolderAttributes = (int)File.GetAttributes(folder);
            ctx.Persist();
        }

        string text = "";
        if (File.Exists(ini)) text = DecodeIni(File.ReadAllBytes(ini));
        text = DesktopIni.SetIcon(text, icoPath + ",0");
        if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
        File.WriteAllText(ini, text, Encoding.Unicode); // UTF-16 LE с BOM — Проводник это понимает
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
        var fa = File.GetAttributes(folder);
        if (!fa.HasFlag(FileAttributes.ReadOnly) && !fa.HasFlag(FileAttributes.System))
            File.SetAttributes(folder, fa | FileAttributes.ReadOnly);
        ctx.ChangedDirs.Add(folder);
    }

    public void Restore(BackupRecord rec, Func<RegValueBackup, bool> keyStillUsed, ApplyContext ctx)
    {
        if (rec.File != null) Handlers.RestoreFile(rec.File);
        if (rec.FolderPath != null && rec.FolderAttributes != null && Directory.Exists(rec.FolderPath))
        {
            File.SetAttributes(rec.FolderPath, (FileAttributes)rec.FolderAttributes.Value);
            ctx.ChangedDirs.Add(rec.FolderPath);
        }
    }

    internal static string DecodeIni(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        // без BOM desktop.ini в кодировке ANSI
        var ansi = Encoding.GetEncoding((int)GetACP());
        return ansi.GetString(data);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    static FolderHandler()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}

/// <summary>Диски: HKCU\Software\Classes\Applications\Explorer.exe\Drives\X\DefaultIcon.</summary>
internal sealed class DriveHandler : ITargetHandler
{
    public bool CanHandle(string id) => id.StartsWith("drive:", StringComparison.OrdinalIgnoreCase);

    public static string KeyPath(string letter) => @"Software\Classes\Applications\Explorer.exe\Drives\" + letter.ToUpperInvariant() + @"\DefaultIcon";

    public void Apply(string id, string icoPath, ApplyContext ctx)
    {
        var letter = id[6..].TrimEnd(':', '\\');
        ctx.WriteRegistry("HKCU", KeyPath(letter), "", Handlers.IconRef(icoPath));
        ctx.ChangedItems.Add(letter + ":\\");
    }

    public void Restore(BackupRecord rec, Func<RegValueBackup, bool> keyStillUsed, ApplyContext ctx)
    {
        Handlers.RestoreRegistry(rec, keyStillUsed);
        ctx.ChangedItems.Add(rec.TargetId[6..].TrimEnd(':', '\\') + ":\\");
    }
}

/// <summary>Иконка всех папок: HKLM\...\Explorer\Shell Icons (3 — закрытая, 4 — открытая). Требует админа.</summary>
internal sealed class ShellIconHandler : ITargetHandler
{
    public bool CanHandle(string id) => id.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    public void Apply(string id, string icoPath, ApplyContext ctx)
    {
        var value = id.Equals("shell:folder-open", StringComparison.OrdinalIgnoreCase) ? "4" : "3";
        ctx.WriteRegistry("HKLM", SystemIcons.ShellIconsKey, value, Handlers.IconRef(icoPath));
    }

    public void Restore(BackupRecord rec, Func<RegValueBackup, bool> keyStillUsed, ApplyContext ctx) =>
        Handlers.RestoreRegistry(rec, keyStillUsed);
}

/// <summary>Минимальная правка desktop.ini с сохранением остальных строк.</summary>
internal static class DesktopIni
{
    public static string SetIcon(string text, string iconResource)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        int section = lines.FindIndex(l => l.Trim().Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase));
        if (section < 0)
        {
            lines.Insert(0, "[.ShellClassInfo]");
            if (lines.Count > 1) lines.Insert(1, "");
            section = 0;
        }
        int end = lines.FindIndex(section + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        for (int i = end - 1; i > section; i--)
        {
            var key = lines[i].Split('=')[0].Trim();
            if (key.Equals("IconResource", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("IconFile", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("IconIndex", StringComparison.OrdinalIgnoreCase))
            {
                lines.RemoveAt(i);
                end--;
            }
        }
        // вставляем после последней непустой строки секции
        int insertAt = end;
        while (insertAt > section + 1 && lines[insertAt - 1].Trim().Length == 0) insertAt--;
        lines.Insert(insertAt, "IconResource=" + iconResource);
        return string.Join("\r\n", lines) + "\r\n";
    }
}
