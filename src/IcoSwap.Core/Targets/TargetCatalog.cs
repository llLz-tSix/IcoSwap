using IcoSwap.Core.Matching;
using IcoSwap.Core.Native;
using Microsoft.Win32;

namespace IcoSwap.Core.Targets;

/// <summary>Всё, у чего на этой машине можно сменить иконку.</summary>
public sealed class TargetCatalog
{
    public List<IconTarget> Targets { get; } = new();

    private readonly Dictionary<string, IconTarget> _fileTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Генераторы для поиска расширения в имени иконки: "txt", "file-txt", "application-pdf"...</summary>
    private static readonly string[] ExtPrefixes = { "file", "ext", "type", "filetype", "application", "text", "image", "audio", "video", "doc", "файл" };
    private static readonly string[] ExtSuffixes = { "file", "files", "document", "файл" };

    /// <summary>Типичные exe-лаунчеры, имя которых ничего не говорит о программе.</summary>
    private static readonly HashSet<string> GenericExe = new(StringComparer.OrdinalIgnoreCase)
    {
        "update", "launcher", "setup", "install", "installer", "uninstall", "uninst", "unins000", "unins001", "rundll32",
        "msiexec", "helper", "app", "start", "run", "main", "client", "bin", "squirrel", "javaw", "java",
    };

    /// <summary>Программы-«хосты»: с аргументами запускают что-то другое.</summary>
    private static readonly HashSet<string> HostExe = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "cmd", "powershell", "pwsh", "wscript", "cscript", "mmc", "conhost", "wt", "msedge", "chrome", "chrome_proxy",
        "msedge_proxy", "control", "python", "pythonw",
    };

    public static TargetCatalog Discover(IProgress<string>? progress = null)
    {
        var c = new TargetCatalog();
        progress?.Report("Системные значки…");
        c.AddSystemIcons();
        c.AddKnownFolders();
        c.AddShellFolderIcons();
        progress?.Report("Диски…");
        c.AddDrives();
        progress?.Report("Ярлыки…");
        c.AddShortcuts();
        return c;
    }

    // ---------------- системные ----------------

    private void AddSystemIcons()
    {
        foreach (var s in SystemIcons.All)
            Targets.Add(new IconTarget
            {
                Id = "sys:" + s.Key,
                Kind = TargetKind.SystemIcon,
                DisplayName = s.Title,
                Location = "Рабочий стол / Проводник",
                Aliases = new[] { new NameAlias(Synonyms.Groups[s.Synonym][0]) },
            });
    }

    private void AddKnownFolders()
    {
        foreach (var kf in SystemIcons.KnownFolders)
        {
            var path = SystemIcons.GetKnownFolderPath(kf);
            if (path == null || !Directory.Exists(path)) continue;
            Targets.Add(new IconTarget
            {
                Id = "kf:" + kf.Key,
                Kind = TargetKind.KnownFolder,
                DisplayName = kf.Title,
                Location = path,
                Path = path,
                Aliases = new[] { new NameAlias(Synonyms.Groups[kf.Synonym][0]) },
            });
        }
    }

    private void AddShellFolderIcons()
    {
        bool admin = !CanWriteHklm(SystemIcons.ShellIconsKey);
        const string note = "Меняет иконку всех обычных папок. Нужны права администратора и перезапуск Проводника.";
        Targets.Add(new IconTarget
        {
            Id = "shell:folder", Kind = TargetKind.ShellIcon, DisplayName = "Папка (все папки)",
            Aliases = new[] { new NameAlias(Synonyms.Groups[Synonyms.Folder][0]) }, RequiresAdmin = admin, Note = note,
        });
        Targets.Add(new IconTarget
        {
            Id = "shell:folder-open", Kind = TargetKind.ShellIcon, DisplayName = "Открытая папка (все папки)",
            Aliases = new[] { new NameAlias(Synonyms.Groups[Synonyms.FolderOpen][0]) }, RequiresAdmin = admin, Note = note,
        });
    }

    private void AddDrives()
    {
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.Substring(0, 1).ToUpperInvariant();
        foreach (var d in DriveInfo.GetDrives())
        {
            var letter = d.Name.Substring(0, 1).ToUpperInvariant();
            var aliases = new List<NameAlias>();
            foreach (var pattern in new[] { "drive {0}", "{0} drive", "disk {0}", "{0} disk", "диск {0}", "local disk {0}", "локальный диск {0}", "hdd {0}", "drive {0}:" })
                aliases.Add(new NameAlias(string.Format(pattern, letter.ToLowerInvariant()), 1.0, ExactOnly: true));
            switch (d.DriveType)
            {
                case DriveType.Fixed:
                    aliases.Add(new NameAlias(Synonyms.Groups[Synonyms.Drive][0], 0.95));
                    if (letter == systemDrive) aliases.Add(new NameAlias(Synonyms.Groups[Synonyms.SystemDrive][0], 0.98));
                    break;
                case DriveType.Removable:
                    aliases.Add(new NameAlias(Synonyms.Groups[Synonyms.RemovableDrive][0], 0.95));
                    break;
                case DriveType.CDRom:
                    aliases.Add(new NameAlias(Synonyms.Groups[Synonyms.OpticalDrive][0], 0.95));
                    break;
                default:
                    continue;
            }
            string label = "";
            try { if (d.IsReady) label = d.VolumeLabel; } catch { }
            bool bitLocker = IsBitLockerProtected(d.Name);
            Targets.Add(new IconTarget
            {
                Id = "drive:" + letter,
                Kind = TargetKind.Drive,
                DisplayName = $"Диск {letter}:" + (string.IsNullOrEmpty(label) ? "" : $" ({label})"),
                Location = d.DriveType switch { DriveType.Removable => "Съёмный", DriveType.CDRom => "Оптический", _ => "Локальный" },
                Path = d.Name,
                Aliases = aliases,
                Note = bitLocker ? "Диск зашифрован BitLocker: Проводник показывает для него свою иконку с замком, новая иконка не отобразится." : null,
                DefaultEnabled = !bitLocker,
            });
        }
    }

    /// <summary>System.Volume.BitLockerProtection: 1 — защита включена (читается без прав администратора).</summary>
    private static bool IsBitLockerProtected(string root)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return false;
            dynamic shell = Activator.CreateInstance(type)!;
            var ns = shell.NameSpace(root);
            if (ns == null) return false;
            object? v = ns.Self.ExtendedProperty("System.Volume.BitLockerProtection");
            return v != null && Convert.ToInt32(v) == 1;
        }
        catch { return false; }
    }

    // ---------------- ярлыки ----------------

    private void AddShortcuts()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var roots = new (string? Path, string Label, bool Recursive)[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Рабочий стол", false),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Рабочий стол (все пользователи)", false),
            (Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"), "Панель задач", false),
            (Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Меню «Пуск»", true),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Меню «Пуск» (все пользователи)", true),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, label, recursive) in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", new EnumerationOptions
                {
                    RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = 0,
                }).Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                  .ToList();
            }
            catch { continue; }
            foreach (var file in files)
            {
                if (!seen.Add(file)) continue;
                var t = CreateShortcutTarget(file, label);
                if (t != null) Targets.Add(t);
            }
        }
    }

    public static IconTarget? CreateShortcutTarget(string file, string? label = null)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var aliases = new List<NameAlias> { new(name) };
        string? note = null;
        if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var link = ShellLink.Open(file);
                var target = link.TargetPath;
                if (!string.IsNullOrEmpty(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var exe = Path.GetFileNameWithoutExtension(target);
                    var args = link.Arguments;
                    // explorer.exe <папка>, cmd /k ..., msedge --app-id=... — exe ничего не говорит о самом ярлыке
                    bool hosted = args.Length > 0 && (HostExe.Contains(exe) || args.Contains("--app-id", StringComparison.OrdinalIgnoreCase));
                    if (!GenericExe.Contains(exe) && !hosted) aliases.Add(new NameAlias(exe));
                }
                if (string.IsNullOrEmpty(target)) note = "Ярлык приложения Microsoft Store: значок в «Пуске» может браться из самого приложения.";
            }
            catch { /* битый ярлык — сопоставляем только по имени */ }
        }
        return new IconTarget
        {
            Id = "lnk:" + file,
            Kind = TargetKind.Shortcut,
            DisplayName = name,
            Location = label ?? Path.GetDirectoryName(file),
            Path = file,
            Aliases = aliases,
            RequiresAdmin = !CanWriteFile(file),
            Note = note,
        };
    }

    // ---------------- типы файлов ----------------

    /// <summary>Расширения, которые могут подразумеваться именем иконки ("txt", "file-pdf", "image-png").</summary>
    public static IEnumerable<string> ExtensionCandidates(NameInfo name)
    {
        var t = name.Tokens;
        if (t.Count == 1) yield return t[0];
        if (t.Count == 2)
        {
            if (ExtPrefixes.Contains(t[0])) yield return t[1];
            if (ExtSuffixes.Contains(t[1])) yield return t[0];
        }
        if (t.Count == 3 && ExtPrefixes.Contains(t[0]) && ExtSuffixes.Contains(t[2])) yield return t[1];
    }

    public IconTarget? GetFileType(string ext)
    {
        ext = FileTypeRegistry.NormalizeExt(ext);
        if (ext.Length < 2 || ext.Length > 16 || ext.Skip(1).Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')) return null;
        if (_fileTypes.TryGetValue(ext, out var cached)) return cached;
        IconTarget? result = null;
        if (FileTypeRegistry.IsRegistered(ext) && !FileTypeRegistry.IsPerFileIcon(ext))
        {
            var progId = FileTypeRegistry.GetProgId(ext);
            var shared = FileTypeRegistry.SharedExtensions(ext);
            string? note = null;
            if (shared.Count > 0)
                note = "Общий тип с: " + string.Join(", ", shared.Take(8)) + (shared.Count > 8 ? $" и ещё {shared.Count - 8}" : "");
            result = new IconTarget
            {
                Id = "ext:" + ext,
                Kind = TargetKind.FileType,
                DisplayName = ext,
                Location = progId != null ? "Тип: " + progId : null,
                Aliases = new[] { new NameAlias(ext.TrimStart('.'), 1.0, ExactOnly: true) },
                Note = note,
            };
        }
        _fileTypes[ext] = result!;
        return result;
    }

    /// <summary>Создаёт цель для произвольной папки (ручное назначение или manifest "path:").</summary>
    public static IconTarget CreateFolderTarget(string path) => new()
    {
        Id = "dir:" + path,
        Kind = TargetKind.Folder,
        DisplayName = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path,
        Location = path,
        Path = path,
    };

    public IconTarget? FindById(string id)
    {
        var t = Targets.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (t != null) return t;
        if (id.StartsWith("ext:", StringComparison.OrdinalIgnoreCase)) return GetFileType(id[4..]);
        if (id.StartsWith("dir:", StringComparison.OrdinalIgnoreCase) && Directory.Exists(id[4..])) return CreateFolderTarget(id[4..]);
        if (id.StartsWith("lnk:", StringComparison.OrdinalIgnoreCase) && File.Exists(id[4..])) return CreateShortcutTarget(id[4..]);
        return null;
    }

    // ---------------- права ----------------

    private static bool CanWriteFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
        catch { return true; } // занят и т.п. — не повод требовать админа
    }

    private static bool CanWriteHklm(string key)
    {
        // Проверяем права на родительский ключ Explorer, ничего не создавая
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(Path.GetDirectoryName(key)!, writable: true);
            return k != null;
        }
        catch { return false; }
    }
}
