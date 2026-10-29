using System.Diagnostics;
using IcoSwap.Core.Native;

namespace IcoSwap.Core.Apply;

/// <summary>Сообщает Проводнику об изменениях и сбрасывает кэш иконок.</summary>
public static class ShellRefresher
{
    private const uint WM_USER = 0x0400;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static void Refresh(IEnumerable<string>? items = null, IEnumerable<string>? dirs = null)
    {
        foreach (var p in items ?? Enumerable.Empty<string>())
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_UPDATEITEM, NativeMethods.SHCNF_PATHW | NativeMethods.SHCNF_FLUSHNOWAIT, p, null);
        foreach (var d in dirs ?? Enumerable.Empty<string>())
        {
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_UPDATEDIR, NativeMethods.SHCNF_PATHW | NativeMethods.SHCNF_FLUSHNOWAIT, d, null);
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_UPDATEITEM, NativeMethods.SHCNF_PATHW | NativeMethods.SHCNF_FLUSHNOWAIT, d, null);
        }
        RunIe4uinit("-show");
        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST | NativeMethods.SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>ie4uinit -show / -ClearIconCache — штатные способы Windows 10/11 обновить кэш иконок пользователя.</summary>
    private static void RunIe4uinit(string args)
    {
        try
        {
            var exe = Path.Combine(Environment.SystemDirectory, "ie4uinit.exe");
            if (!File.Exists(exe)) return;
            using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(15000);
        }
        catch { /* необязательный шаг */ }
    }

    /// <summary>
    /// Глубокая очистка: перезапуск Проводника с удалением файлов кэша иконок пользователя.
    /// Нужна для иконки всех папок и закреплённых на панели задач ярлыков.
    /// </summary>
    public static void DeepRefresh()
    {
        // Штатный выход Проводника (как «Выйти из проводника» по Ctrl+Shift+ПКМ на панели задач):
        // в отличие от Kill, Windows не перезапускает его мгновенно и файлы кэша освобождаются.
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero) PostMessage(tray, WM_USER + 436, IntPtr.Zero, IntPtr.Zero);
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && Process.GetProcessesByName("explorer").Length > 0) Thread.Sleep(200);
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch { }
            finally { p.Dispose(); }
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cacheDir = Path.Combine(local, @"Microsoft\Windows\Explorer");
        var files = new List<string>();
        if (Directory.Exists(cacheDir)) files.AddRange(Directory.GetFiles(cacheDir, "iconcache_*.db"));
        files.Add(Path.Combine(local, "IconCache.db"));
        foreach (var f in files)
            try { if (File.Exists(f)) File.Delete(f); } catch { /* файл может быть занят — пропускаем */ }

        // Файлы кэша часто держат открытыми и другие программы — тогда помогает штатная очистка
        RunIe4uinit("-ClearIconCache");
        Thread.Sleep(1000);
        if (Process.GetProcessesByName("explorer").Length == 0)
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true });
        Thread.Sleep(1500);
        Refresh();
    }
}
