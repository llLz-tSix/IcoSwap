using System.Text.Json;
using System.Text.Json.Serialization;
using IcoSwap.Core.Targets;

namespace IcoSwap.Core.Apply;

/// <summary>Исходное значение одного параметра реестра.</summary>
public sealed class RegValueBackup
{
    public string Hive { get; set; } = "HKCU";
    public string KeyPath { get; set; } = "";
    public string ValueName { get; set; } = "";
    public bool Existed { get; set; }
    public string? Kind { get; set; }
    public string? Data { get; set; }
    /// <summary>Самый верхний ключ, которого не было и который создали мы (удаляется при откате, если в нём не осталось наших значений).</summary>
    public string? CreatedRoot { get; set; }
}

/// <summary>Исходное содержимое файла (ярлык, desktop.ini).</summary>
public sealed class FileBackup
{
    public string Path { get; set; } = "";
    public bool Existed { get; set; }
    public string? ContentBase64 { get; set; }
    public int Attributes { get; set; }
    public DateTime LastWriteUtc { get; set; }
}

/// <summary>Всё, что нужно для отката одной цели к исходному состоянию.</summary>
public sealed class BackupRecord
{
    public string TargetId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Location { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TargetKind Kind { get; set; }
    public bool RequiresAdmin { get; set; }
    public string? AppliedIcon { get; set; }
    public DateTime FirstAppliedUtc { get; set; }
    public DateTime LastAppliedUtc { get; set; }
    public List<RegValueBackup>? Registry { get; set; }
    public FileBackup? File { get; set; }
    /// <summary>Атрибуты папки до изменения (для desktop.ini).</summary>
    public int? FolderAttributes { get; set; }
    public string? FolderPath { get; set; }
}

public sealed class BackupState
{
    public int Version { get; set; } = 1;
    public Dictionary<string, BackupRecord> Records { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Хранит оригиналы в %APPDATA%\IcoSwap\backups\state.json. Оригинал фиксируется один раз —
/// при первом изменении цели; повторные применения его не перезаписывают.
/// Каждое сохранение дополнительно копируется в history\ с меткой времени.
/// </summary>
public sealed class BackupStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _lock = new();

    public BackupState State { get; private set; }

    public BackupStore(string? path = null)
    {
        _path = path ?? AppPaths.StateFile;
        State = Load(_path);
    }

    public string FilePath => _path;

    public static BackupState Load(string path)
    {
        if (!File.Exists(path)) return new BackupState();
        var json = File.ReadAllText(path);
        var s = JsonSerializer.Deserialize<BackupState>(json, Json) ?? new BackupState();
        s.Records = new Dictionary<string, BackupRecord>(s.Records, StringComparer.OrdinalIgnoreCase);
        return s;
    }

    public void Reload()
    {
        lock (_lock) State = Load(_path);
    }

    public BackupRecord? Get(string targetId)
    {
        lock (_lock) return State.Records.TryGetValue(targetId, out var r) ? r : null;
    }

    public IReadOnlyList<BackupRecord> All()
    {
        lock (_lock) return State.Records.Values.OrderByDescending(r => r.LastAppliedUtc).ToList();
    }

    public void Put(BackupRecord record)
    {
        lock (_lock)
        {
            State.Records[record.TargetId] = record;
            Save();
        }
    }

    public void Remove(string targetId)
    {
        lock (_lock)
        {
            if (State.Records.Remove(targetId)) Save();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(State, Json);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
        try
        {
            var hist = Path.Combine(Path.GetDirectoryName(_path)!, "history");
            Directory.CreateDirectory(hist);
            File.WriteAllText(Path.Combine(hist, $"state_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json"), json);
            // храним последние 50 снимков
            foreach (var old in Directory.GetFiles(hist, "state_*.json").OrderByDescending(f => f).Skip(50))
                File.Delete(old);
        }
        catch { /* история — не критично */ }
    }
}
