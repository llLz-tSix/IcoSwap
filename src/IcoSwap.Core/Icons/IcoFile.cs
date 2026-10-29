using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IcoSwap.Core.Icons;

/// <summary>Чтение и запись .ico (кадры PNG и 32-бит BMP).</summary>
public static class IcoFile
{
    public sealed record Entry(int Width, int Height, int BitCount, byte[] Data)
    {
        public bool IsPng => Data.Length > 8 && Data[0] == 0x89 && Data[1] == 0x50 && Data[2] == 0x4E && Data[3] == 0x47;
    }

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static bool LooksLikeIco(byte[] data) =>
        data.Length >= 6 + 16 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0 && BitConverter.ToUInt16(data, 4) > 0;

    public static bool LooksLikePng(byte[] data) => data.Length > 8 && data.AsSpan(0, 8).SequenceEqual(PngSignature);

    public static List<Entry> Read(byte[] data)
    {
        if (!LooksLikeIco(data)) throw new InvalidDataException("Файл не является .ico");
        int count = BitConverter.ToUInt16(data, 4);
        var list = new List<Entry>(count);
        for (int i = 0; i < count; i++)
        {
            int off = 6 + i * 16;
            if (off + 16 > data.Length) break;
            int w = data[off] == 0 ? 256 : data[off];
            int h = data[off + 1] == 0 ? 256 : data[off + 1];
            int bpp = BitConverter.ToUInt16(data, off + 6);
            int size = BitConverter.ToInt32(data, off + 8);
            int dataOff = BitConverter.ToInt32(data, off + 12);
            if (size <= 0 || dataOff < 0 || dataOff + size > data.Length) continue;
            var bytes = data.AsSpan(dataOff, size).ToArray();
            var e = new Entry(w, h, bpp, bytes);
            if (e.IsPng && (w == 256 || h == 256))
            {
                // реальный размер PNG-кадра берём из IHDR
                if (bytes.Length > 24)
                {
                    int pw = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                    int ph = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                    if (pw > 0 && ph > 0) e = e with { Width = pw, Height = ph };
                }
            }
            list.Add(e);
        }
        if (list.Count == 0) throw new InvalidDataException("В .ico нет корректных кадров");
        return list;
    }

    /// <summary>Записывает .ico. Кадры больше 48px — PNG, меньше — 32-бит BMP (максимальная совместимость).</summary>
    public static byte[] Write(IEnumerable<Bitmap> frames)
    {
        var entries = new List<(int size, byte[] data)>();
        foreach (var bmp in frames.OrderBy(f => f.Width))
        {
            int s = bmp.Width;
            entries.Add((s, s > 48 ? EncodePng(bmp) : EncodeBmp(bmp)));
        }
        return WriteRaw(entries.Select(e => new Entry(e.size, e.size, 32, e.data)));
    }

    public static byte[] WriteRaw(IEnumerable<Entry> entries)
    {
        var list = entries.ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)list.Count);
        int offset = 6 + 16 * list.Count;
        foreach (var e in list)
        {
            w.Write((byte)(e.Width >= 256 ? 0 : e.Width));
            w.Write((byte)(e.Height >= 256 ? 0 : e.Height));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)(e.BitCount == 0 ? 32 : e.BitCount));
            w.Write(e.Data.Length);
            w.Write(offset);
            offset += e.Data.Length;
        }
        foreach (var e in list) w.Write(e.Data);
        w.Flush();
        return ms.ToArray();
    }

    public static byte[] EncodePng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] EncodeBmp(Bitmap src)
    {
        int s = src.Width;
        var pixels = GetArgb(src);
        int maskStride = ((s + 31) / 32) * 4;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        // BITMAPINFOHEADER, высота удвоена (XOR + AND маска)
        w.Write(40); w.Write(s); w.Write(s * 2);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write(0); w.Write(s * s * 4 + maskStride * s);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = s - 1; y >= 0; y--)
            for (int x = 0; x < s; x++)
                w.Write(pixels[y * s + x]); // BGRA little-endian == ARGB int
        for (int y = s - 1; y >= 0; y--)
        {
            var row = new byte[maskStride];
            for (int x = 0; x < s; x++)
                if (((uint)pixels[y * s + x] >> 24) == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
            w.Write(row);
        }
        w.Flush();
        return ms.ToArray();
    }

    internal static int[] GetArgb(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var result = new int[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, result, y * bmp.Width, bmp.Width);
            return result;
        }
        finally { bmp.UnlockBits(data); }
    }

    /// <summary>Декодирует самый крупный кадр .ico в Bitmap (для превью и пересборки).</summary>
    public static Bitmap DecodeLargest(byte[] icoData)
    {
        var entries = Read(icoData);
        var best = entries.OrderByDescending(e => e.Width * e.Height).ThenByDescending(e => e.BitCount).First();
        return DecodeEntry(best, icoData);
    }

    public static Bitmap DecodeEntry(Entry e, byte[]? wholeIco = null)
    {
        if (e.IsPng)
        {
            using var ms = new MemoryStream(e.Data);
            using var img = new Bitmap(ms);
            return new Bitmap(img); // отвязываем от потока
        }
        int biBit = BitConverter.ToUInt16(e.Data, 14);
        if (biBit == 32) return DecodeBmp32(e.Data);

        // Палитровые и 24-битные кадры — через стандартный декодер
        var single = WriteRaw(new[] { e });
        using var ims = new MemoryStream(single);
        using var icon = new Icon(ims);
        return icon.ToBitmap();
    }

    private static Bitmap DecodeBmp32(byte[] dib)
    {
        int headerSize = BitConverter.ToInt32(dib, 0);
        int w = BitConverter.ToInt32(dib, 4);
        int h = Math.Abs(BitConverter.ToInt32(dib, 8)) / 2;
        int maskStride = ((w + 31) / 32) * 4;
        int xorOff = headerSize;
        int andOff = xorOff + w * h * 4;
        var px = new int[w * h];
        bool anyAlpha = false;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int v = BitConverter.ToInt32(dib, xorOff + ((h - 1 - y) * w + x) * 4);
                px[y * w + x] = v;
                if (((uint)v >> 24) != 0) anyAlpha = true;
            }
        if (!anyAlpha && andOff + maskStride * h <= dib.Length)
        {
            // старый формат: альфа отсутствует, прозрачность из AND-маски
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool transparent = (dib[andOff + (h - 1 - y) * maskStride + x / 8] & (0x80 >> (x % 8))) != 0;
                    px[y * w + x] = transparent ? 0 : (px[y * w + x] | unchecked((int)0xFF000000));
                }
        }
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++) Marshal.Copy(px, y * w, data.Scan0 + y * data.Stride, w);
        bmp.UnlockBits(data);
        return bmp;
    }
}
