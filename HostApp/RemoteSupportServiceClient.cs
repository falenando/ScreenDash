using RemoteCore;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace HostApp;

public sealed class RemoteSupportServiceClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly Encoding _encoding = new UTF8Encoding(false);

    private RemoteSupportServiceClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
    }

    public static RemoteSupportServiceClient? TryConnect(int timeoutMs = 500)
    {
        try
        {
            var pipe = new NamedPipeClientStream(".", RemoteSupportPipe.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(timeoutMs);
            return new RemoteSupportServiceClient(pipe);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> HealthCheckAsync()
    {
        var response = await SendRequestAsync(CreateRequest("HealthCheck", null));
        return response?.Status == "OK";
    }

    public Task StartSessionAsync(string? accessCode) => SendRequestAsync(CreateRequest("StartSession", new { AccessCode = accessCode })).ContinueWith(_ => { });

    public Task StopSessionAsync() => SendRequestAsync(CreateRequest("StopSession", null)).ContinueWith(_ => { });

    public Task SendInputAsync(string raw) => SendRequestAsync(CreateRequest("SendInput", new { Raw = raw })).ContinueWith(_ => { });

    public async Task<byte[]?> RequestFrameAsync()
    {
        var response = await SendRequestAsync(CreateRequest("RequestFrame", null));
        if (response == null || response.Status != "OK" || response.Length is null || response.Length <= 0)
            return null;

        var length = response.Length.Value;
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var chunk = await _pipe.ReadAsync(buffer, read, length - read);
            if (chunk == 0)
                break;
            read += chunk;
        }

        if (read != length)
            return null;

        return buffer;
    }

    private async Task<RemoteSupportResponse?> SendRequestAsync(RemoteSupportRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        await WriteLineAsync(json);
        var line = await ReadLineAsync();
        if (string.IsNullOrWhiteSpace(line))
            return null;

        return JsonSerializer.Deserialize<RemoteSupportResponse>(line);
    }

    private static RemoteSupportRequest CreateRequest(string messageType, object? payload)
    {
        JsonElement? element = payload == null ? null : JsonSerializer.SerializeToElement(payload);
        return new RemoteSupportRequest(messageType, element);
    }

    private async Task WriteLineAsync(string text)
    {
        var bytes = _encoding.GetBytes(text + "\n");
        await _pipe.WriteAsync(bytes, 0, bytes.Length);
        await _pipe.FlushAsync();
    }

    private async Task<string?> ReadLineAsync()
    {
        var buffer = new List<byte>();
        var temp = new byte[1];
        while (true)
        {
            var read = await _pipe.ReadAsync(temp, 0, 1);
            if (read == 0)
                return null;

            if (temp[0] == (byte)'\n')
                break;

            buffer.Add(temp[0]);
        }

        return _encoding.GetString(buffer.ToArray());
    }

    public void Dispose()
    {
        try { _pipe.Dispose(); } catch { }
    }

}
