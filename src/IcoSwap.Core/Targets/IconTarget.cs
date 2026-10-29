namespace IcoSwap.Core.Targets;

public enum TargetKind
{
    SystemIcon = 0,
    KnownFolder = 1,
    ShellIcon = 2,
    Drive = 3,
    FileType = 4,
    Shortcut = 5,
    Folder = 6,
}

/// <summary>Имя, по которому цель сопоставляется с иконкой. ExactOnly отключает нечёткое сравнение.</summary>
public readonly record struct NameAlias(string Name, double Weight = 1.0, bool ExactOnly = false);

/// <summary>
/// Объект системы, у которого можно сменить иконку. Id самоописывающий
/// (lnk:путь, ext:.txt, sys:thispc, kf:downloads, dir:путь, drive:C, shell:folder),
/// чтобы по нему одному можно было применить и откатить изменение (в т.ч. в процессе с правами админа).
/// </summary>
public sealed class IconTarget
{
    public required string Id { get; init; }
    public required TargetKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public string? Location { get; init; }
    public IReadOnlyList<NameAlias> Aliases { get; init; } = Array.Empty<NameAlias>();
    public bool RequiresAdmin { get; init; }
    public string? Note { get; init; }

    /// <summary>Отмечать ли пункт в превью по умолчанию (false — Windows, скорее всего, не покажет иконку).</summary>
    public bool DefaultEnabled { get; init; } = true;

    /// <summary>Путь в файловой системе (ярлык, папка, диск), если есть — нужен для превью и обновления Проводника.</summary>
    public string? Path { get; init; }

    public override string ToString() => $"{Kind}: {DisplayName}" + (Location != null ? $" ({Location})" : "");

    public static string KindTitle(TargetKind kind) => kind switch
    {
        TargetKind.SystemIcon => "Системные значки",
        TargetKind.KnownFolder => "Особые папки",
        TargetKind.ShellIcon => "Все папки",
        TargetKind.Drive => "Диски",
        TargetKind.FileType => "Типы файлов",
        TargetKind.Shortcut => "Ярлыки",
        TargetKind.Folder => "Папки",
        _ => kind.ToString(),
    };
}
