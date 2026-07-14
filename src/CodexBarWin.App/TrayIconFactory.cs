using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace CodexBarWin.App;

/// <summary>
/// Loads the app's tray icon from the bundled <c>assets/usagebar.ico</c> file
/// (copied next to the exe at build time). Falls back to a small runtime-drawn
/// icon if the asset is missing for any reason (e.g. a stripped-down copy of
/// the output directory).
/// </summary>
internal static class TrayIconFactory
{
    public static Icon CreateIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "assets", "usagebar.ico");
        if (File.Exists(icoPath))
        {
            try
            {
                return new Icon(icoPath);
            }
            catch
            {
                // Fall through to the runtime-drawn fallback below.
            }
        }

        return CreateFallbackIcon();
    }

    private static Icon CreateFallbackIcon()
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
