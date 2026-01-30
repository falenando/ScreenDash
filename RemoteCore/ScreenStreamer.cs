using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace RemoteCore
{
    public static class ScreenStreamer
    {
        public static byte[] CaptureJpegBytes(int quality = 60, int maxWidth = 1024)
        {
            // capture primary screen
            var screenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            using var bmp = new Bitmap(screenBounds.Width, screenBounds.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
            }

            Image final = bmp;
            // resize if wider than maxWidth
            if (bmp.Width > maxWidth)
            {
                var ratio = (double)maxWidth / bmp.Width;
                var newW = maxWidth;
                var newH = (int)(bmp.Height * ratio);
                var resized = new Bitmap(newW, newH);
                using (var g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bmp, 0, 0, newW, newH);
                }
                final = resized;
            }

            try
            {
                using var ms = new MemoryStream();
                var codec = GetJpegCodec();
                var encParams = new EncoderParameters(1);
                encParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                final.Save(ms, codec, encParams);
                return ms.ToArray();
            }
            finally
            {
                if (!ReferenceEquals(final, bmp))
                    final.Dispose();
            }
        }

        private static ImageCodecInfo GetJpegCodec()
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            foreach (var c in codecs)
                if (c.FormatID == ImageFormat.Jpeg.Guid)
                    return c;
            return codecs[0];
        }
    }
}
