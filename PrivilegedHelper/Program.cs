using System.Threading;

namespace PrivilegedHelper;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Placeholder loop; real implementation will handle capture/IPC.
        Thread.Sleep(Timeout.Infinite);
    }
}
