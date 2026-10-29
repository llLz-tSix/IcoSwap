using System.Text.Json;

namespace IcoSwap.Core.Packs;

/// <summary>Иконка пака, уже сконвертированная в .ico и скопированная в %APPDATA%\IcoSwap\packs.</summary>
public sealed class PackIcon
{
    /// <summary>Имя для сопоставления и показа (имя файла без расширения или имя ресурса из .icl).</summary>
    public required string Name { get; init; }
    public required string IcoPath { get; init; }
    public required string PreviewPath { get; init; }
    /// <summary>Относительный путь в исходном паке (для .icl — "lib.icl#3").</summary>
    public required string Source { get; init; }
    /// <summary>Имена родительских папок в паке ("drives", "folders"...) — подсказки для сопоставления.</summary>
    public List<string> Hints { get; init; } = new();

    public override string ToString() => Name;
}

public sealed class PackManifest
{
    public string? Name { get; set; }
    public string? Author { get; set; }
    /// <summary>Файл иконки (или "lib.icl#3") → список спецификаций целей.</summary>
    public Dictionary<string, List<string>> Map { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Ignore { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static PackManifest Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        var root = doc.RootElement;
        var m = new PackManifest();
        if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) m.Name = n.GetString();
        if (root.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.String) m.Author = a.GetString();
        if (root.TryGetProperty("map", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in map.EnumerateObject())
            {
                var list = new List<string>();
                if (p.Value.ValueKind == JsonValueKind.String) list.Add(p.Value.GetString()!);
                else if (p.Value.ValueKind == JsonValueKind.Array)
                    list.AddRange(p.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!));
                if (list.Count > 0) m.Map[NormalizeKey(p.Name)] = list;
            }
        }
        if (root.TryGetProperty("ignore", out var ig) && ig.ValueKind == JsonValueKind.Array)
            foreach (var v in ig.EnumerateArray())
                if (v.ValueKind == JsonValueKind.String) m.Ignore.Add(NormalizeKey(v.GetString()!));
        return m;
    }

    public static string NormalizeKey(string key) => key.Replace('\\', '/').TrimStart('.', '/');
}

public sealed class IconPack
{
    public required string Name { get; init; }
    public required string Directory { get; init; }
    public string? SourcePath { get; init; }
    public List<PackIcon> Icons { get; init; } = new();
    public PackManifest? Manifest { get; init; }
    public List<string> Warnings { get; init; } = new();
}
