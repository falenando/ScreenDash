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
            var openedDesktop = IntPtr.Zero;
            var threadId = GetCurrentThreadId();

            try
            {
                originalDesktop = GetThreadDesktop(threadId);
                inputDesktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP | DESKTOP_READOBJECTS | DESKTOP_WRITEOBJECTS);
                if (inputDesktop == IntPtr.Zero)
                {
                    inputDesktop = OpenDesktop("Winlogon", 0, false, DESKTOP_SWITCHDESKTOP | DESKTOP_READOBJECTS | DESKTOP_WRITEOBJECTS);
                }

                if (inputDesktop != IntPtr.Zero && inputDesktop != originalDesktop)
                {
                    if (SetThreadDesktop(inputDesktop))
                    {
                        openedDesktop = inputDesktop;
                    }
                    else
                    {
                        CloseDesktop(inputDesktop);
                    }
                }

                var screenBounds = GetVirtualScreenBounds();
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

                if (openedDesktop != IntPtr.Zero)
                {
                    CloseDesktop(openedDesktop);
                }
            }
        }

        private const uint DESKTOP_READOBJECTS = 0x0001;
        private const uint DESKTOP_WRITEOBJECTS = 0x0080;
        private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        private static Rectangle GetVirtualScreenBounds()
        {
            var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (width <= 0 || height <= 0)
            {
                x = 0;
                y = 0;
                width = GetSystemMetrics(SM_CXSCREEN);
                height = GetSystemMetrics(SM_CYSCREEN);
            }

            if (width <= 0)
                width = 1;
            if (height <= 0)
                height = 1;

            return new Rectangle(x, y, width, height);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(uint dwThreadId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
    }
}
