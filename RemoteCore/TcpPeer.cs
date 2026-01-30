using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RemoteCore
{
    public class TcpPeer : IDisposable
    {
        private readonly Socket _socket;
        private readonly ConnectionLogger _logger;

        public TcpPeer(Socket socket, ConnectionLogger logger)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public EndPoint RemoteEndPoint => _socket.RemoteEndPoint;

        public async Task SendAsync(string text)
        {
            var data = Encoding.UTF8.GetBytes(text);
            var sent = 0;
            while (sent < data.Length)
            {
                sent += await _socket.SendAsync(new ArraySegment<byte>(data, sent, data.Length - sent), SocketFlags.None);
            }
        }

        public async Task<string> ReceiveAsync(int bufferSize = 8192)
        {
            var buf = new byte[bufferSize];
            var received = await _socket.ReceiveAsync(buf, SocketFlags.None);
            if (received == 0)
                return string.Empty;
            return Encoding.UTF8.GetString(buf, 0, received);
        }

        public void Dispose()
        {
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
        }
    }
}
