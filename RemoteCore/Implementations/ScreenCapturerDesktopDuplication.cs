using RemoteCore.Interfaces;
using System.Threading.Tasks;
using System.Drawing;
using System.Runtime.InteropServices;

namespace RemoteCore.Implementations
{
    // Simpler implementation using existing Screen capture util for now.
    public class ScreenCapturerDesktopDuplication : IScreenCapturer
    {
        public Task<Bitmap> CaptureAsync()
        {
            IntPtr inputDesktop = IntPtr.Zero;
            IntPtr originalDesktop = IntPtr.Zero;
            var threadId = GetCurrentThreadId();

            try
            {
                originalDesktop = GetThreadDesktop(threadId);
                inputDesktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP | DESKTOP_READOBJECTS | DESKTOP_WRITEOBJECTS);

                if (inputDesktop != IntPtr.Zero && inputDesktop != originalDesktop)
                {
                    SetThreadDesktop(inputDesktop);
                }

                var screenBounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                var bmp = new Bitmap(screenBounds.Width, screenBounds.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
                }
                return Task.FromResult(bmp);
            }
            finally
            {
                if (originalDesktop != IntPtr.Zero)
                {
                    SetThreadDesktop(originalDesktop);
                }

                if (inputDesktop != IntPtr.Zero && inputDesktop != originalDesktop)
                {
                    CloseDesktop(inputDesktop);
                }
            }
        }

        private const uint DESKTOP_READOBJECTS = 0x0001;
        private const uint DESKTOP_WRITEOBJECTS = 0x0080;
        private const uint DESKTOP_SWITCHDESKTOP = 0x0100;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(uint dwThreadId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
