using System.Text.RegularExpressions;
using IcoSwap.Core.Packs;
using IcoSwap.Core.Targets;

namespace IcoSwap.Core.Matching;

public sealed class MatchItem
{
    public required IconTarget Target { get; init; }
    public required PackIcon Icon { get; set; }
    public double Score { get; set; }
    public string Reason { get; set; } = "";
    private bool? _enabled;
    public bool Enabled { get => _enabled ?? Target.DefaultEnabled; set => _enabled = value; }
}

public sealed class MatchPlan
{
    public List<MatchItem> Items { get; } = new();
    public List<PackIcon> Unmatched { get; } = new();
}

/// <summary>Автоматическое сопоставление иконок пака с целями системы.</summary>
public static class Matcher
{
    public const double Threshold = 0.82;
    public const double Strong = 0.95;

    private static readonly Dictionary<string, string> HintPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["drives"] = "drive", ["drive"] = "drive", ["disks"] = "drive", ["диски"] = "drive",
        ["folders"] = "folder", ["папки"] = "folder",
        ["filetypes"] = "file", ["file types"] = "file", ["types"] = "file", ["extensions"] = "file", ["ext"] = "file",
        ["files"] = "file", ["mimetypes"] = "file", ["mimes"] = "file", ["типы файлов"] = "file",
    };

    private static int Tier(TargetKind k) => k switch
    {
        TargetKind.SystemIcon or TargetKind.KnownFolder or TargetKind.ShellIcon => 0,
        TargetKind.Drive => 1,
        TargetKind.FileType => 2,
        _ => 3,
    };

    public static MatchPlan Build(IconPack pack, TargetCatalog catalog)
    {
        var chosen = new Dictionary<string, MatchItem>(StringComparer.OrdinalIgnoreCase);
        var locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestIcons = new HashSet<PackIcon>();

        // 1. Явные соответствия из manifest.json
        if (pack.Manifest != null)
        {
            foreach (var icon in pack.Icons)
            {
                var specs = ManifestSpecsFor(pack.Manifest, icon);
                if (specs == null) continue;
                manifestIcons.Add(icon);
                foreach (var spec in specs)
                    foreach (var t in ResolveSpec(spec, catalog))
                    {
                        chosen[t.Id] = new MatchItem { Target = t, Icon = icon, Score = 1, Reason = "manifest.json" };
                        locked.Add(t.Id);
                    }
            }
        }

        // 2. Автосопоставление по именам
        var targetNames = catalog.Targets.ToDictionary(t => t, t => t.Aliases.Select(a => (a, NameInfo.From(a.Name))).ToList());
        foreach (var icon in pack.Icons)
        {
            if (manifestIcons.Contains(icon)) continue;
            var names = IconNames(icon);
            var candidates = new List<(IconTarget t, double score, string reason)>();

            foreach (var (target, aliases) in targetNames)
            {
                double best = 0;
                string reason = "";
                foreach (var n in names)
                    foreach (var (alias, info) in aliases)
                    {
                        var (s, r) = Score(n, info, alias);
                        if (s > best) { best = s; reason = r; }
                    }
                if (best >= Threshold) candidates.Add((target, best, reason));
            }

            foreach (var n in names)
                foreach (var ext in TargetCatalog.ExtensionCandidates(n))
                {
                    var ft = catalog.GetFileType(ext);
                    if (ft != null && !candidates.Any(c => c.t.Id == ft.Id))
                        candidates.Add((ft, n.Tokens.Count == 1 ? 1.0 : 0.97, "расширение"));
                }

            if (candidates.Count == 0) continue;
            var strong = candidates.Where(c => c.score >= Strong).Select(c => Tier(c.t.Kind)).DefaultIfEmpty(int.MaxValue).Min();
            if (strong != int.MaxValue) candidates = candidates.Where(c => Tier(c.t.Kind) == strong).ToList();

            foreach (var (t, score, reason) in candidates)
            {
                if (locked.Contains(t.Id)) continue;
                if (chosen.TryGetValue(t.Id, out var existing) && existing.Score >= score) continue;
                chosen[t.Id] = new MatchItem { Target = t, Icon = icon, Score = score, Reason = reason };
            }
        }

        var plan = new MatchPlan();
        plan.Items.AddRange(chosen.Values
            .OrderBy(i => Tier(i.Target.Kind)).ThenBy(i => i.Target.Kind).ThenBy(i => i.Target.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        var used = plan.Items.Select(i => i.Icon).ToHashSet();
        plan.Unmatched.AddRange(pack.Icons.Where(i => !used.Contains(i)));
        return plan;
    }

    /// <summary>Варианты имени иконки: само имя и имя с подсказкой из папки пака ("drives/c.ico" → "drive c").</summary>
    internal static List<NameInfo> IconNames(PackIcon icon)
    {
        var list = new List<NameInfo> { NameInfo.From(icon.Name) };
        foreach (var h in icon.Hints)
            if (HintPrefixes.TryGetValue(h, out var prefix))
                list.Add(NameInfo.From(prefix + " " + icon.Name));
        return list;
    }

    /// <summary>Оценка сходства имени иконки и имени цели (0..1).</summary>
    public static (double Score, string Reason) Score(NameInfo icon, NameInfo target, NameAlias alias)
    {
        if (icon.Compact.Length == 0 || target.Compact.Length == 0) return (0, "");
        double w = alias.Weight;

        if (icon.Canonical != null && target.Canonical != null)
            return icon.Canonical == target.Canonical ? (w, "синоним") : (0, "");
        if (icon.Compact == target.Compact) return (w, "точное имя");
        if (alias.ExactOnly) return (0, "");
        // Канонические имена совпадают с именем цели после синонима
        if (icon.Canonical != null && icon.Canonical == target.Compact) return (w, "синоним");
        if (target.Canonical != null && target.Canonical == icon.Compact) return (w, "синоним");

        // Все слова иконки входят в имя цели: "roblox" → "Roblox Player"
        if (IsSubset(icon.Tokens, target.Tokens) && icon.Compact.Length >= 4)
        {
            int extra = target.Tokens.Count - icon.Tokens.Count;
            return ((0.9 - 0.06 * extra) * w, "часть имени");
        }
        if (IsSubset(target.Tokens, icon.Tokens) && target.Compact.Length >= 4)
        {
            int extra = icon.Tokens.Count - target.Tokens.Count;
            return ((0.86 - 0.06 * extra) * w, "часть имени");
        }

        if (icon.Compact.Length >= 7 && target.Compact.Length >= 7)
        {
            var jw = NameNormalizer.JaroWinkler(icon.Compact, target.Compact);
            if (jw >= 0.93) return (jw * 0.9 * w, "похожее имя");
        }
        return (0, "");
    }

    private static bool IsSubset(IReadOnlyList<string> small, IReadOnlyList<string> big) =>
        small.Count > 0 && small.Count <= big.Count && small.All(s => big.Contains(s));

    // ---------------- manifest ----------------

    private static List<string>? ManifestSpecsFor(PackManifest m, PackIcon icon)
    {
        var src = PackManifest.NormalizeKey(icon.Source);
        if (m.Map.TryGetValue(src, out var l)) return l;
        var file = src.Split('/').Last();
        if (m.Map.TryGetValue(file, out l)) return l;
        if (m.Map.TryGetValue(icon.Name, out l)) return l;
        // "lib.icl#CHROME" — по имени ресурса
        var hash = src.IndexOf('#');
        if (hash > 0 && m.Map.TryGetValue(src[..hash] + "#" + icon.Name, out l)) return l;
        return null;
    }

    /// <summary>Разбор спецификации цели из manifest.json (см. README).</summary>
    public static IEnumerable<IconTarget> ResolveSpec(string spec, TargetCatalog catalog)
    {
        var idx = spec.IndexOf(':');
        if (idx <= 0) yield break;
        var kind = spec[..idx].Trim().ToLowerInvariant();
        var value = spec[(idx + 1)..].Trim();
        if (value.Length == 0) yield break;

        switch (kind)
        {
            case "ext":
            {
                var t = catalog.GetFileType(value);
                if (t != null) yield return t;
                break;
            }
            case "system":
            case "sys":
            {
                var key = SystemIcons.Find(value)?.Key ?? SysKeyFromSynonym(value);
                var t = key != null ? catalog.FindById("sys:" + key) : null;
                if (t != null) yield return t;
                break;
            }
            case "folder":
            case "kf":
            {
                var v = value.ToLowerInvariant();
                IconTarget? t = v switch
                {
                    "generic" or "folder" or "all" or "default" => catalog.FindById("shell:folder"),
                    "open" or "folder-open" => catalog.FindById("shell:folder-open"),
                    _ when SystemIcons.FindKnownFolder(v) != null => catalog.FindById("kf:" + v),
                    _ => KnownFolderFromSynonym(v, catalog) ?? FolderPath(value),
                };
                if (t != null) yield return t;
                break;
            }
            case "path":
            case "dir":
            {
                var t = FolderPath(value);
                if (t != null) yield return t;
                break;
            }
            case "drive":
            {
                var v = value.TrimEnd(':', '\\').ToLowerInvariant();
                foreach (var t in catalog.Targets.Where(t => t.Kind == TargetKind.Drive))
                {
                    var letter = t.Id[6..].ToLowerInvariant();
                    bool ok = v switch
                    {
                        "all" => true,
                        "fixed" or "local" => t.Location == "Локальный",
                        "removable" or "usb" => t.Location == "Съёмный",
                        "optical" or "cd" or "dvd" => t.Location == "Оптический",
                        "system" => letter == Path.GetPathRoot(Environment.SystemDirectory)![..1].ToLowerInvariant(),
                        _ => letter == v,
                    };
                    if (ok) yield return t;
                }
                break;
            }
            case "shortcut":
            case "lnk":
            {
                var re = WildcardToRegex(value);
                foreach (var t in catalog.Targets.Where(t => t.Kind == TargetKind.Shortcut && re.IsMatch(t.DisplayName)))
                    yield return t;
                break;
            }
            case "exe":
            {
                var exe = Path.GetFileNameWithoutExtension(value);
                foreach (var t in catalog.Targets.Where(t => t.Kind == TargetKind.Shortcut && t.Aliases.Skip(1).Any(a => a.Name.Equals(exe, StringComparison.OrdinalIgnoreCase))))
                    yield return t;
                break;
            }
        }
    }

    private static string? SysKeyFromSynonym(string value)
    {
        var syn = Synonyms.Lookup(NameNormalizer.Compact(value));
        return SystemIcons.All.FirstOrDefault(s => s.Synonym == syn)?.Key;
    }

    private static IconTarget? KnownFolderFromSynonym(string value, TargetCatalog catalog)
    {
        var syn = Synonyms.Lookup(NameNormalizer.Compact(value));
        var kf = SystemIcons.KnownFolders.FirstOrDefault(k => k.Synonym == syn);
        return kf != null ? catalog.FindById("kf:" + kf.Key) : null;
    }

    private static IconTarget? FolderPath(string value)
    {
        var path = Environment.ExpandEnvironmentVariables(value);
        return Path.IsPathFullyQualified(path) && Directory.Exists(path) ? TargetCatalog.CreateFolderTarget(path) : null;
    }

    private static Regex WildcardToRegex(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
