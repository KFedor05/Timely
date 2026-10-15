using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Draws the Timely app icon and packs every size into one .ico.
//
// A ring alone disappears at 16px, so the mark is a solid disc with clock
// hands punched out of it — legible at taskbar size, still clean at 256.
//
// Sizes up to 64 are stored as classic 32bpp DIB frames rather than PNG:
// GDI+ and several shell paths cannot decode PNG-compressed frames at small
// sizes, and silently fall back to a scaled mess.
internal static class Program
{
    private static readonly Color Accent = Color.FromArgb(255, 79, 209, 197);
    private static readonly Color Ink = Color.FromArgb(255, 12, 24, 28);

    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    private const int PngFrom = 128;

    private static void Main(string[] args)
    {
        var target = args.Length > 0
            ? args[0]
            : Path.Combine(Directory.GetCurrentDirectory(), "timely.ico");

        var bitmaps = new List<Bitmap>();
        foreach (var size in Sizes) bitmaps.Add(Render(size));

        var frames = new List<byte[]>();
        for (var i = 0; i < Sizes.Length; i++)
        {
            frames.Add(Sizes[i] >= PngFrom
                ? EncodePng(bitmaps[i])
                : EncodeDib(bitmaps[i]));
        }

        WriteIco(target, Sizes, frames);
        Console.WriteLine($"{target}: {Sizes.Length} sizes, {new FileInfo(target).Length} bytes");

        if (args.Length > 1)
        {
            WritePreview(args[1], bitmaps);
            Console.WriteLine($"{args[1]}: preview strip");
        }

        foreach (var bitmap in bitmaps) bitmap.Dispose();
    }

    private static Bitmap Render(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // A hair of padding so the disc does not touch the edges.
        var pad = size * 0.06f;
        var box = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);

        using (var fill = new SolidBrush(Accent))
            g.FillEllipse(fill, box);

        var centre = new PointF(size / 2f, size / 2f);
        var radius = box.Width / 2f;

        // Hands thicken slightly at small sizes so they survive rasterising.
        var thickness = Math.Max(1.4f, size * 0.075f);
        using var pen = new Pen(Ink, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        // Hour hand straight up, minute hand out to the right.
        g.DrawLine(pen, centre, new PointF(centre.X, centre.Y - radius * 0.46f));
        g.DrawLine(pen, centre, new PointF(centre.X + radius * 0.60f, centre.Y));

        return bitmap;
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>
    /// A 32bpp bottom-up DIB with a BITMAPINFOHEADER whose height is doubled to
    /// cover the (unused, all-zero) AND mask that the icon format still demands.
    /// </summary>
    private static byte[] EncodeDib(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;

        var maskStride = ((width + 31) / 32) * 4;
        var maskSize = maskStride * height;
        var pixelSize = width * height * 4;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(40);                       // biSize
        writer.Write(width);                    // biWidth
        writer.Write(height * 2);               // biHeight: colour + mask
        writer.Write((short)1);                 // biPlanes
        writer.Write((short)32);                // biBitCount
        writer.Write(0);                        // biCompression: BI_RGB
        writer.Write(pixelSize + maskSize);     // biSizeImage
        writer.Write(0);                        // biXPelsPerMeter
        writer.Write(0);                        // biYPelsPerMeter
        writer.Write(0);                        // biClrUsed
        writer.Write(0);                        // biClrImportant

        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[width * 4];
            // DIB rows run bottom-up.
            for (var y = height - 1; y >= 0; y--)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    data.Scan0 + y * data.Stride, row, 0, row.Length);
                writer.Write(row);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        // AND mask: all zero means "take every pixel", alpha does the rest.
        writer.Write(new byte[maskSize]);

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteIco(string path, int[] sizes, List<byte[]> frames)
    {
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);

        writer.Write((short)0);            // reserved
        writer.Write((short)1);            // type: icon
        writer.Write((short)frames.Count);

        // Directory entries are fixed width, so images start after all of them.
        var offset = 6 + 16 * frames.Count;
        for (var i = 0; i < frames.Count; i++)
        {
            var size = sizes[i];
            writer.Write((byte)(size >= 256 ? 0 : size));   // 0 means 256
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);         // palette size
            writer.Write((byte)0);         // reserved
            writer.Write((short)1);        // colour planes
            writer.Write((short)32);       // bits per pixel
            writer.Write(frames[i].Length);
            writer.Write(offset);
            offset += frames[i].Length;
        }

        foreach (var frame in frames) writer.Write(frame);
    }

    private static void WritePreview(string path, List<Bitmap> bitmaps)
    {
        const int Pad = 16;
        var width = Pad;
        foreach (var bitmap in bitmaps) width += bitmap.Width + Pad;

        using var strip = new Bitmap(width, 256 + Pad * 2);
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(255, 34, 34, 38));
            var x = Pad;
            foreach (var bitmap in bitmaps)
            {
                g.DrawImage(bitmap, x, (strip.Height - bitmap.Height) / 2,
                    bitmap.Width, bitmap.Height);
                x += bitmap.Width + Pad;
            }
        }

        strip.Save(path, ImageFormat.Png);
    }
}
