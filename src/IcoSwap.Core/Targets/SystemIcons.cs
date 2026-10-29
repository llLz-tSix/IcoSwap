using IcoSwap.Core.Matching;

namespace IcoSwap.Core.Targets;

/// <summary>Описание системных значков (CLSID и имена значений DefaultIcon) и особых папок.</summary>
public static class SystemIcons
{
    public sealed record SysIcon(string Key, string Title, string Synonym, (string Clsid, string ValueName)[] Values);

    public const string ThisPcClsid = "{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    public const string RecycleBinClsid = "{645FF040-5081-101B-9F08-00AA002F954E}";
    public const string NetworkClsid = "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
    public const string UserFilesClsid = "{59031a47-3f72-44a7-89c5-5595fe6b30ee}";
    public const string ControlPanelClsid = "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}";
    public const string ControlPanelAllClsid = "{21EC2020-3AEA-1069-A2DD-08002B30309D}";

    public static readonly SysIcon[] All =
    {
        new("thispc", "Этот компьютер", Synonyms.ThisPc, new[] { (ThisPcClsid, "") }),
        new("recyclebin-empty", "Корзина (пустая)", Synonyms.RecycleBinEmpty, new[] { (RecycleBinClsid, "empty") }),
        new("recyclebin-full", "Корзина (полная)", Synonyms.RecycleBinFull, new[] { (RecycleBinClsid, "full") }),
        new("network", "Сеть", Synonyms.Network, new[] { (NetworkClsid, "") }),
        new("userfiles", "Файлы пользователя", Synonyms.UserFiles, new[] { (UserFilesClsid, "") }),
        new("controlpanel", "Панель управления", Synonyms.ControlPanel, new[] { (ControlPanelClsid, ""), (ControlPanelAllClsid, "") }),
    };

    public static SysIcon? Find(string key) => All.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public sealed record KnownFolderDef(string Key, string Title, string Synonym, Guid FolderId);

    public static readonly KnownFolderDef[] KnownFolders =
    {
        new("desktop", "Рабочий стол", Synonyms.Desktop, new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641")),
        new("documents", "Документы", Synonyms.Documents, new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7")),
        new("downloads", "Загрузки", Synonyms.Downloads, new Guid("374DE290-123F-4565-9164-39C4925E467B")),
        new("pictures", "Изображения", Synonyms.Pictures, new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB")),
        new("music", "Музыка", Synonyms.Music, new Guid("4BD8D571-6D19-48D3-BE97-422220080E43")),
        new("videos", "Видео", Synonyms.Videos, new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC")),
    };

    public static KnownFolderDef? FindKnownFolder(string key) =>
        KnownFolders.FirstOrDefault(k => k.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static string? GetKnownFolderPath(KnownFolderDef def)
    {
        try { return Native.NativeMethods.SHGetKnownFolderPath(def.FolderId, 0, IntPtr.Zero); }
        catch { return null; }
    }

    /// <summary>Значения "3" (закрытая) и "4" (открытая папка) в HKLM\...\Explorer\Shell Icons.</summary>
    public const string ShellIconsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
}
