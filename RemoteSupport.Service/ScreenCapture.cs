using System.Drawing;
using System.Drawing.Imaging;

namespace RemoteSupport.Service;

public static class ScreenCapture
{
    public static byte[]? CaptureJpeg()
    {
        try
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1024, 768);
            using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
