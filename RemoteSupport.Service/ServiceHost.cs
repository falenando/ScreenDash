using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace RemoteSupport.Service;

public sealed class ServiceHost : BackgroundService
{
    private const string PipeName = "ScreenDash_RemoteSupport_v1";
    private readonly ServiceLogger _logger;
    private readonly SessionManager _sessionManager;

    public ServiceHost(ServiceLogger logger, SessionManager sessionManager)
    {
        _logger = logger;
        _sessionManager = sessionManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("RemoteSupport.Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            using var server = CreatePipe();
            try
            {
                await server.WaitForConnectionAsync(stoppingToken);
                _logger.Info("HostApp connected to service pipe.");
                await HandleClientAsync(server, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Pipe error: " + ex.Message);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken token)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(server, Encoding.UTF8, 4096, true) { AutoFlush = true };

        while (server.IsConnected && !token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                break;

            PipeRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<PipeRequest>(line);
            }
            catch
            {
                await WriteResponseAsync(writer, new PipeResponse("Error", "Invalid JSON"));
                continue;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.MessageType))
            {
                await WriteResponseAsync(writer, new PipeResponse("Error", "Missing MessageType"));
                continue;
            }

            switch (request.MessageType)
            {
                case "HealthCheck":
                    await WriteResponseAsync(writer, new PipeResponse("OK", "Service running"));
                    break;
                case "StartSession":
                    var accessCode = request.Payload?.GetPropertyOrDefault("AccessCode");
                    var relayAddress = request.Payload?.GetPropertyOrDefault("OptionalRelayAddress");
                    await _sessionManager.StartSessionAsync(accessCode, relayAddress, token);
                    await WriteResponseAsync(writer, new PipeResponse("OK", "Session started"));
                    break;
                case "StopSession":
                    _sessionManager.StopSession();
                    await WriteResponseAsync(writer, new PipeResponse("OK", "Session stopped"));
                    break;
                case "SendInput":
                    var raw = request.Payload?.GetPropertyOrDefault("Raw");
                    if (!string.IsNullOrWhiteSpace(raw))
                        await _sessionManager.SendInputAsync(raw, token);
                    await WriteResponseAsync(writer, new PipeResponse("OK", "Input forwarded"));
                    break;
                case "RequestFrame":
                    var frame = await _sessionManager.RequestFrameAsync(token);
                    if (frame == null)
                    {
                        await WriteResponseAsync(writer, new PipeResponse("Error", "No frame"));
                        break;
                    }

                    var response = new PipeResponse("OK", "Frame") { MessageType = "Frame", Length = frame.Length };
                    await WriteResponseAsync(writer, response);
                    await server.WriteAsync(frame, 0, frame.Length, token);
                    await server.FlushAsync(token);
                    break;
                default:
                    await WriteResponseAsync(writer, new PipeResponse("Error", "Unknown MessageType"));
                    break;
            }
        }
    }

    private static async Task WriteResponseAsync(StreamWriter writer, PipeResponse response)
    {
        var json = JsonSerializer.Serialize(response);
        await writer.WriteLineAsync(json);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var pipeSecurity = new PipeSecurity();
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(users, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096,
            4096,
            pipeSecurity);
    }

    private sealed record PipeRequest(string MessageType, JsonElement? Payload);

    private sealed record PipeResponse(string Status, string Info)
    {
        public string? MessageType { get; init; }
        public int? Length { get; init; }
    }
}

internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement? element, string name)
    {
        if (element is null || element.Value.ValueKind == JsonValueKind.Null)
            return null;

        if (element.Value.TryGetProperty(name, out var value))
            return value.GetString();

        return null;
    }
}
