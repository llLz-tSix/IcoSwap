using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using IcoSwap.Core.Targets;

namespace IcoSwap.Core.Apply;

/// <summary>
/// Задание для процесса с правами администратора. Передаётся через JSON-файл:
/// exe --elevated-job &lt;файл&gt;. Процесс применяет/откатывает пункты и пишет отчёт рядом.
/// </summary>
public sealed class ElevatedJob
{
    public string Action { get; set; } = "apply";
    public string StatePath { get; set; } = "";
    public List<JobItem> Items { get; set; } = new();

    public sealed class JobItem
    {
        public string TargetId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? Location { get; set; }
        public TargetKind Kind { get; set; }
        public string? IconPath { get; set; }
    }

    private sealed class ResultDto
    {
        public List<string> Succeeded { get; set; } = new();
        public List<string[]> Failed { get; set; } = new();
        public List<string> ChangedItems { get; set; } = new();
        public List<string> ChangedDirs { get; set; } = new();
    }

    public static ElevatedJob ForApply(IEnumerable<ApplyRequest> items, string statePath) => new()
    {
        Action = "apply",
        StatePath = statePath,
        Items = items.Select(r => new JobItem
        {
            TargetId = r.Target.Id, DisplayName = r.Target.DisplayName, Location = r.Target.Location,
            Kind = r.Target.Kind, IconPath = r.IcoPath,
        }).ToList(),
    };

    public static ElevatedJob ForRestore(IEnumerable<BackupRecord> records, string statePath) => new()
    {
        Action = "restore",
        StatePath = statePath,
        Items = records.Select(r => new JobItem { TargetId = r.TargetId, DisplayName = r.DisplayName, Kind = r.Kind }).ToList(),
    };

    /// <summary>Запускает exePath с правами администратора (UAC) и ждёт результата. null — пользователь отказал.</summary>
    public ApplyReport? RunElevated(string exePath)
    {
        var dir = Path.Combine(AppPaths.Temp, "jobs");
        Directory.CreateDirectory(dir);
        var jobFile = Path.Combine(dir, $"job_{Guid.NewGuid():N}.json");
        File.WriteAllText(jobFile, JsonSerializer.Serialize(this));
        var resultFile = jobFile + ".result.json";
        try
        {
            var psi = new ProcessStartInfo(exePath, $"--elevated-job \"{jobFile}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            p.WaitForExit();
            if (!File.Exists(resultFile)) throw new InvalidOperationException("Процесс администратора завершился без отчёта (код " + p.ExitCode + ")");
            var dto = JsonSerializer.Deserialize<ResultDto>(File.ReadAllText(resultFile))!;
            var report = new ApplyReport();
            report.Succeeded.AddRange(dto.Succeeded);
            report.Failed.AddRange(dto.Failed.Select(f => (f[0], f[1])));
            report.ChangedItems.UnionWith(dto.ChangedItems);
            report.ChangedDirs.UnionWith(dto.ChangedDirs);
            return report;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED — отказ в UAC
        {
            return null;
        }
        finally
        {
            try { File.Delete(jobFile); File.Delete(resultFile); } catch { }
        }
    }

    /// <summary>
    /// С правами администратора разрешено менять только то, что без них недоступно:
    /// иконку всех папок и ярлыки в общих «Пуске»/рабочем столе.
    /// </summary>
    internal static bool IsAllowedElevated(string targetId)
    {
        if (targetId.Equals("shell:folder", StringComparison.OrdinalIgnoreCase) ||
            targetId.Equals("shell:folder-open", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!targetId.StartsWith("lnk:", StringComparison.OrdinalIgnoreCase)) return false;
        var path = Path.GetFullPath(targetId[4..]);
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            return false;
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };
        return roots.Any(r => !string.IsNullOrEmpty(r) && path.StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Точка входа процесса с правами администратора.</summary>
    public static int Execute(string jobFile)
    {
        var resultFile = jobFile + ".result.json";
        var dto = new ResultDto();
        try
        {
            var job = JsonSerializer.Deserialize<ElevatedJob>(File.ReadAllText(jobFile))!;
            var store = new BackupStore(job.StatePath);
            var engine = new ApplyEngine(store) { RefreshShell = false };
            var report = new ApplyReport();
            foreach (var item in job.Items)
            {
                if (!IsAllowedElevated(item.TargetId) ||
                    (job.Action == "restore" && store.Get(item.TargetId)?.File is { } fb && !IsAllowedElevated("lnk:" + fb.Path)))
                {
                    report.Failed.Add((item.DisplayName, "цель не разрешена для режима администратора"));
                    continue;
                }
                if (job.Action == "apply")
                {
                    var target = new IconTarget
                    {
                        Id = item.TargetId, Kind = item.Kind, DisplayName = item.DisplayName, Location = item.Location,
                        RequiresAdmin = true,
                    };
                    engine.ApplyOne(new ApplyRequest(target, item.IconPath!), report);
                }
                else
                {
                    var rec = store.Get(item.TargetId);
                    if (rec != null) engine.RestoreOne(rec, report);
                }
            }
            dto.Succeeded = report.Succeeded;
            dto.Failed = report.Failed.Select(f => new[] { f.Target, f.Error }).ToList();
            dto.ChangedItems = report.ChangedItems.ToList();
            dto.ChangedDirs = report.ChangedDirs.ToList();
            File.WriteAllText(resultFile, JsonSerializer.Serialize(dto));
            return 0;
        }
        catch (Exception ex)
        {
            dto.Failed.Add(new[] { "задание администратора", ex.Message });
            File.WriteAllText(resultFile, JsonSerializer.Serialize(dto));
            return 1;
        }
    }
}
