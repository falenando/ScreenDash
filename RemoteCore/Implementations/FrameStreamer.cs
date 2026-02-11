using RemoteCore.Interfaces;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Text;

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

        public async Task StreamToAsync(Socket socket, CancellationToken cancellationToken, bool skipHandshake = false)
        {
            try
            {
                using var peer = new TcpPeer(socket, _logger);

                if (!skipHandshake)
                {
                    var msg = await NetworkHelper.ReceiveLineAsync(socket, cancellationToken);
                    _logger.Log("Received: " + msg);
                    if (!string.Equals(msg, "REQUEST_STREAM", StringComparison.Ordinal))
                    {
                        await peer.SendAsync("WELCOME");
                        return;
                    }
                }

                _logger.Log("Starting stream to " + socket.RemoteEndPoint);

                while (socket.Connected && !cancellationToken.IsCancellationRequested)
                {
                    byte[] jpg;
                    try
                    {
                        using var bmp = await _capturer.CaptureAsync();
                        jpg = await _encoder.EncodeAsync(bmp);
                    }
                    catch (Exception ex)
                    {
                        _logger.Log("Frame capture error: " + ex.Message);
                        using var fallback = new System.Drawing.Bitmap(1, 1);
                        jpg = await _encoder.EncodeAsync(fallback);
                    }
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
