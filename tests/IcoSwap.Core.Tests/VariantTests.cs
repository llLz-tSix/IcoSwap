using System.Drawing;
using System.Drawing.Imaging;
using IcoSwap.Core.Packs;

namespace IcoSwap.Core.Tests;

public class VariantTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "icoswap_var_" + Guid.NewGuid().ToString("N"));

    public VariantTests()
    {
        Directory.CreateDirectory(_dir);
        AppPaths.Root = Path.Combine(_dir, "home");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Theory]
    [InlineData("Windows Icons/Night/chrome.ico", true, 0)]
    [InlineData("Windows Icons/Day/chrome.ico", true, 2)]
    [InlineData("Windows Icons/Day/chrome.ico", false, 0)]
    [InlineData("PNGS/Dark Mode/chrome.png", true, 0)]
    [InlineData("Светлая/chrome.png", true, 2)]
    [InlineData("apps/chrome.png", true, 1)]
    [InlineData("chrome.png", false, 1)]
    public void VariantRank_DetectsDayNightFolders(string rel, bool preferDark, int expected) =>
        Assert.Equal(expected, PackLoader.VariantRank(rel, preferDark));

    [Fact]
    public void Import_PicksVariantForCurrentTheme()
    {
        var src = Path.Combine(_dir, "pack");
        SavePng(Path.Combine(src, "Day", "chrome.png"), Color.White);
        SavePng(Path.Combine(src, "Night", "chrome.png"), Color.Black);

        var pack = PackLoader.Import(src);
        var icon = Assert.Single(pack.Icons);
        bool dark = PackLoader.SystemUsesDarkTheme();
        Assert.Equal(dark ? "Night/chrome.png" : "Day/chrome.png", icon.Source);
        Assert.EndsWith(dark ? "_dark" : "_light", pack.Directory);
        Assert.Contains(pack.Warnings, w => w.Contains("вариант"));
    }

    private static void SavePng(string path, Color color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(color);
        bmp.Save(path, ImageFormat.Png);
    }
}
