using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using IcoSwap.Core;
using IcoSwap.Core.Apply;
using IcoSwap.Core.Matching;
using IcoSwap.Core.Packs;
using IcoSwap.Core.Targets;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length >= 2 && args[0] == "--elevated-job") return ElevatedJob.Execute(args[1]);
if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("""
        icoswap-cli — консольный режим IcoSwap

          scan <папка|zip|icl>                 показать, что на что заменится
          apply <папка|zip|icl> [опции]        применить пак
              --no-admin                       пропустить пункты, требующие прав администратора
              --filter <regex>                 применить только цели, чей Id подходит под regex
          status                               список применённых изменений
          rollback --all | <id> [<id>...]      откатить всё или отдельные пункты
          refresh [--deep]                     обновить Проводник (--deep — с перезапуском и очисткой кэша)
          current <id> <out.png>               сохранить текущую иконку цели в PNG
          convert <in.png|svg> <out.ico>       сконвертировать картинку в многоразмерный .ico
        """);
    return 0;
}

var store = new BackupStore();
var engine = new ApplyEngine(store)
{
    ElevatedRunner = job => job.RunElevated(Environment.ProcessPath!),
};
var progress = new Progress<string>(_ => { });

switch (args[0])
{
    case "scan":
    case "apply":
    {
        if (args.Length < 2) { Console.Error.WriteLine("Укажите пак"); return 2; }
        var sw = Stopwatch.StartNew();
        var pack = PackLoader.Import(args[1], progress);
        var catalog = TargetCatalog.Discover();
        var plan = Matcher.Build(pack, catalog);
        Console.WriteLine($"Пак: {pack.Name} ({pack.Icons.Count} иконок) → {pack.Directory}");
        foreach (var w in pack.Warnings) Console.WriteLine("  ! " + w);
        Console.WriteLine($"Целей в системе: {catalog.Targets.Count}, сопоставлено: {plan.Items.Count}, {sw.ElapsedMilliseconds} мс");
        foreach (var g in plan.Items.GroupBy(i => i.Target.Kind))
        {
            Console.WriteLine($"\n[{IconTarget.KindTitle(g.Key)}]");
            foreach (var i in g)
                Console.WriteLine($"  {i.Icon.Name,-24} → {i.Target.DisplayName} ({i.Target.Location}) [{i.Score:0.00} {i.Reason}]"
                                  + (i.Target.RequiresAdmin ? " [админ]" : "") + (i.Enabled ? "" : " [выкл]") + $"  id={i.Target.Id}"
                                  + (i.Target.Note != null ? $"\n      ! {i.Target.Note}" : ""));
        }
        if (plan.Unmatched.Count > 0)
            Console.WriteLine("\nНе опознаны: " + string.Join(", ", plan.Unmatched.Select(u => u.Name)));
        if (args[0] == "scan") return 0;

        var items = plan.Items.Where(i => i.Enabled);
        if (args.Contains("--no-admin")) items = items.Where(i => !i.Target.RequiresAdmin);
        var fi = Array.IndexOf(args, "--filter");
        if (fi > 0 && fi + 1 < args.Length)
        {
            var re = new Regex(args[fi + 1], RegexOptions.IgnoreCase);
            items = items.Where(i => re.IsMatch(i.Target.Id));
        }
        var report = engine.Apply(items.Select(i => new ApplyRequest(i.Target, i.Icon.IcoPath)).ToList(), progress);
        PrintReport(report);
        return report.Failed.Count == 0 ? 0 : 1;
    }
    case "status":
    {
        var all = store.All();
        Console.WriteLine($"Применено изменений: {all.Count}");
        foreach (var r in all)
            Console.WriteLine($"  {r.Kind,-12} {r.DisplayName} ({r.Location}) ← {Path.GetFileName(r.AppliedIcon)}  id={r.TargetId}");
        return 0;
    }
    case "rollback":
    {
        var report = args.Contains("--all") ? engine.RestoreAll(progress) : engine.Restore(args.Skip(1), progress);
        PrintReport(report);
        return report.Failed.Count == 0 ? 0 : 1;
    }
    case "refresh":
        if (args.Contains("--deep")) ShellRefresher.DeepRefresh(); else ShellRefresher.Refresh();
        Console.WriteLine("Готово");
        return 0;
    case "convert":
    {
        var ext = Path.GetExtension(args[1]).ToLowerInvariant();
        var ico = ext == ".svg" ? IcoSwap.Core.Icons.IcoConverter.SvgToIco(args[1]) : IcoSwap.Core.Icons.IcoConverter.RasterToIco(args[1]);
        File.WriteAllBytes(args[2], ico);
        Console.WriteLine("Сохранено: " + args[2]);
        return 0;
    }
    case "current":
    {
        var catalog = TargetCatalog.Discover();
        var t = catalog.FindById(args[1]) ?? throw new ArgumentException("Цель не найдена: " + args[1]);
        var png = ShellImage.GetCurrentIconPng(t, 64);
        if (png == null) { Console.Error.WriteLine("Нет изображения"); return 1; }
        File.WriteAllBytes(args[2], png);
        Console.WriteLine("Сохранено: " + args[2]);
        return 0;
    }
    default:
        Console.Error.WriteLine("Неизвестная команда: " + args[0]);
        return 2;
}

static void PrintReport(ApplyReport r)
{
    Console.WriteLine("\n" + r);
    foreach (var (t, e) in r.Failed) Console.WriteLine($"  ✗ {t}: {e}");
    foreach (var s in r.SkippedAdmin) Console.WriteLine($"  – пропущено (нужен админ): {s}");
}
