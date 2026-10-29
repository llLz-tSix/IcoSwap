using Microsoft.Win32;

namespace IcoSwap.Core.Targets;

/// <summary>Определение ProgID расширения так же, как это делает Проводник (UserChoice → HKCR).</summary>
public static class FileTypeRegistry
{
    private const string FileExts = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\";

    public static string NormalizeExt(string ext)
    {
        ext = ext.Trim().ToLowerInvariant();
        return ext.StartsWith('.') ? ext : "." + ext;
    }

    public static bool IsRegistered(string ext)
    {
        using var k = Registry.ClassesRoot.OpenSubKey(NormalizeExt(ext));
        return k != null;
    }

    public static string? GetProgId(string ext)
    {
        ext = NormalizeExt(ext);
        string? userChoice = null;
        using (var latest = Registry.CurrentUser.OpenSubKey(FileExts + ext + @"\UserChoiceLatest\ProgId"))
            userChoice = latest?.GetValue("ProgId") as string;
        if (string.IsNullOrEmpty(userChoice))
            using (var uc = Registry.CurrentUser.OpenSubKey(FileExts + ext + @"\UserChoice"))
                userChoice = uc?.GetValue("ProgId") as string;
        if (!string.IsNullOrEmpty(userChoice) && KeyExists(userChoice)) return userChoice;

        using var extKey = Registry.ClassesRoot.OpenSubKey(ext);
        var progId = extKey?.GetValue(null) as string;
        return !string.IsNullOrEmpty(progId) && KeyExists(progId) ? progId : null;
    }

    /// <summary>Текущее значение DefaultIcon для расширения (через ProgID или само расширение).</summary>
    public static string? GetDefaultIcon(string ext)
    {
        var progId = GetProgId(ext);
        using var k = Registry.ClassesRoot.OpenSubKey((progId ?? NormalizeExt(ext)) + @"\DefaultIcon");
        return k?.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    /// <summary>Иконка зависит от содержимого файла (%1 или IconHandler) — смена DefaultIcon не сработает.</summary>
    public static bool IsPerFileIcon(string ext)
    {
        var icon = GetDefaultIcon(ext);
        if (icon != null && icon.Trim().Trim('"').StartsWith("%1")) return true;
        var progId = GetProgId(ext);
        if (progId == null) return false;
        using var h = Registry.ClassesRoot.OpenSubKey(progId + @"\ShellEx\IconHandler");
        return h != null;
    }

    /// <summary>Ключ HKCU\Software\Classes\..., куда пишется DefaultIcon для расширения.</summary>
    public static string GetUserIconKeyPath(string ext)
    {
        var progId = GetProgId(ext);
        return @"Software\Classes\" + (progId ?? NormalizeExt(ext)) + @"\DefaultIcon";
    }

    private static bool KeyExists(string sub)
    {
        using var k = Registry.ClassesRoot.OpenSubKey(sub);
        return k != null;
    }

    private static Dictionary<string, List<string>>? _byProgId;

    /// <summary>Другие расширения с тем же ProgID — смена иконки затронет и их.</summary>
    public static IReadOnlyList<string> SharedExtensions(string ext)
    {
        ext = NormalizeExt(ext);
        var progId = GetProgId(ext);
        if (progId == null) return Array.Empty<string>();
        if (_byProgId == null)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in Registry.ClassesRoot.GetSubKeyNames())
            {
                if (name.Length < 2 || name[0] != '.') continue;
                string? p;
                try { p = GetProgId(name); } catch { continue; }
                if (p == null) continue;
                if (!map.TryGetValue(p, out var list)) map[p] = list = new List<string>();
                list.Add(name.ToLowerInvariant());
            }
            _byProgId = map;
        }
        return _byProgId.TryGetValue(progId, out var l) ? l.Where(e => e != ext).ToList() : Array.Empty<string>();
    }
}
