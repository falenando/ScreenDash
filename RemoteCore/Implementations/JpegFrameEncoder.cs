using RemoteCore.Interfaces;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace RemoteCore.Implementations
{
    public class JpegFrameEncoder : IFrameEncoder
    {
        private readonly int _quality;
        private readonly int _maxWidth;

        public JpegFrameEncoder(int quality = 60, int maxWidth = 1024)
        {
            _quality = quality;
            _maxWidth = maxWidth;
        }

        public Task<byte[]> EncodeAsync(Bitmap bmp)
        {
            // resize if necessary
            Image final = bmp;
            if (bmp.Width > _maxWidth)
            {
                var ratio = (double)_maxWidth / bmp.Width;
                var newW = _maxWidth;
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
                encParams.Param[0] = new EncoderParameter(Encoder.Quality, _quality);
                final.Save(ms, codec, encParams);
                return Task.FromResult(ms.ToArray());
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
