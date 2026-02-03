using RemoteCore.Interfaces;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace RemoteCore.Implementations
{
    public class FrameStreamer : IFrameStreamer
    {
        private readonly IScreenCapturer _capturer;
        private readonly IFrameEncoder _encoder;
        private readonly ConnectionLogger _logger;

        public FrameStreamer(IScreenCapturer capturer, IFrameEncoder encoder, ConnectionLogger logger)
        {
            _capturer = capturer;
            _encoder = encoder;
            _logger = logger;
        }

        public async Task StreamToAsync(Socket socket, CancellationToken cancellationToken)
        {
            try
            {
                using var peer = new TcpPeer(socket, _logger);
                var msg = await peer.ReceiveAsync();
                _logger.Log("Received: " + msg);
                if (msg != "REQUEST_STREAM")
                {
                    await peer.SendAsync("WELCOME");
                    return;
                }

                _logger.Log("Starting stream to " + socket.RemoteEndPoint);

                while (socket.Connected && !cancellationToken.IsCancellationRequested)
                {
                    using var bmp = await _capturer.CaptureAsync();
                    var jpg = await _encoder.EncodeAsync(bmp);
                    var header = System.Text.Encoding.ASCII.GetBytes(jpg.Length.ToString("D8"));
                    await peer.SendAsync(System.Text.Encoding.ASCII.GetString(header));
                    var sent = 0;
                    while (sent < jpg.Length)
                    {
                        sent += await socket.SendAsync(new ArraySegment<byte>(jpg, sent, jpg.Length - sent), SocketFlags.None);
                    }
                    await Task.Delay(200, cancellationToken).ContinueWith(_ => { });
                }

                _logger.Log("Finished streaming or client disconnected.");
            }
            catch (Exception ex)
            {
                _logger.Log("Stream error: " + ex.Message);
            }
        }
    }
}
