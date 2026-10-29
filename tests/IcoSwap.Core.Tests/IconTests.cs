using System.Drawing;
using System.Drawing.Imaging;
using IcoSwap.Core.Icons;
using IcoSwap.Core.Packs;

namespace IcoSwap.Core.Tests;

public class IconTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "icoswap_tests_" + Guid.NewGuid().ToString("N"));

    public IconTests()
    {
        Directory.CreateDirectory(_dir);
        AppPaths.Root = Path.Combine(_dir, "home");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string MakePng(string name, int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.FillEllipse(Brushes.OrangeRed, 0, 0, w - 1, h - 1);
        }
        var path = Path.Combine(_dir, name);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void Png_ToMultiSizeIco()
    {
        var ico = IcoConverter.RasterToIco(MakePng("a.png", 512, 300));
        var entries = IcoFile.Read(ico);
        Assert.Equal(IcoConverter.IcoSizes, entries.Select(e => e.Width).OrderBy(x => x).ToArray());
        Assert.All(entries, e => Assert.Equal(e.Width, e.Height));
        // большие кадры — PNG, маленькие — BMP
        Assert.True(entries.Single(e => e.Width == 256).IsPng);
        Assert.False(entries.Single(e => e.Width == 16).IsPng);

        // Читается стандартным декодером Windows
        using var ms = new MemoryStream(ico);
        using var icon = new Icon(ms, 32, 32);
        Assert.Equal(32, icon.Width);
    }

    [Fact]
    public void Ico_DecodeKeepsAlpha()
    {
        var ico = IcoConverter.RasterToIco(MakePng("b.png", 64, 64));
        var small = IcoFile.Read(ico).Single(e => e.Width == 32);
        using var bmp = IcoFile.DecodeEntry(small);
        Assert.Equal(0, bmp.GetPixel(0, 0).A);      // угол прозрачный
        Assert.Equal(255, bmp.GetPixel(16, 16).A);  // центр непрозрачный
    }

    [Fact]
    public void Svg_ToIco()
    {
        var svg = Path.Combine(_dir, "s.svg");
        File.WriteAllText(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 50"><rect width="100" height="50" fill="#2a7"/></svg>""");
        var entries = IcoFile.Read(IcoConverter.SvgToIco(svg));
        Assert.Equal(9, entries.Count);
        using var big = IcoFile.DecodeEntry(entries.Single(e => e.Width == 256));
        Assert.Equal(0, big.GetPixel(128, 10).A);    // поля сверху (картинка 2:1 по центру)
        Assert.Equal(255, big.GetPixel(128, 128).A);
    }

    [Fact]
    public void PeLibrary_ExtractsIcons()
    {
        var dll = Path.Combine(Environment.SystemDirectory, "imageres.dll");
        var icons = IconLibraryReader.Extract(dll);
        Assert.True(icons.Count > 50);
        Assert.All(icons.Take(5), i => IcoFile.Read(i.IcoData));
    }

    [Fact]
    public void NeLibrary_ExtractsNamedIcons()
    {
        // Собираем минимальный 16-битный .icl с одной группой "CHROME"
        var icoBytes = IcoConverter.RasterToIco(MakePng("c.png", 32, 32));
        var frame = IcoFile.Read(icoBytes).Single(e => e.Width == 32).Data;
        var icl = BuildNe(frame, "CHROME");
        var path = Path.Combine(_dir, "test.icl");
        File.WriteAllBytes(path, icl);

        var icons = IconLibraryReader.Extract(path);
        var single = Assert.Single(icons);
        Assert.Equal("CHROME", single.Name);
        Assert.Equal(32, IcoFile.Read(single.IcoData).Single().Width);
    }

    private static byte[] BuildNe(byte[] iconData, string groupName)
    {
        const int shift = 4;
        var f = new byte[0x200 + 0x40 + ((iconData.Length + 15) & ~15) + 0x40];
        f[0] = (byte)'M'; f[1] = (byte)'Z';
        BitConverter.GetBytes(0x40).CopyTo(f, 0x3C);
        int ne = 0x40;
        f[ne] = (byte)'N'; f[ne + 1] = (byte)'E';
        int resRel = 0x40;
        BitConverter.GetBytes((ushort)resRel).CopyTo(f, ne + 0x24);
        int rt = ne + resRel;
        int iconOff = 0x200, grpOff = 0x200 + ((iconData.Length + 15) & ~15);
        var grp = new byte[6 + 14];
        BitConverter.GetBytes((ushort)1).CopyTo(grp, 2);
        BitConverter.GetBytes((ushort)1).CopyTo(grp, 4);
        grp[6] = 32; grp[7] = 32;
        BitConverter.GetBytes((ushort)1).CopyTo(grp, 6 + 4);
        BitConverter.GetBytes((ushort)32).CopyTo(grp, 6 + 6);
        BitConverter.GetBytes(iconData.Length).CopyTo(grp, 6 + 8);
        BitConverter.GetBytes((ushort)1).CopyTo(grp, 6 + 12);
        iconData.CopyTo(f, iconOff);
        grp.CopyTo(f, grpOff);

        int p = rt;
        BitConverter.GetBytes((ushort)shift).CopyTo(f, p); p += 2;
        // RT_ICON
        BitConverter.GetBytes((ushort)0x8003).CopyTo(f, p); BitConverter.GetBytes((ushort)1).CopyTo(f, p + 2); p += 8;
        BitConverter.GetBytes((ushort)(iconOff >> shift)).CopyTo(f, p);
        BitConverter.GetBytes((ushort)((iconData.Length + 15) >> shift)).CopyTo(f, p + 2);
        BitConverter.GetBytes((ushort)0x8001).CopyTo(f, p + 6); p += 12;
        // RT_GROUP_ICON с именем
        BitConverter.GetBytes((ushort)0x800E).CopyTo(f, p); BitConverter.GetBytes((ushort)1).CopyTo(f, p + 2); p += 8;
        int nameRel = (p + 12 + 2) - rt; // строка имени после таблицы и терминатора
        BitConverter.GetBytes((ushort)(grpOff >> shift)).CopyTo(f, p);
        BitConverter.GetBytes((ushort)((grp.Length + 15) >> shift)).CopyTo(f, p + 2);
        BitConverter.GetBytes((ushort)nameRel).CopyTo(f, p + 6); p += 12;
        p += 2; // терминатор типов (0)
        f[p] = (byte)groupName.Length;
        System.Text.Encoding.ASCII.GetBytes(groupName).CopyTo(f, p + 1);
        return f;
    }

    [Fact]
    public void PackLoader_ImportsFolder_And_Zip()
    {
        var src = Path.Combine(_dir, "pack");
        Directory.CreateDirectory(Path.Combine(src, "drives"));
        File.Copy(MakePng("chrome.png", 128, 128), Path.Combine(src, "chrome.png"));
        File.Copy(MakePng("c.png", 64, 64), Path.Combine(src, "drives", "c.png"));
        File.WriteAllText(Path.Combine(src, "broken.ico"), "not an icon");
        File.WriteAllText(Path.Combine(src, "manifest.json"), """{ "name": "My Pack", "map": { "chrome.png": "ext:.txt" } }""");

        var pack = PackLoader.Import(src);
        Assert.Equal("My Pack", pack.Name);
        Assert.Equal(2, pack.Icons.Count);
        Assert.Contains(pack.Warnings, w => w.Contains("broken.ico"));
        Assert.All(pack.Icons, i => Assert.True(File.Exists(i.IcoPath) && File.Exists(i.PreviewPath)));
        Assert.StartsWith(AppPaths.Packs, pack.Directory);
        Assert.Equal(new[] { "drives" }, pack.Icons.Single(i => i.Name == "c").Hints);
        Assert.NotNull(pack.Manifest);

        // Пак работает и после удаления исходной папки; повторный импорт берёт кэш
        var zip = Path.Combine(_dir, "pack.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(src, zip, System.IO.Compression.CompressionLevel.Fastest, includeBaseDirectory: true);
        Directory.Delete(src, true);
        var fromZip = PackLoader.Import(zip);
        Assert.Equal(pack.Directory, fromZip.Directory);
        Assert.Equal(2, fromZip.Icons.Count);
    }
}
