using System.Threading.Tasks;
using System.Drawing;

namespace RemoteCore.Interfaces
{
    public interface IFrameEncoder
    {
        /// <summary>
        /// Encode bitmap to a byte[] (e.g., JPEG). Caller retains ownership of bitmap.
        /// </summary>
        Task<byte[]> EncodeAsync(Bitmap bmp);
    }
}
