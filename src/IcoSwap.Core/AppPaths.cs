namespace IcoSwap.Core;

/// <summary>Данные приложения: бэкапы в %APPDATA%\IcoSwap, иконки паков в %ProgramData%\IcoSwap (ICOSWAP_HOME — для тестов).</summary>
public static class AppPaths
{
    private static string? _root;

    public static string Root
    {
        get
        {
            if (_root != null) return _root;
            var env = Environment.GetEnvironmentVariable("ICOSWAP_HOME");
            _root = !string.IsNullOrWhiteSpace(env)
                ? env
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IcoSwap");
            return _root;
        }
        set
        {
            _root = value;
            _packs = null;
        }
    }

    private static string? _packs;

    /// <summary>
    /// Иконки паков лежат в C:\ProgramData\IcoSwap\packs, а не в %APPDATA%: на части систем Проводник
    /// не загружает иконки из папок внутри AppData (проверено на Windows 11 26200) — ярлыки становятся пустыми.
    /// ProgramData доступна на запись обычному пользователю (своя папка) и на чтение всем.
    /// Если задан ICOSWAP_HOME или Root переопределён (тесты), паки хранятся внутри Root.
    /// </summary>
    public static string Packs
    {
        get
        {
            if (_packs == null)
            {
                bool custom = _root != null && !_root.Equals(DefaultRoot, StringComparison.OrdinalIgnoreCase);
                _packs = custom || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ICOSWAP_HOME"))
                    ? Path.Combine(Root, "packs")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IcoSwap", "packs");
            }
            return Ensure(_packs);
        }
        set => _packs = value;
    }

    private static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IcoSwap");
    public static string Backups => Ensure(Path.Combine(Root, "backups"));
    public static string BackupHistory => Ensure(Path.Combine(Backups, "history"));
    public static string StateFile => Path.Combine(Backups, "state.json");
    public static string Temp => Ensure(Path.Combine(Path.GetTempPath(), "IcoSwap"));

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
