using System.Drawing;
using System.Drawing.Imaging;
using IcoSwap.Core.Apply;
using IcoSwap.Core.Icons;
using IcoSwap.Core.Native;
using IcoSwap.Core.Targets;
using Microsoft.Win32;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IcoSwap.Core.Tests;

/// <summary>Применение и откат на реальных объектах, созданных тестом (временные ярлыки, папки, тестовое расширение).</summary>
public class BackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "icoswap_bk_" + Guid.NewGuid().ToString("N"));
    private readonly string _ico;
    private readonly ApplyEngine _engine;
    private const string TestExt = ".icoswaptest";

    public BackupTests()
    {
        Directory.CreateDirectory(_dir);
        AppPaths.Root = Path.Combine(_dir, "home");
        using var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.SteelBlue);
        var png = Path.Combine(_dir, "i.png");
        bmp.Save(png, ImageFormat.Png);
        _ico = Path.Combine(_dir, "i.ico");
        File.WriteAllBytes(_ico, IcoConverter.RasterToIco(png));
        _engine = new ApplyEngine(new BackupStore(Path.Combine(_dir, "state.json"))) { RefreshShell = false };
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + TestExt, false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\IcoSwapTest.File", false);
        try
        {
            foreach (var d in Directory.GetDirectories(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(d, FileAttributes.Normal);
            foreach (var f in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch { }
    }

    [Fact]
    public void Shortcut_ApplyAndRestore_IsByteExact()
    {
        var lnk = Path.Combine(_dir, "Test App.lnk");
        ShellLink.Create(lnk, Path.Combine(Environment.SystemDirectory, "notepad.exe"), Path.Combine(Environment.SystemDirectory, "shell32.dll"), 3);
        var original = File.ReadAllBytes(lnk);
        var target = TargetCatalog.CreateShortcutTarget(lnk, "test")!;

        var r = _engine.Apply(new[] { new ApplyRequest(target, _ico) });
        Assert.Empty(r.Failed);
        using (var l = ShellLink.Open(lnk)) Assert.Equal(_ico, l.IconLocation.Path);

        // Повторное применение не затирает исходный бэкап
        _engine.Apply(new[] { new ApplyRequest(target, _ico) });
        Assert.Equal(original, Convert.FromBase64String(_engine.Store.Get(target.Id)!.File!.ContentBase64!));

        r = _engine.Restore(new[] { target.Id });
        Assert.Empty(r.Failed);
        Assert.Equal(original, File.ReadAllBytes(lnk));
        Assert.Null(_engine.Store.Get(target.Id));
    }

    [Fact]
    public void UrlShortcut_ApplyAndRestore()
    {
        var url = Path.Combine(_dir, "Game.url");
        File.WriteAllText(url, "[InternetShortcut]\r\nURL=steam://rungameid/1\r\nIconFile=C:\\old.ico\r\nIconIndex=0\r\n");
        var original = File.ReadAllBytes(url);
        var target = TargetCatalog.CreateShortcutTarget(url, "test")!;
        Assert.Empty(_engine.Apply(new[] { new ApplyRequest(target, _ico) }).Failed);
        Assert.Contains("IconFile=" + _ico, File.ReadAllText(url));
        Assert.Empty(_engine.Restore(new[] { target.Id }).Failed);
        Assert.Equal(original, File.ReadAllBytes(url));
    }

    [Fact]
    public void Folder_DesktopIni_CreatedAndRemoved()
    {
        var folder = Path.Combine(_dir, "MyFolder");
        Directory.CreateDirectory(folder);
        var attrsBefore = File.GetAttributes(folder);
        var target = TargetCatalog.CreateFolderTarget(folder);

        Assert.Empty(_engine.Apply(new[] { new ApplyRequest(target, _ico) }).Failed);
        var ini = Path.Combine(folder, "desktop.ini");
        Assert.Contains("IconResource=" + _ico + ",0", File.ReadAllText(ini));
        Assert.True(File.GetAttributes(ini).HasFlag(FileAttributes.Hidden | FileAttributes.System));
        Assert.True(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));

        Assert.Empty(_engine.Restore(new[] { target.Id }).Failed);
        Assert.False(File.Exists(ini));
        Assert.Equal(attrsBefore, File.GetAttributes(folder));
    }

    [Fact]
    public void Folder_ExistingDesktopIni_PreservesOtherLines()
    {
        var folder = Path.Combine(_dir, "Localized");
        Directory.CreateDirectory(folder);
        var ini = Path.Combine(folder, "desktop.ini");
        File.WriteAllText(ini, "[.ShellClassInfo]\r\nLocalizedResourceName=@shell32.dll,-21798\r\nIconResource=old.ico,1\r\n[ViewState]\r\nMode=\r\n", System.Text.Encoding.Unicode);
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
        var original = File.ReadAllBytes(ini);
        var target = TargetCatalog.CreateFolderTarget(folder);

        Assert.Empty(_engine.Apply(new[] { new ApplyRequest(target, _ico) }).Failed);
        var text = File.ReadAllText(ini);
        Assert.Contains("LocalizedResourceName=@shell32.dll,-21798", text);
        Assert.Contains("[ViewState]", text);
        Assert.DoesNotContain("old.ico", text);
        Assert.Single(text.Split('\n'), l => l.StartsWith("IconResource="));

        Assert.Empty(_engine.Restore(new[] { target.Id }).Failed);
        Assert.Equal(original, File.ReadAllBytes(ini));
    }

    [Fact]
    public void FileType_WritesHkcuAndCleansUp()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + TestExt)) k.SetValue("", "IcoSwapTest.File");
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\IcoSwapTest.File")) k.SetValue("", "Test file");
        var catalog = new TargetCatalog();
        var target = catalog.GetFileType(TestExt)!;
        Assert.NotNull(target);

        Assert.Empty(_engine.Apply(new[] { new ApplyRequest(target, _ico) }).Failed);
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\IcoSwapTest.File\DefaultIcon"))
            Assert.Equal(_ico + ",0", k!.GetValue(""));

        Assert.Empty(_engine.Restore(new[] { target.Id }).Failed);
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\IcoSwapTest.File\DefaultIcon"))
            Assert.Null(k); // ключа не было — он удалён
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\IcoSwapTest.File"))
            Assert.NotNull(k); // а родительский ProgID остался
    }

    [Fact]
    public void FileType_RestoresPreviousValue()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + TestExt)) k.SetValue("", "IcoSwapTest.File");
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\IcoSwapTest.File\DefaultIcon"))
            k.SetValue("", "%SystemRoot%\\system32\\imageres.dll,-102", RegistryValueKind.ExpandString);
        var target = new TargetCatalog().GetFileType(TestExt)!;

        _engine.Apply(new[] { new ApplyRequest(target, _ico) });
        _engine.Restore(new[] { target.Id });
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\IcoSwapTest.File\DefaultIcon")!;
        Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind(""));
        Assert.Equal("%SystemRoot%\\system32\\imageres.dll,-102", key.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
    }

    [Fact]
    public void RestoreAll_RevertsEverything_AndStateSurvivesReload()
    {
        var lnk = Path.Combine(_dir, "A.lnk");
        ShellLink.Create(lnk, Path.Combine(Environment.SystemDirectory, "notepad.exe"));
        var original = File.ReadAllBytes(lnk);
        var folder = Path.Combine(_dir, "F");
        Directory.CreateDirectory(folder);

        _engine.Apply(new[]
        {
            new ApplyRequest(TargetCatalog.CreateShortcutTarget(lnk)!, _ico),
            new ApplyRequest(TargetCatalog.CreateFolderTarget(folder), _ico),
        });

        // Новый экземпляр (как после перезапуска приложения) видит бэкап
        var engine2 = new ApplyEngine(new BackupStore(_engine.Store.FilePath)) { RefreshShell = false };
        Assert.Equal(2, engine2.Store.All().Count);
        var r = engine2.RestoreAll();
        Assert.Equal(2, r.Succeeded.Count);
        Assert.Equal(original, File.ReadAllBytes(lnk));
        Assert.False(File.Exists(Path.Combine(folder, "desktop.ini")));
        Assert.Empty(engine2.Store.All());
    }

    [Fact]
    public void FailedApply_LeavesNoBackupRecord()
    {
        var target = TargetCatalog.CreateFolderTarget(Path.Combine(_dir, "missing"));
        var r = _engine.Apply(new[] { new ApplyRequest(target, _ico) });
        Assert.Single(r.Failed);
        Assert.Empty(_engine.Store.All());
    }

    [Fact]
    public void AdminItems_SkippedWithoutRunner()
    {
        var target = new IconTarget { Id = "shell:folder", Kind = TargetKind.ShellIcon, DisplayName = "Папка", RequiresAdmin = true };
        if (ApplyEngine.IsElevated) return; // в админской сессии пункт применился бы напрямую
        var r = _engine.Apply(new[] { new ApplyRequest(target, _ico) });
        Assert.Single(r.SkippedAdmin);
        Assert.Empty(_engine.Store.All());
    }

    [Theory]
    [InlineData("shell:folder", true)]
    [InlineData("ext:.txt", false)]
    [InlineData(@"lnk:C:\Windows\System32\evil.lnk", false)]
    public void ElevatedJob_AllowsOnlyAdminTargets(string id, bool allowed) =>
        Assert.Equal(allowed, ElevatedJob.IsAllowedElevated(id));

    [Fact]
    public void ElevatedJob_AllowsCommonStartMenu()
    {
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        Assert.True(ElevatedJob.IsAllowedElevated("lnk:" + Path.Combine(common, "App.lnk")));
        Assert.False(ElevatedJob.IsAllowedElevated("lnk:" + Path.Combine(common, "App.exe")));
    }
}
