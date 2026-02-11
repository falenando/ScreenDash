using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteCore.Interfaces
{
    public interface IFrameStreamer
    {
        Task StreamToAsync(Socket socket, CancellationToken cancellationToken, bool skipHandshake = false);
    }
}
