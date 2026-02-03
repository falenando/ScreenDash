using RemoteCore.Interfaces;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteCore.Implementations
{
    public class TcpNetworkListener : INetworkListener
    {
        private readonly int _port;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;

        public event Action<Socket>? ConnectionAccepted;

        public TcpNetworkListener(int port)
        {
            _port = port;
        }

        public int Port => _port;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var socket = await _listener.AcceptSocketAsync();
                    ConnectionAccepted?.Invoke(socket);
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception) { }
        }

        public Task StopAsync()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
            }
            catch { }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
        }
    }
}
