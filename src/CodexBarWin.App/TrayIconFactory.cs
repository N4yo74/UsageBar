using System.Drawing;
using System.Drawing.Drawing2D;

namespace CodexBarWin.App;

/// <summary>
/// Generates a tiny tray icon at runtime (no .ico asset needed). Created once for the
/// lifetime of the process, so the small GDI handle leak from Icon.FromHandle is fine.
/// </summary>
internal static class TrayIconFactory
{
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var circleBrush = new SolidBrush(Color.FromArgb(255, 91, 140, 255));
            g.FillEllipse(circleBrush, 1, 1, 14, 14);

            using var font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString("C", font, textBrush, new RectangleF(0, 0, 16, 16), format);
        }

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}
