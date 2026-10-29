using System.Security.Principal;
using IcoSwap.Core.Targets;

namespace IcoSwap.Core.Apply;

public sealed record ApplyRequest(IconTarget Target, string IcoPath);

public sealed class ApplyReport
{
    public List<string> Succeeded { get; } = new();
    public List<(string Target, string Error)> Failed { get; } = new();
    /// <summary>Пункты, требующие прав администратора, которые не были выполнены (UAC отклонён или запрещён).</summary>
    public List<string> SkippedAdmin { get; } = new();
    internal HashSet<string> ChangedItems { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> ChangedDirs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Merge(ApplyReport other)
    {
        Succeeded.AddRange(other.Succeeded);
        Failed.AddRange(other.Failed);
        SkippedAdmin.AddRange(other.SkippedAdmin);
        ChangedItems.UnionWith(other.ChangedItems);
        ChangedDirs.UnionWith(other.ChangedDirs);
    }

    public override string ToString() =>
        $"Успешно: {Succeeded.Count}, ошибок: {Failed.Count}" + (SkippedAdmin.Count > 0 ? $", пропущено (нужен админ): {SkippedAdmin.Count}" : "");
}

/// <summary>
/// Применяет и откатывает изменения. Пункты, требующие админа, передаются в отдельный
/// процесс с повышенными правами через <see cref="ElevatedRunner"/>; остальное выполняется в текущем процессе.
/// </summary>
public sealed class ApplyEngine
{
    public BackupStore Store { get; }

    /// <summary>Запускает задание в процессе с правами администратора. null — такие пункты пропускаются.</summary>
    public Func<ElevatedJob, ApplyReport?>? ElevatedRunner { get; set; }

    /// <summary>Обновлять ли Проводник после операции.</summary>
    public bool RefreshShell { get; set; } = true;

    public ApplyEngine(BackupStore store) => Store = store;

    public static bool IsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public ApplyReport Apply(IEnumerable<ApplyRequest> requests, IProgress<string>? progress = null)
    {
        var list = requests.ToList();
        var report = new ApplyReport();
        bool elevated = IsElevated;
        var adminItems = list.Where(r => r.Target.RequiresAdmin && !elevated).ToList();

        foreach (var r in list.Except(adminItems))
        {
            progress?.Report("Применяю: " + r.Target.DisplayName);
            ApplyOne(r, report);
        }

        if (adminItems.Count > 0) RunElevated(ElevatedJob.ForApply(adminItems, Store.FilePath), adminItems.Select(a => a.Target.DisplayName), report, progress);
        if (RefreshShell) ShellRefresher.Refresh(report.ChangedItems, report.ChangedDirs);
        return report;
    }

    public ApplyReport Restore(IEnumerable<string> targetIds, IProgress<string>? progress = null)
    {
        Store.Reload();
        var records = targetIds.Select(Store.Get).Where(r => r != null).Cast<BackupRecord>().ToList();
        var report = new ApplyReport();
        bool elevated = IsElevated;
        var adminRecords = records.Where(r => r.RequiresAdmin && !elevated).ToList();

        foreach (var rec in records.Except(adminRecords))
        {
            progress?.Report("Откатываю: " + rec.DisplayName);
            RestoreOne(rec, report);
        }

        if (adminRecords.Count > 0) RunElevated(ElevatedJob.ForRestore(adminRecords, Store.FilePath), adminRecords.Select(a => a.DisplayName), report, progress);
        if (RefreshShell) ShellRefresher.Refresh(report.ChangedItems, report.ChangedDirs);
        return report;
    }

    public ApplyReport RestoreAll(IProgress<string>? progress = null)
    {
        Store.Reload();
        return Restore(Store.All().Select(r => r.TargetId).ToList(), progress);
    }

    private void RunElevated(ElevatedJob job, IEnumerable<string> names, ApplyReport report, IProgress<string>? progress)
    {
        progress?.Report("Запрос прав администратора…");
        ApplyReport? sub = null;
        try { sub = ElevatedRunner?.Invoke(job); }
        catch (Exception ex) { report.Failed.Add(("права администратора", ex.Message)); }
        if (sub == null) report.SkippedAdmin.AddRange(names);
        else report.Merge(sub);
        Store.Reload(); // процесс с правами админа мог дописать бэкап
    }

    internal void ApplyOne(ApplyRequest r, ApplyReport report)
    {
        var t = r.Target;
        var existing = Store.Get(t.Id);
        var rec = existing ?? new BackupRecord
        {
            TargetId = t.Id,
            DisplayName = t.DisplayName,
            Location = t.Location,
            Kind = t.Kind,
            RequiresAdmin = t.RequiresAdmin,
            FirstAppliedUtc = DateTime.UtcNow,
        };
        var ctx = new ApplyContext { Record = rec, Persist = () => Store.Put(rec) };
        try
        {
            if (!File.Exists(r.IcoPath)) throw new FileNotFoundException("Файл иконки не найден", r.IcoPath);
            Handlers.For(t.Id).Apply(t.Id, r.IcoPath, ctx);
            rec.AppliedIcon = r.IcoPath;
            rec.LastAppliedUtc = DateTime.UtcNow;
            Store.Put(rec);
            report.Succeeded.Add(t.DisplayName);
        }
        catch (Exception ex)
        {
            report.Failed.Add((t.DisplayName, ex.Message));
            // Если это было первое применение и что-то успели изменить — возвращаем как было
            if (existing == null && (rec.Registry?.Count > 0 || rec.File != null))
            {
                try { RestoreOne(rec, new ApplyReport()); } catch { /* оставляем бэкап для ручного отката */ }
            }
        }
        report.ChangedItems.UnionWith(ctx.ChangedItems);
        report.ChangedDirs.UnionWith(ctx.ChangedDirs);
    }

    internal void RestoreOne(BackupRecord rec, ApplyReport report)
    {
        var ctx = new ApplyContext { Record = rec, Persist = () => { } };
        try
        {
            Handlers.For(rec.TargetId).Restore(rec, b => KeyUsedByOthers(b, rec), ctx);
            Store.Remove(rec.TargetId);
            report.Succeeded.Add(rec.DisplayName);
        }
        catch (Exception ex)
        {
            report.Failed.Add((rec.DisplayName, ex.Message));
        }
        report.ChangedItems.UnionWith(ctx.ChangedItems);
        report.ChangedDirs.UnionWith(ctx.ChangedDirs);
    }

    /// <summary>Есть ли другие активные изменения, которые пишут в тот же ключ (или под созданный нами корень).</summary>
    private bool KeyUsedByOthers(RegValueBackup b, BackupRecord self)
    {
        var root = b.CreatedRoot ?? b.KeyPath;
        return Store.All().Where(r => r.TargetId != self.TargetId && r.Registry != null)
            .SelectMany(r => r.Registry!)
            .Any(o => o.Hive == b.Hive && (o.KeyPath.Equals(root, StringComparison.OrdinalIgnoreCase)
                                           || o.KeyPath.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)));
    }
}
