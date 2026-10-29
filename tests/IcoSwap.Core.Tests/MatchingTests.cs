using IcoSwap.Core.Matching;
using IcoSwap.Core.Packs;
using IcoSwap.Core.Targets;

namespace IcoSwap.Core.Tests;

public class NormalizerTests
{
    [Theory]
    [InlineData("chrome_256", "chrome")]
    [InlineData("Folder-48x48", "folder")]
    [InlineData("RecycleBinFull", "recyclebinfull")]
    [InlineData("recycle-bin-full", "recyclebinfull")]
    [InlineData("Google Chrome icon", "googlechrome")]
    [InlineData("notepad++", "notepadpp")]
    [InlineData("Мой_Компьютер", "мойкомпьютер")]
    [InlineData("icon@2x", "icon")]
    public void Compact_Normalizes(string raw, string expected) =>
        Assert.Equal(expected, NameNormalizer.Compact(raw));

    [Theory]
    [InlineData("This PC", Synonyms.ThisPc)]
    [InlineData("мой компьютер", Synonyms.ThisPc)]
    [InlineData("user-trash-full", Synonyms.RecycleBinFull)]
    [InlineData("корзина", Synonyms.RecycleBinEmpty)]
    [InlineData("Загрузки", Synonyms.Downloads)]
    [InlineData("folder-documents", Synonyms.Documents)]
    [InlineData("Google Chrome", "chrome")]
    [InlineData("Visual Studio Code", "vscode")]
    [InlineData("Браузер Opera", "opera")]
    public void Synonyms_Resolve(string raw, string key) =>
        Assert.Equal(key, NameInfo.From(raw).Canonical);

    [Fact]
    public void JaroWinkler_Basics()
    {
        Assert.Equal(1.0, NameNormalizer.JaroWinkler("telegram", "telegram"));
        Assert.True(NameNormalizer.JaroWinkler("telegram", "telegramm") > 0.95);
        Assert.True(NameNormalizer.JaroWinkler("chrome", "spotify") < 0.6);
    }
}

public class MatcherTests
{
    private static IconTarget Lnk(string name, string? exe = null) => new()
    {
        Id = $"lnk:C:\\fake\\{name}.lnk",
        Kind = TargetKind.Shortcut,
        DisplayName = name,
        Aliases = exe == null ? new[] { new NameAlias(name) } : new[] { new NameAlias(name), new NameAlias(exe) },
    };

    private static TargetCatalog Catalog()
    {
        var c = new TargetCatalog();
        foreach (var s in SystemIcons.All)
            c.Targets.Add(new IconTarget { Id = "sys:" + s.Key, Kind = TargetKind.SystemIcon, DisplayName = s.Title, Aliases = new[] { new NameAlias(Synonyms.Groups[s.Synonym][0]) } });
        foreach (var k in SystemIcons.KnownFolders)
            c.Targets.Add(new IconTarget { Id = "kf:" + k.Key, Kind = TargetKind.KnownFolder, DisplayName = k.Title, Aliases = new[] { new NameAlias(Synonyms.Groups[k.Synonym][0]) } });
        c.Targets.Add(new IconTarget { Id = "shell:folder", Kind = TargetKind.ShellIcon, DisplayName = "Папка", Aliases = new[] { new NameAlias("folder") } });
        c.Targets.Add(new IconTarget { Id = "shell:folder-open", Kind = TargetKind.ShellIcon, DisplayName = "Открытая", Aliases = new[] { new NameAlias("folder open") } });
        foreach (var letter in new[] { "C", "D" })
            c.Targets.Add(new IconTarget
            {
                Id = "drive:" + letter, Kind = TargetKind.Drive, DisplayName = "Диск " + letter, Location = "Локальный",
                Aliases = new[] { new NameAlias("drive " + letter.ToLower(), 1, true), new NameAlias("диск " + letter.ToLower(), 1, true), new NameAlias("drive", 0.95) },
            });
        c.Targets.Add(Lnk("Google Chrome", "chrome"));
        c.Targets.Add(Lnk("Uninstall Google Chrome", "uninstall"));
        c.Targets.Add(Lnk("Notepad++", "notepad++"));
        c.Targets.Add(Lnk("Notepad", "notepad"));
        c.Targets.Add(Lnk("Roblox Studio", "RobloxStudioBeta"));
        c.Targets.Add(Lnk("Roblox Player", "RobloxPlayerBeta"));
        c.Targets.Add(Lnk("Telegram Desktop", "Telegram"));
        c.Targets.Add(Lnk("Documents"));
        c.Targets.Add(Lnk("Visual Studio Code", "Code"));
        c.Targets.Add(Lnk("Network Monitor", "netmon"));
        return c;
    }

    private static IconPack Pack(params string[] names) => new()
    {
        Name = "test",
        Directory = "C:\\fake",
        Icons = names.Select(n =>
        {
            var parts = n.Split('/');
            return new PackIcon { Name = parts[^1], IcoPath = $"C:\\fake\\{parts[^1]}.ico", PreviewPath = "", Source = n + ".ico", Hints = parts[..^1].ToList() };
        }).ToList(),
    };

    private static Dictionary<string, string> Run(params string[] icons)
    {
        var plan = Matcher.Build(Pack(icons), Catalog());
        return plan.Items.ToDictionary(i => i.Target.Id, i => i.Icon.Name);
    }

    [Fact]
    public void Chrome_MatchesShortcut_NotUninstaller()
    {
        var m = Run("chrome");
        Assert.Equal("chrome", m[@"lnk:C:\fake\Google Chrome.lnk"]);
        Assert.False(m.ContainsKey(@"lnk:C:\fake\Uninstall Google Chrome.lnk"));
    }

    [Fact]
    public void FileType_Txt()
    {
        var m = Run("txt", "file-pdf");
        Assert.Equal("txt", m["ext:.txt"]);
        if (FileTypeRegistry.IsRegistered(".pdf") && !FileTypeRegistry.IsPerFileIcon(".pdf"))
            Assert.Equal("file-pdf", m["ext:.pdf"]);
    }

    [Fact]
    public void RecycleBin_FullAndEmpty()
    {
        var m = Run("recycle-bin-full", "trash");
        Assert.Equal("recycle-bin-full", m["sys:recyclebin-full"]);
        Assert.Equal("trash", m["sys:recyclebin-empty"]);
    }

    [Fact]
    public void Russian_Names()
    {
        var m = Run("Этот компьютер", "Корзина полная", "Загрузки", "сеть");
        Assert.Equal("Этот компьютер", m["sys:thispc"]);
        Assert.Equal("Корзина полная", m["sys:recyclebin-full"]);
        Assert.Equal("Загрузки", m["kf:downloads"]);
        Assert.Equal("сеть", m["sys:network"]);
    }

    [Fact]
    public void Documents_GoesToKnownFolder_NotShortcut()
    {
        var m = Run("documents");
        Assert.Equal("documents", m["kf:documents"]);
        Assert.False(m.ContainsKey(@"lnk:C:\fake\Documents.lnk"));
    }

    [Fact]
    public void Network_DoesNotGrabUnrelatedShortcut()
    {
        var m = Run("network");
        Assert.Equal("network", m["sys:network"]);
        Assert.False(m.ContainsKey(@"lnk:C:\fake\Network Monitor.lnk"));
    }

    [Fact]
    public void Folder_And_FolderOpen()
    {
        var m = Run("folder", "folder-open");
        Assert.Equal("folder", m["shell:folder"]);
        Assert.Equal("folder-open", m["shell:folder-open"]);
    }

    [Fact]
    public void Drives_SpecificBeatsGeneric()
    {
        var m = Run("drive", "drive-c");
        Assert.Equal("drive-c", m["drive:C"]);
        Assert.Equal("drive", m["drive:D"]);
    }

    [Fact]
    public void Drives_FromSubfolderHint()
    {
        var m = Run("drives/d");
        Assert.Equal("d", m["drive:D"]);
    }

    [Fact]
    public void NotepadPlusPlus_IsNotNotepad()
    {
        var m = Run("notepad++");
        Assert.Equal("notepad++", m[@"lnk:C:\fake\Notepad++.lnk"]);
        Assert.False(m.ContainsKey(@"lnk:C:\fake\Notepad.lnk"));
    }

    [Fact]
    public void Roblox_SpecificIconsWin()
    {
        var m = Run("roblox", "roblox-studio");
        Assert.Equal("roblox-studio", m[@"lnk:C:\fake\Roblox Studio.lnk"]);
        Assert.Equal("roblox", m[@"lnk:C:\fake\Roblox Player.lnk"]);
    }

    [Fact]
    public void Fuzzy_And_Synonyms_ForApps()
    {
        var m = Run("telegram", "vscode");
        Assert.Equal("telegram", m[@"lnk:C:\fake\Telegram Desktop.lnk"]);
        Assert.Equal("vscode", m[@"lnk:C:\fake\Visual Studio Code.lnk"]);
    }

    [Fact]
    public void Unknown_IsReportedAsUnmatched()
    {
        var plan = Matcher.Build(Pack("chrome", "zzzqqq-unknown"), Catalog());
        Assert.Contains(plan.Unmatched, u => u.Name == "zzzqqq-unknown");
        Assert.DoesNotContain(plan.Unmatched, u => u.Name == "chrome");
    }

    [Fact]
    public void Manifest_OverridesAutoMatching()
    {
        var pack = Pack("mystery", "chrome");
        var manifest = PackManifest.Parse("""
            {
              // комментарии допускаются
              "name": "Test",
              "map": {
                "mystery.ico": ["system:thispc", "drive:all"],
                "chrome.ico": "shortcut:Notepad*"
              }
            }
            """);
        var withManifest = new IconPack { Name = pack.Name, Directory = pack.Directory, Icons = pack.Icons, Manifest = manifest };
        var m = Matcher.Build(withManifest, Catalog()).Items.ToDictionary(i => i.Target.Id, i => i.Icon.Name);
        Assert.Equal("mystery", m["sys:thispc"]);
        Assert.Equal("mystery", m["drive:C"]);
        Assert.Equal("mystery", m["drive:D"]);
        Assert.Equal("chrome", m[@"lnk:C:\fake\Notepad.lnk"]);
        Assert.Equal("chrome", m[@"lnk:C:\fake\Notepad++.lnk"]);
        Assert.False(m.ContainsKey(@"lnk:C:\fake\Google Chrome.lnk")); // в манифесте явно указано иначе
    }
}
