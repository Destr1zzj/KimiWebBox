using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Generates a multi-resolution app.ico (PNG-compressed entries) for KimiWebBox.
var output = args.Length > 0 ? args[0] : "app.ico";
int[] sizes = [256, 48, 32, 16];
var pngs = sizes.Select(RenderIcon).ToArray();

using (var fs = File.Create(output))
using (var bw = new BinaryWriter(fs))
{
    bw.Write((ushort)0);                 // reserved
    bw.Write((ushort)1);                 // type: icon
    bw.Write((ushort)pngs.Length);       // count
    var offset = 6 + 16 * pngs.Length;
    for (var i = 0; i < pngs.Length; i++)
    {
        bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // width  (0 = 256)
        bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // height (0 = 256)
        bw.Write((byte)0);               // palette
        bw.Write((byte)0);               // reserved
        bw.Write((ushort)1);             // color planes
        bw.Write((ushort)32);            // bpp
        bw.Write(pngs[i].Length);        // data size
        bw.Write(offset);                // data offset
        offset += pngs[i].Length;
    }
    foreach (var p in pngs) bw.Write(p);
}
Console.WriteLine("wrote " + output);

static byte[] RenderIcon(int size)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);

    var radius = size * 0.22f;
    using var path = RoundedRect(0.5f, 0.5f, size - 1f, size - 1f, radius);
    using var bg = new SolidBrush(Color.FromArgb(255, 45, 108, 223)); // #2D6CDF
    g.FillPath(bg, path);

    using var font = new Font("Segoe UI", size * 0.56f, FontStyle.Bold, GraphicsUnit.Pixel);
    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    g.DrawString("K", font, Brushes.White, new RectangleF(0, -size * 0.02f, size, size), sf);

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
{
    var path = new GraphicsPath();
    path.AddArc(x, y, r * 2, r * 2, 180, 90);
    path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
    path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
    path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
    path.CloseFigure();
    return path;
}
