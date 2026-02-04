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
        var buffer = new byte[16];
        var readTask = socket.ReceiveAsync(buffer, SocketFlags.None);
        var completed = await Task.WhenAny(readTask, Task.Delay(1000, token));
        if (completed != readTask)
            return (ConnectionRole.Viewer, null);

        var read = await readTask;
        if (read <= 0)
            return (ConnectionRole.Unknown, null);

        var text = Encoding.ASCII.GetString(buffer, 0, read);
        if (text.StartsWith("HOST", StringComparison.OrdinalIgnoreCase))
            return (ConnectionRole.Host, null);

        return (ConnectionRole.Viewer, buffer.AsSpan(0, read).ToArray());
    }

    private void StartRelayIfReady()
    {
        if (_host == null || _viewer == null || _relayTask != null)
            return;

        _relayCts = new CancellationTokenSource();
        var token = _relayCts.Token;

        _relayTask = Task.WhenAll(
                PipeAsync(_host.Socket, _viewer.Socket, null, token),
                PipeAsync(_viewer.Socket, _host.Socket, _viewer.InitialPayload, token))
            .ContinueWith(_ => CleanupSession(), TaskScheduler.Default);

        Console.WriteLine("Relay session started.");
    }

    private async Task PipeAsync(Socket source, Socket destination, byte[]? initialPayload, CancellationToken token)
    {
        try
        {
            if (initialPayload is { Length: > 0 })
                await destination.SendAsync(initialPayload, SocketFlags.None);

            var buffer = new byte[8192];
            while (!token.IsCancellationRequested)
            {
                var read = await source.ReceiveAsync(buffer, SocketFlags.None);
                if (read <= 0)
                    break;

                await destination.SendAsync(buffer.AsMemory(0, read), SocketFlags.None);
            }
        }
        catch
        {
        }
        finally
        {
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
