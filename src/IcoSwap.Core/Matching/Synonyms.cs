namespace IcoSwap.Core.Matching;

/// <summary>
/// Словарь синонимов: канонический ключ → варианты имён (RU/EN, имена exe, имена тем freedesktop).
/// Все варианты нормализуются так же, как имена иконок, и сравниваются в слитной форме.
/// </summary>
public static class Synonyms
{
    // ---- системные объекты ----
    public const string ThisPc = "thispc";
    public const string RecycleBinEmpty = "recyclebin";
    public const string RecycleBinFull = "recyclebinfull";
    public const string Network = "network";
    public const string UserFiles = "userfiles";
    public const string ControlPanel = "controlpanel";
    public const string Documents = "documents";
    public const string Downloads = "downloads";
    public const string Pictures = "pictures";
    public const string Music = "music";
    public const string Videos = "videos";
    public const string Desktop = "desktop";
    public const string Folder = "folder";
    public const string FolderOpen = "folderopen";
    public const string Drive = "drive";
    public const string SystemDrive = "systemdrive";
    public const string RemovableDrive = "removabledrive";
    public const string OpticalDrive = "opticaldrive";

    public static readonly IReadOnlyDictionary<string, string[]> Groups = new Dictionary<string, string[]>
    {
        [ThisPc] = new[] { "this pc", "my computer", "computer", "mycomputer", "pc", "this computer", "workstation",
            "этот компьютер", "мой компьютер", "компьютер" },
        [RecycleBinEmpty] = new[] { "recycle bin", "recycle bin empty", "recyclebin empty", "trash", "trash empty", "empty trash",
            "bin", "bin empty", "empty bin", "trashcan", "trash can", "user trash", "recycler", "recycle", "waste basket", "wastebasket",
            "корзина", "корзина пустая", "пустая корзина" },
        [RecycleBinFull] = new[] { "recycle bin full", "recyclebin full", "full recycle bin", "trash full", "full trash", "bin full",
            "full bin", "trashcan full", "trash can full", "user trash full", "recycle full", "wastebasket full",
            "корзина полная", "полная корзина" },
        [Network] = new[] { "network", "network places", "my network places", "network neighborhood", "network workgroup", "networking",
            "сеть", "сетевое окружение" },
        [UserFiles] = new[] { "user", "user files", "user folder", "userfolder", "user home", "home", "my files", "profile", "user profile",
            "пользователь", "файлы пользователя", "папка пользователя" },
        [ControlPanel] = new[] { "control panel", "control", "controlpanel", "preferences system", "панель управления" },

        [Documents] = new[] { "documents", "my documents", "docs", "folder documents", "documents folder", "folder docs",
            "документы", "мои документы", "папка документы" },
        [Downloads] = new[] { "downloads", "download", "folder download", "folder downloads", "downloads folder",
            "загрузки", "папка загрузки" },
        [Pictures] = new[] { "pictures", "images", "photos", "my pictures", "folder pictures", "folder images", "pictures folder",
            "изображения", "картинки", "фото", "папка изображения" },
        [Music] = new[] { "music", "my music", "folder music", "music folder", "audio folder", "музыка", "папка музыка" },
        [Videos] = new[] { "videos", "video", "movies", "my videos", "folder videos", "folder video", "videos folder",
            "видео", "папка видео" },
        [Desktop] = new[] { "desktop", "user desktop", "folder desktop", "desktop folder", "рабочий стол" },
        [Folder] = new[] { "folder", "folder closed", "closed folder", "dir", "directory", "folder default", "default folder",
            "generic folder", "папка", "закрытая папка" },
        [FolderOpen] = new[] { "folder open", "open folder", "folder opened", "opened folder", "открытая папка", "папка открытая" },

        [Drive] = new[] { "drive", "hdd", "hard drive", "harddrive", "harddisk", "hard disk", "drive harddisk", "disk", "local disk",
            "local drive", "fixed drive", "ssd", "диск", "локальный диск", "жесткий диск" },
        [SystemDrive] = new[] { "system drive", "windows drive", "sys drive", "drive system", "drive windows", "os drive",
            "системный диск" },
        [RemovableDrive] = new[] { "removable", "removable drive", "drive removable", "drive removable media", "usb", "usb drive",
            "flash", "flash drive", "pendrive", "usb stick", "флешка", "съемный диск" },
        [OpticalDrive] = new[] { "cd", "dvd", "cdrom", "cd rom", "dvd rom", "optical", "drive optical", "optical drive", "bluray",
            "дисковод", "привод" },

        // ---- популярные программы ----
        ["chrome"] = new[] { "chrome", "google chrome", "googlechrome", "хром", "гугл хром" },
        ["firefox"] = new[] { "firefox", "mozilla firefox", "файрфокс" },
        ["edge"] = new[] { "edge", "microsoft edge", "msedge", "ms edge" },
        ["opera"] = new[] { "opera", "opera browser", "браузер opera", "опера", "opera stable" },
        ["operagx"] = new[] { "opera gx", "operagx", "opera gx browser", "браузер opera gx" },
        ["yandex"] = new[] { "yandex", "yandex browser", "яндекс", "яндекс браузер", "браузер яндекс" },
        ["brave"] = new[] { "brave", "brave browser" },
        ["vivaldi"] = new[] { "vivaldi" },
        ["tor"] = new[] { "tor browser", "torbrowser", "tor" },
        ["word"] = new[] { "word", "microsoft word", "winword", "ms word", "ворд" },
        ["excel"] = new[] { "excel", "microsoft excel", "ms excel", "эксель" },
        ["powerpoint"] = new[] { "powerpoint", "microsoft powerpoint", "powerpnt", "ms powerpoint" },
        ["outlook"] = new[] { "outlook", "microsoft outlook", "outlook classic", "new outlook" },
        ["onenote"] = new[] { "onenote", "microsoft onenote", "onenote for windows 10" },
        ["access"] = new[] { "access", "microsoft access", "msaccess" },
        ["teams"] = new[] { "teams", "microsoft teams", "ms teams", "msteams", "teams classic" },
        ["onedrive"] = new[] { "onedrive", "microsoft onedrive" },
        ["skype"] = new[] { "skype", "скайп" },
        ["vscode"] = new[] { "vscode", "vs code", "visual studio code", "code" },
        ["visualstudio"] = new[] { "visual studio", "devenv", "visual studio 2022", "visual studio 2019", "vs2022", "vs2019" },
        ["notepad"] = new[] { "notepad", "блокнот" },
        ["notepadpp"] = new[] { "notepadpp", "notepad++", "notepad plus plus", "notepad plus" },
        ["sublime"] = new[] { "sublime text", "sublime", "sublime text 3", "sublime text 4", "sublimetext" },
        ["telegram"] = new[] { "telegram", "telegram desktop", "телеграм", "телеграмм" },
        ["discord"] = new[] { "discord", "дискорд" },
        ["whatsapp"] = new[] { "whatsapp", "whats app", "ватсап" },
        ["viber"] = new[] { "viber", "вайбер" },
        ["zoom"] = new[] { "zoom", "zoom workplace", "zoom meetings" },
        ["slack"] = new[] { "slack" },
        ["steam"] = new[] { "steam", "стим" },
        ["epicgames"] = new[] { "epic games", "epic games launcher", "epicgameslauncher", "epic" },
        ["battlenet"] = new[] { "battle net", "battlenet", "blizzard battle net" },
        ["eaapp"] = new[] { "ea", "ea app", "origin", "ea desktop" },
        ["ubisoft"] = new[] { "ubisoft connect", "uplay", "ubisoft" },
        ["gog"] = new[] { "gog galaxy", "gog", "galaxyclient" },
        ["spotify"] = new[] { "spotify", "спотифай" },
        ["vlc"] = new[] { "vlc", "vlc media player", "vlc player" },
        ["obs"] = new[] { "obs", "obs studio", "obs64", "obs32" },
        ["audacity"] = new[] { "audacity" },
        ["gimp"] = new[] { "gimp", "gimp 2", "gimp 2 10" },
        ["blender"] = new[] { "blender" },
        ["photoshop"] = new[] { "photoshop", "adobe photoshop", "фотошоп" },
        ["illustrator"] = new[] { "illustrator", "adobe illustrator" },
        ["premiere"] = new[] { "premiere", "premiere pro", "adobe premiere pro", "adobe premiere" },
        ["aftereffects"] = new[] { "after effects", "adobe after effects", "afterfx" },
        ["acrobat"] = new[] { "acrobat", "adobe acrobat", "acrobat reader", "adobe reader", "adobe acrobat reader", "acrord32", "acrobat dc" },
        ["7zip"] = new[] { "7zip", "7 zip", "7zfm", "7zip file manager", "7 zip file manager" },
        ["winrar"] = new[] { "winrar", "rar", "винрар" },
        ["qbittorrent"] = new[] { "qbittorrent", "qbit torrent" },
        ["utorrent"] = new[] { "utorrent", "µtorrent", "mu torrent" },
        ["thunderbird"] = new[] { "thunderbird", "mozilla thunderbird" },
        ["calculator"] = new[] { "calculator", "calc", "калькулятор" },
        ["paint"] = new[] { "paint", "mspaint", "ms paint", "paint 3d" },
        ["explorer"] = new[] { "explorer", "file explorer", "windows explorer", "проводник", "file manager" },
        ["cmd"] = new[] { "cmd", "command prompt", "командная строка", "console" },
        ["powershell"] = new[] { "powershell", "windows powershell", "pwsh", "powershell 7" },
        ["terminal"] = new[] { "terminal", "windows terminal", "wt", "терминал" },
        ["taskmanager"] = new[] { "task manager", "taskmgr", "диспетчер задач" },
        ["settings"] = new[] { "settings", "windows settings", "параметры", "настройки" },
        ["store"] = new[] { "store", "microsoft store", "ms store", "магазин" },
        ["roblox"] = new[] { "roblox", "roblox player", "robloxplayer", "роблокс" },
        ["robloxstudio"] = new[] { "roblox studio", "robloxstudio", "robloxstudiobeta" },
        ["minecraft"] = new[] { "minecraft", "minecraft launcher", "майнкрафт" },
        ["intellij"] = new[] { "intellij", "intellij idea", "idea64", "idea" },
        ["pycharm"] = new[] { "pycharm", "pycharm64", "pycharm community", "pycharm professional" },
        ["androidstudio"] = new[] { "android studio", "studio64" },
        ["git"] = new[] { "git", "git bash", "git gui" },
        ["github"] = new[] { "github desktop", "github" },
        ["python"] = new[] { "python", "idle", "python idle" },
        ["virtualbox"] = new[] { "virtualbox", "oracle vm virtualbox", "virtual box" },
        ["anydesk"] = new[] { "anydesk" },
        ["teamviewer"] = new[] { "teamviewer", "team viewer" },
        ["nvidia"] = new[] { "nvidia", "nvidia app", "geforce experience", "nvidia control panel" },
        ["itunes"] = new[] { "itunes", "apple music" },
        ["libreoffice"] = new[] { "libreoffice", "libre office", "soffice" },
        ["foobar"] = new[] { "foobar", "foobar2000" },
        ["aimp"] = new[] { "aimp" },
        ["potplayer"] = new[] { "potplayer", "daum potplayer", "potplayermini64" },
        ["mpc"] = new[] { "mpc hc", "mpc be", "media player classic", "mpc hc64" },
        ["wmp"] = new[] { "windows media player", "wmplayer", "media player" },
        ["photos"] = new[] { "photos app", "microsoft photos", "windows photos", "фотографии" },
        ["mail"] = new[] { "mail", "windows mail", "почта" },
        ["calendar"] = new[] { "calendar", "календарь" },
        ["snippingtool"] = new[] { "snipping tool", "ножницы", "snippingtool" },
        ["wordpad"] = new[] { "wordpad" },
        ["regedit"] = new[] { "regedit", "registry editor", "редактор реестра" },
        ["claude"] = new[] { "claude", "anthropic claude" },
        ["chatgpt"] = new[] { "chatgpt", "chat gpt", "openai" },
        ["cursor"] = new[] { "cursor", "cursor ai" },
        ["windhawk"] = new[] { "windhawk" },
    };

    private static readonly Dictionary<string, string> Index = BuildIndex();

    private static Dictionary<string, string> BuildIndex()
    {
        var idx = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, aliases) in Groups)
        {
            idx.TryAdd(key, key);
            foreach (var a in aliases)
            {
                var c = NameNormalizer.Compact(a);
                if (c.Length > 0) idx.TryAdd(c, key);
            }
        }
        return idx;
    }

    /// <summary>Канонический ключ для слитной формы имени или null.</summary>
    public static string? Lookup(string compact) =>
        compact.Length > 0 && Index.TryGetValue(compact, out var key) ? key : null;
}
