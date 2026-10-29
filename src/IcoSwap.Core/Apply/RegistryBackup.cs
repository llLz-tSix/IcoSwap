using Microsoft.Win32;

namespace IcoSwap.Core.Apply;

/// <summary>Чтение/запись/откат отдельных значений реестра с запоминанием исходного состояния.</summary>
internal static class RegistryBackup
{
    public static RegistryKey Root(string hive) => hive.ToUpperInvariant() switch
    {
        "HKLM" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
        "HKCU" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
        _ => throw new ArgumentException("Неизвестный раздел реестра: " + hive),
    };

    /// <summary>Добавляет в запись бэкап значения, если его там ещё нет. Возвращает true, если добавлен новый.</summary>
    public static bool EnsureCaptured(BackupRecord rec, string hive, string keyPath, string valueName)
    {
        rec.Registry ??= new List<RegValueBackup>();
        if (rec.Registry.Any(r => r.Hive == hive && r.KeyPath.Equals(keyPath, StringComparison.OrdinalIgnoreCase)
                                  && r.ValueName.Equals(valueName, StringComparison.OrdinalIgnoreCase)))
            return false;
        rec.Registry.Add(Capture(hive, keyPath, valueName));
        return true;
    }

    public static RegValueBackup Capture(string hive, string keyPath, string valueName)
    {
        using var root = Root(hive);
        var b = new RegValueBackup { Hive = hive, KeyPath = keyPath, ValueName = valueName };
        using (var key = root.OpenSubKey(keyPath))
        {
            if (key != null && key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase))
            {
                b.Existed = true;
                b.Kind = key.GetValueKind(valueName).ToString();
                var v = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                b.Data = v switch
                {
                    string s => s,
                    int i => i.ToString(),
                    string[] arr => string.Join("\0", arr),
                    byte[] bytes => Convert.ToBase64String(bytes),
                    null => null,
                    _ => v.ToString(),
                };
            }
        }
        b.CreatedRoot = FirstMissingKey(root, keyPath);
        return b;
    }

    public static void WriteString(string hive, string keyPath, string valueName, string data)
    {
        using var root = Root(hive);
        using var key = root.CreateSubKey(keyPath, writable: true)
                        ?? throw new UnauthorizedAccessException("Нет доступа к " + keyPath);
        // DefaultIcon пишем как REG_EXPAND_SZ — тем же типом, что использует сама Windows
        key.SetValue(valueName, data, RegistryValueKind.ExpandString);
    }

    public static string? ReadString(string hive, string keyPath, string valueName)
    {
        using var root = Root(hive);
        using var key = root.OpenSubKey(keyPath);
        return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    /// <param name="keyStillUsed">Используется ли созданный нами ключ другими активными изменениями.</param>
    /// <param name="fallbackValue">Чем заменить значение, которого не было, если ключ удалить нельзя (null — удалить значение).</param>
    public static void Restore(RegValueBackup b, Func<RegValueBackup, bool> keyStillUsed, Func<RegValueBackup, (string Data, RegistryValueKind Kind)?>? fallbackValue = null)
    {
        using var root = Root(b.Hive);
        if (b.Existed)
        {
            using var key = root.CreateSubKey(b.KeyPath, writable: true)!;
            var kind = Enum.TryParse<RegistryValueKind>(b.Kind, out var k) ? k : RegistryValueKind.String;
            object data = kind switch
            {
                RegistryValueKind.DWord => int.Parse(b.Data ?? "0"),
                RegistryValueKind.QWord => long.Parse(b.Data ?? "0"),
                RegistryValueKind.MultiString => (b.Data ?? "").Split('\0'),
                RegistryValueKind.Binary => Convert.FromBase64String(b.Data ?? ""),
                _ => b.Data ?? "",
            };
            key.SetValue(b.ValueName, data, kind);
            return;
        }

        bool used = keyStillUsed(b);
        if (b.CreatedRoot != null && !used)
        {
            root.DeleteSubKeyTree(b.CreatedRoot, throwOnMissingSubKey: false);
            return;
        }
        using (var key = root.OpenSubKey(b.KeyPath, writable: true))
        {
            if (key == null) return;
            var fb = used ? fallbackValue?.Invoke(b) : null;
            if (fb != null) key.SetValue(b.ValueName, fb.Value.Data, fb.Value.Kind);
            else key.DeleteValue(b.ValueName, throwOnMissingValue: false);
        }
    }

    private static string? FirstMissingKey(RegistryKey root, string keyPath)
    {
        var parts = keyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var p in parts)
        {
            current = current.Length == 0 ? p : current + "\\" + p;
            using var k = root.OpenSubKey(current);
            if (k == null) return current;
        }
        return null;
    }
}
