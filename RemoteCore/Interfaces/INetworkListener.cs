using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteCore.Interfaces
{
    public interface INetworkListener : IDisposable
    {
        event Action<Socket>? ConnectionAccepted;
        int Port { get; }
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync();
    }
}
