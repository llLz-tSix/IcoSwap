using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IcoSwap.Core.Icons;

namespace IcoSwap.Core.Packs;

/// <summary>
/// Импорт пака: папка, zip или одиночный .icl/.dll. Все иконки конвертируются в .ico
/// и копируются в %APPDATA%\IcoSwap\packs\&lt;имя&gt;_&lt;хэш&gt;, чтобы пак работал и после удаления исходника.
/// </summary>
public static class PackLoader
{
    private static readonly string[] IcoExt = { ".ico" };
    private static readonly string[] RasterExt = { ".png", ".bmp", ".jpg", ".jpeg", ".gif", ".tif", ".tiff" };
    private static readonly string[] SvgExt = { ".svg" };
    private static readonly string[] LibExt = { ".icl", ".dll" };
    private const int PreviewSize = 96;
    private const string IndexFile = "pack.json";

    public static bool IsSupportedFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".zip" || IcoExt.Contains(ext) || RasterExt.Contains(ext) || SvgExt.Contains(ext) || LibExt.Contains(ext);
    }

    public static IconPack Import(string sourcePath, IProgress<string>? progress = null)
    {
        string? tempDir = null;
        try
        {
            string root;
            List<string> files;
            if (File.Exists(sourcePath) && Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report("Распаковка архива…");
                tempDir = Path.Combine(AppPaths.Temp, "unzip_" + Guid.NewGuid().ToString("N"));
                ZipFile.ExtractToDirectory(sourcePath, tempDir); // защищает от выхода за пределы папки
                root = SingleSubfolderOrSelf(tempDir);
                files = EnumerateFiles(root);
            }
            else if (File.Exists(sourcePath))
            {
                root = Path.GetDirectoryName(sourcePath)!;
                files = new List<string> { sourcePath };
                var mf = Path.Combine(root, "manifest.json");
                if (File.Exists(mf)) files.Add(mf);
            }
            else if (Directory.Exists(sourcePath))
            {
                root = sourcePath;
                files = EnumerateFiles(root);
            }
            else throw new FileNotFoundException("Пак не найден", sourcePath);

            var warnings = new List<string>();
            PackManifest? manifest = null;
            var manifestPath = files.FirstOrDefault(f => Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
                                                        && Path.GetDirectoryName(f)!.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (manifestPath != null)
            {
                try { manifest = PackManifest.Parse(File.ReadAllText(manifestPath)); }
                catch (Exception ex) { warnings.Add($"manifest.json не прочитан: {ex.Message}"); }
            }

            var iconFiles = files.Where(f => IsSupportedFile(f) && !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            if (iconFiles.Count == 0) throw new InvalidDataException("В выбранном месте нет иконок (.ico, .png, .svg, .icl, .dll)");

            string baseName = manifest?.Name
                ?? (File.Exists(sourcePath) ? Path.GetFileNameWithoutExtension(sourcePath) : new DirectoryInfo(sourcePath).Name);
            string hash = ComputeHash(root, iconFiles, manifestPath);
            // В паке со светлым и тёмным вариантами берём вариант под текущую тему; копия пака — отдельная на каждый вариант
            bool preferDark = SystemUsesDarkTheme();
            bool hasVariants = iconFiles.Any(f => VariantRank(Path.GetRelativePath(root, f), preferDark) != 1);
            string variant = hasVariants ? (preferDark ? "_dark" : "_light") : "";
            string destDir = Path.Combine(AppPaths.Packs, SafeName(baseName) + "_" + hash + variant);

            var cached = TryLoadIndex(destDir);
            if (cached != null) return cached; // этот пак уже импортирован без изменений

            progress?.Report("Подготовка иконок…");
            var tmpDest = destDir + ".tmp";
            if (Directory.Exists(tmpDest)) Directory.Delete(tmpDest, true);
            Directory.CreateDirectory(Path.Combine(tmpDest, "icons"));
            Directory.CreateDirectory(Path.Combine(tmpDest, "previews"));

            var icons = new List<PackIcon>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Приоритет форматов при одинаковых именах: .ico > .svg > растр, библиотеки отдельно
            int Rank(string f)
            {
                var e = Path.GetExtension(f).ToLowerInvariant();
                return IcoExt.Contains(e) ? 0 : SvgExt.Contains(e) ? 1 : RasterExt.Contains(e) ? 2 : 3;
            }

            int done = 0, skippedOtherVariant = 0;
            var duplicates = new List<string>();
            var ordered = iconFiles
                .OrderBy(f => VariantRank(Path.GetRelativePath(root, f), preferDark))
                .ThenBy(Rank)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase);
            foreach (var file in ordered)
            {
                done++;
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (manifest != null && manifest.Ignore.Contains(rel)) continue;
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var hints = Path.GetDirectoryName(rel)?.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new();
                progress?.Report($"Иконка {done}/{iconFiles.Count}: {rel}");
                try
                {
                    if (LibExt.Contains(ext))
                    {
                        var lib = Path.GetFileNameWithoutExtension(file);
                        foreach (var li in IconLibraryReader.Extract(file))
                        {
                            var name = !string.IsNullOrWhiteSpace(li.Name) ? li.Name! : $"{lib}_{li.Index}";
                            AddIcon(icons, usedNames, tmpDest, destDir, name, li.IcoData, $"{rel}#{li.Index}", hints, warnings);
                        }
                        continue;
                    }

                    var stem = Path.GetFileNameWithoutExtension(file);
                    if (usedNames.Contains(stem))
                    {
                        if (VariantRank(rel, preferDark) == 2) skippedOtherVariant++;
                        else duplicates.Add(rel);
                        continue;
                    }
                    byte[] ico = IcoExt.Contains(ext) ? IcoConverter.NormalizeIco(file)
                        : SvgExt.Contains(ext) ? IcoConverter.SvgToIco(file)
                        : IcoConverter.RasterToIco(file);
                    AddIcon(icons, usedNames, tmpDest, destDir, stem, ico, rel, hints, warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add($"{rel}: {ex.Message}");
                }
            }

            if (icons.Count == 0) throw new InvalidDataException("Не удалось прочитать ни одной иконки.\n" + string.Join("\n", warnings));
            if (skippedOtherVariant > 0)
                warnings.Insert(0, $"В паке есть светлый и тёмный варианты — взят {(preferDark ? "тёмный" : "светлый")} под тему Windows " +
                                   $"(пропущено файлов другого варианта: {skippedOtherVariant}).");
            if (duplicates.Count > 0)
                warnings.Add($"Пропущены повторы с тем же именем (обычно та же иконка в другом формате): {duplicates.Count} — " +
                             string.Join(", ", duplicates.Take(3)) + (duplicates.Count > 3 ? "…" : ""));

            if (manifestPath != null) File.Copy(manifestPath, Path.Combine(tmpDest, "manifest.json"), true);
            var pack = new IconPack
            {
                Name = baseName,
                Directory = destDir,
                SourcePath = sourcePath,
                Icons = icons,
                Manifest = manifest,
                Warnings = warnings,
            };
            SaveIndex(tmpDest, pack);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            Directory.Move(tmpDest, destDir);
            return pack;
        }
        finally
        {
            if (tempDir != null)
                try { Directory.Delete(tempDir, true); } catch { /* временная папка, не критично */ }
        }
    }

    // ---- светлый / тёмный варианты (Day/Night, Light/Dark) ----

    private static readonly string[] DarkWords = { "night", "dark", "black", "ночь", "ночная", "темная", "тёмная", "темный", "тёмный" };
    private static readonly string[] LightWords = { "day", "light", "white", "день", "дневная", "светлая", "светлый" };

    /// <summary>0 — папка подходящего варианта, 1 — без варианта, 2 — папка другого варианта.</summary>
    internal static int VariantRank(string relPath, bool preferDark)
    {
        var dirs = (Path.GetDirectoryName(relPath) ?? "")
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(d => Matching.NameNormalizer.Tokenize(d))
            .ToHashSet();
        bool dark = DarkWords.Any(dirs.Contains), light = LightWords.Any(dirs.Contains);
        if (dark == light) return 1;
        return dark == preferDark ? 0 : 2;
    }

    /// <summary>Тёмная ли системная тема (панель задач и рабочий стол).</summary>
    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v ? v == 0 : true;
        }
        catch { return true; }
    }

    private static void AddIcon(List<PackIcon> icons, HashSet<string> used, string tmpDir, string finalDir,
        string name, byte[] ico, string source, List<string> hints, List<string> warnings)
    {
        var unique = name;
        for (int i = 2; used.Contains(unique); i++) unique = $"{name}_{i}";
        used.Add(unique);
        var fileName = SafeName(unique);
        File.WriteAllBytes(Path.Combine(tmpDir, "icons", fileName + ".ico"), ico);
        try
        {
            using var preview = IcoConverter.PreviewFromIco(ico, PreviewSize);
            IcoConverter.SavePng(preview, Path.Combine(tmpDir, "previews", fileName + ".png"));
        }
        catch (Exception ex) { warnings.Add($"{source}: нет превью ({ex.Message})"); }

        icons.Add(new PackIcon
        {
            Name = unique,
            IcoPath = Path.Combine(finalDir, "icons", fileName + ".ico"),
            PreviewPath = Path.Combine(finalDir, "previews", fileName + ".png"),
            Source = source,
            Hints = hints,
        });
    }

    // ---- индекс импортированного пака ----

    private sealed record IndexDto(string Name, string? Source, List<IndexIcon> Icons, List<string> Warnings);
    private sealed record IndexIcon(string Name, string File, string Source, List<string> Hints);

    private static void SaveIndex(string dir, IconPack pack)
    {
        var dto = new IndexDto(pack.Name, pack.SourcePath,
            pack.Icons.Select(i => new IndexIcon(i.Name, Path.GetFileNameWithoutExtension(i.IcoPath), i.Source, i.Hints)).ToList(),
            pack.Warnings);
        File.WriteAllText(Path.Combine(dir, IndexFile), JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Загружает ранее импортированный пак из папки в %APPDATA%\IcoSwap\packs.</summary>
    public static IconPack? TryLoadIndex(string dir)
    {
        var path = Path.Combine(dir, IndexFile);
        if (!File.Exists(path)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<IndexDto>(File.ReadAllText(path));
            if (dto == null) return null;
            PackManifest? manifest = null;
            var mf = Path.Combine(dir, "manifest.json");
            if (File.Exists(mf)) manifest = PackManifest.Parse(File.ReadAllText(mf));
            var icons = dto.Icons.Select(i => new PackIcon
            {
                Name = i.Name,
                IcoPath = Path.Combine(dir, "icons", i.File + ".ico"),
                PreviewPath = Path.Combine(dir, "previews", i.File + ".png"),
                Source = i.Source,
                Hints = i.Hints ?? new(),
            }).Where(i => File.Exists(i.IcoPath)).ToList();
            return new IconPack { Name = dto.Name, Directory = dir, SourcePath = dto.Source, Icons = icons, Manifest = manifest, Warnings = dto.Warnings ?? new() };
        }
        catch { return null; }
    }

    private static List<string> EnumerateFiles(string root) =>
        Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => !Path.GetFileName(f).StartsWith("._"))
            .ToList();

    private static string SingleSubfolderOrSelf(string dir)
    {
        // Частый случай: zip содержит одну папку с паком внутри
        if (Directory.EnumerateFiles(dir).Any()) return dir;
        var subs = Directory.GetDirectories(dir).Where(d => !Path.GetFileName(d).StartsWith("__MACOSX")).ToArray();
        return subs.Length == 1 ? SingleSubfolderOrSelf(subs[0]) : dir;
    }

    private static string ComputeHash(string root, List<string> files, string? manifest)
    {
        using var sha = SHA256.Create();
        foreach (var f in files.Where(f => IsSupportedFile(f) || f == manifest).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Encoding.UTF8.GetBytes(Path.GetRelativePath(root, f).ToLowerInvariant());
            sha.TransformBlock(rel, 0, rel.Length, null, 0);
            var data = File.ReadAllBytes(f);
            sha.TransformBlock(data, 0, data.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!)[..8].ToLowerInvariant();
    }

    internal static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(invalid.Contains(c) || c == ',' ? '_' : c);
        var s = sb.ToString().Trim().TrimEnd('.');
        return string.IsNullOrEmpty(s) ? "pack" : s.Length > 60 ? s[..60] : s;
    }
}
