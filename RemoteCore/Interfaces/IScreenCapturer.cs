using System.Threading.Tasks;
using System.Drawing;

namespace RemoteCore.Interfaces
{
    public interface IScreenCapturer
    {
        /// <summary>
        /// Capture current screen and return a Bitmap. Caller is responsible for disposing.
        /// </summary>
        Task<Bitmap> CaptureAsync();
    }
}
