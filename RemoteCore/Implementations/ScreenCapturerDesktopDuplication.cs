using RemoteCore.Interfaces;
using System.Threading.Tasks;
using System.Drawing;

namespace RemoteCore.Implementations
{
    // Simpler implementation using existing Screen capture util for now.
    public class ScreenCapturerDesktopDuplication : IScreenCapturer
    {
        public Task<Bitmap> CaptureAsync()
        {
            // Use existing ScreenStreamer capture method but return Bitmap instead of bytes
            var screenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            var bmp = new Bitmap(screenBounds.Width, screenBounds.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
            }
            return Task.FromResult(bmp);
        }
    }
}
