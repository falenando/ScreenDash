using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

var config = RelayConfig.Load("ServerRelayconfig.json");
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new TcpRelayServer(config.Port);
await server.RunAsync(cts.Token);

internal sealed class RelayConfig
{
    public int Port { get; set; } = 5050;

    public static RelayConfig Load(string fileName)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(baseDir, fileName);
            if (!File.Exists(path))
                return new RelayConfig();

            using var fs = File.OpenRead(path);
            var config = JsonSerializer.Deserialize<RelayConfig>(fs);
            return config ?? new RelayConfig();
        }
        catch
        {
            return new RelayConfig();
        }
    }
}

internal sealed class TcpRelayServer
{
    private readonly int _port;
    private TcpListener? _listener;
    private readonly object _sync = new();
    private RelayPeer? _host;
    private RelayPeer? _viewer;
    private Task? _relayTask;
    private CancellationTokenSource? _relayCts;

    public TcpRelayServer(int port)
    {
        _port = port;
    }

    public async Task RunAsync(CancellationToken token)
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        Console.WriteLine($"ServerRelay listening on 0.0.0.0:{_port}");

        try
        {
            while (!token.IsCancellationRequested)
            {
                var socket = await _listener.AcceptSocketAsync(token);
                _ = Task.Run(() => HandleConnectionAsync(socket, token), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try { _listener.Stop(); } catch { }
        }
    }

    private async Task HandleConnectionAsync(Socket socket, CancellationToken token)
    {
        try
        {
            var (role, initialPayload) = await IdentifyRoleAsync(socket, token);
            if (role == ConnectionRole.Unknown)
            {
                CloseSocket(socket);
                return;
            }

            Console.WriteLine($"{role} connected from {socket.RemoteEndPoint}. Initial payload: {(initialPayload?.Length ?? 0)} bytes.");

            lock (_sync)
            {
                if (role == ConnectionRole.Host)
                {
                    if (_host != null)
                    {
                        Console.WriteLine("Host already connected; rejecting additional host.");
                        CloseSocket(socket);
                        return;
                    }

                    _host = new RelayPeer(socket, null);
                    Console.WriteLine("Host connected.");
                }
                else
                {
                    if (_viewer != null)
                    {
                        Console.WriteLine("Viewer already connected; rejecting additional viewer.");
                        CloseSocket(socket);
                        return;
                    }

                    _viewer = new RelayPeer(socket, initialPayload);
                    Console.WriteLine("Viewer connected.");
                }

                StartRelayIfReady();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Connection error: " + ex.Message);
            CloseSocket(socket);
        }
    }

    private async Task<(ConnectionRole Role, byte[]? InitialPayload)> IdentifyRoleAsync(Socket socket, CancellationToken token)
    {
        // Handshake determinístico por linha: "HOST\n".
        // Qualquer coisa diferente é Viewer.
        // Bytes extras após '\n' viram InitialPayload (serão encaminhados).
        var buffer = new byte[256];
        var received = 0;

        while (received < buffer.Length)
        {
            var readTask = socket.ReceiveAsync(buffer.AsMemory(received), SocketFlags.None).AsTask();
            var completed = await Task.WhenAny(readTask, Task.Delay(3000, token));
            if (completed != readTask)
            {
                Console.WriteLine("Handshake timeout; treating as Viewer.");
                return (ConnectionRole.Viewer, null);
            }

            var read = await readTask;
            if (read <= 0)
            {
                Console.WriteLine("Handshake read returned 0 bytes.");
                return (ConnectionRole.Unknown, null);
            }

            received += read;

            var nlIndex = Array.IndexOf(buffer, (byte)'\n', 0, received);
            if (nlIndex < 0)
                continue;

            var line = Encoding.ASCII.GetString(buffer, 0, nlIndex).Trim();
            var extraCount = received - (nlIndex + 1);
            byte[]? extra = extraCount > 0
                ? buffer.AsSpan(nlIndex + 1, extraCount).ToArray()
                : null;

            if (line.Equals("HOST", StringComparison.OrdinalIgnoreCase))
                return (ConnectionRole.Host, extra);

            var lineBytes = Encoding.UTF8.GetBytes(line + "\n");
            var payload = extra == null || extra.Length == 0
                ? lineBytes
                : CombinePayload(lineBytes, extra);

            Console.WriteLine($"Handshake line '{line}' treated as Viewer. Forwarding {payload.Length} bytes.");
            return (ConnectionRole.Viewer, payload);
        }

        // Sem '\n' -> trata como Viewer e encaminha o que tiver.
        Console.WriteLine($"Handshake without newline; treating as Viewer. Bytes: {received}.");
        return (ConnectionRole.Viewer, buffer.AsSpan(0, received).ToArray());
    }

    private static byte[] CombinePayload(byte[] first, byte[] second)
    {
        var combined = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, combined, 0, first.Length);
        Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
        return combined;
    }

    private void StartRelayIfReady()
    {
        if (_host == null || _viewer == null || _relayTask != null)
            return;

        _relayCts = new CancellationTokenSource();
        var token = _relayCts.Token;

        _relayTask = Task.WhenAll(
                PipeAsync(_host.Socket, _viewer.Socket, null, token, "Host->Viewer"),
                PipeAsync(_viewer.Socket, _host.Socket, _viewer.InitialPayload, token, "Viewer->Host"))
            .ContinueWith(_ => CleanupSession(), TaskScheduler.Default);

        Console.WriteLine("Relay session started.");
    }

    private async Task PipeAsync(Socket source, Socket destination, byte[]? initialPayload, CancellationToken token, string label)
    {
        try
        {
            if (initialPayload is { Length: > 0 })
            {
                Console.WriteLine($"{label} initial payload: {initialPayload.Length} bytes.");
                await destination.SendAsync(initialPayload, SocketFlags.None);
            }

            var buffer = new byte[8192];
            var firstRead = true;
            while (!token.IsCancellationRequested)
            {
                var read = await source.ReceiveAsync(buffer, SocketFlags.None);
                if (read <= 0)
                    break;

                if (firstRead)
                {
                    Console.WriteLine($"{label} first read: {read} bytes.");
                    firstRead = false;
                }

                await destination.SendAsync(buffer.AsMemory(0, read), SocketFlags.None);
            }
        }
        catch
        {
        }
        finally
        {
            Console.WriteLine($"{label} pipe ended.");
            _relayCts?.Cancel();
        }
    }

    private void CleanupSession()
    {
        lock (_sync)
        {
            CloseSocket(_host?.Socket);
            CloseSocket(_viewer?.Socket);
            _host = null;
            _viewer = null;
            _relayTask = null;
            _relayCts?.Dispose();
            _relayCts = null;
            Console.WriteLine("Relay session ended.");
        }
    }

    private static void CloseSocket(Socket? socket)
    {
        if (socket == null)
            return;

        try { socket.Shutdown(SocketShutdown.Both); } catch { }
        try { socket.Close(); } catch { }
    }

    private sealed record RelayPeer(Socket Socket, byte[]? InitialPayload);

    private enum ConnectionRole
    {
        Unknown,
        Host,
        Viewer
    }
}
