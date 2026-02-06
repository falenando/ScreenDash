using System.Text.Json;

namespace RemoteCore;

public static class RemoteSupportPipe
{
    public const string PipeName = "ScreenDash_RemoteSupport_v1";
}

public sealed record RemoteSupportRequest(string MessageType, JsonElement? Payload);

public sealed record RemoteSupportResponse(string Status, string Info)
{
    public int? Length { get; init; }
}
