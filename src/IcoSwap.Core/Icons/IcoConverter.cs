using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Svg;

namespace IcoSwap.Core.Icons;

/// <summary>Конвертация PNG/BMP/JPG/SVG в многоразмерный .ico и генерация превью.</summary>
public static class IcoConverter
{
    public static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    public static byte[] RasterToIco(string imagePath)
    {
        using var src = LoadRaster(imagePath);
        using var square = MakeSquare(src);
        var frames = IcoSizes.Select(s => Resize(square, s)).ToList();
        try { return IcoFile.Write(frames); }
        finally { frames.ForEach(f => f.Dispose()); }
    }

    public static byte[] SvgToIco(string svgPath)
    {
        var doc = OpenSvg(svgPath);
        var frames = IcoSizes.Select(s => RenderSvg(doc, s)).ToList();
        try { return IcoFile.Write(frames); }
        finally { frames.ForEach(f => f.Dispose()); }
    }

    /// <summary>Нормализует существующий .ico: если это на самом деле PNG — конвертирует.</summary>
    public static byte[] NormalizeIco(string icoPath)
    {
        var data = File.ReadAllBytes(icoPath);
        if (IcoFile.LooksLikeIco(data))
        {
            IcoFile.Read(data); // валидация
            return data;
        }
        if (IcoFile.LooksLikePng(data)) return RasterToIco(icoPath);
        throw new InvalidDataException("Повреждённый .ico");
    }

    public static Bitmap PreviewFromIco(byte[] icoData, int size)
    {
        using var largest = IcoFile.DecodeLargest(icoData);
        using var sq = MakeSquare(largest);
        return Resize(sq, size);
    }

    public static void SavePng(Bitmap bmp, string path) => bmp.Save(path, ImageFormat.Png);

    private static Bitmap LoadRaster(string path)
    {
        // Читаем через память, чтобы не держать файл открытым
        var bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream(bytes);
        using var img = Image.FromStream(ms, useEmbeddedColorManagement: true, validateImageData: true);
        var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(img, 0, 0, img.Width, img.Height);
        return bmp;
    }

    private static SvgDocument OpenSvg(string path)
    {
        // Запрещаем загрузку внешних ресурсов/сущностей из SVG
        SvgDocument.ResolveExternalElements = ExternalType.None;
        SvgDocument.ResolveExternalImages = ExternalType.None;
        SvgDocument.ResolveExternalXmlEntites = ExternalType.None;
        return SvgDocument.Open<SvgDocument>(path);
    }

    private static Bitmap RenderSvg(SvgDocument doc, int size)
    {
        var dims = doc.GetDimensions();
        float w = dims.Width > 0 ? dims.Width : size, h = dims.Height > 0 ? dims.Height : size;
        float scale = Math.Min(size / w, size / h);
        int rw = Math.Max(1, (int)Math.Round(w * scale)), rh = Math.Max(1, (int)Math.Round(h * scale));
        using var rendered = doc.Draw(rw, rh);
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.Transparent);
        g.DrawImage(rendered, (size - rw) / 2, (size - rh) / 2, rw, rh);
        return result;
    }

    /// <summary>Дополняет неквадратное изображение прозрачными полями до квадрата.</summary>
    internal static Bitmap MakeSquare(Bitmap src)
    {
        int s = Math.Max(src.Width, src.Height);
        var result = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.Transparent);
        g.DrawImageUnscaled(src, (s - src.Width) / 2, (s - src.Height) / 2);
        return result;
    }

    /// <summary>Качественное масштабирование: при сильном уменьшении — ступенчато по 2x, затем бикубически.</summary>
    internal static Bitmap Resize(Bitmap src, int size)
    {
        Bitmap current = src;
        bool owned = false;
        while (current.Width / 2 >= size * 2)
        {
            var half = Draw(current, current.Width / 2);
            if (owned) current.Dispose();
            current = half;
            owned = true;
        }
        var result = Draw(current, size);
        if (owned) current.Dispose();
        return result;
    }

    private static Bitmap Draw(Bitmap src, int size)
    {
        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        return dst;
    }
}
