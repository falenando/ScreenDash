using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Pipes;
using System.Text;
using RemoteSupport.Service.Agent;

var argsList = args.ToList();
var pipeName = GetArgValue(argsList, "--pipe") ?? "ScreenDash_RemoteSupport_Agent_0";

using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
await server.WaitForConnectionAsync();

using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, true);

while (server.IsConnected)
{
    var line = await reader.ReadLineAsync();
    if (string.IsNullOrWhiteSpace(line))
        break;

    if (string.Equals(line, "REQUEST_FRAME", StringComparison.OrdinalIgnoreCase))
    {
        var frame = CaptureJpeg();
        if (frame == null)
        {
            await WriteLengthAsync(server, 0);
            continue;
        }

        await WriteLengthAsync(server, frame.Length);
        await server.WriteAsync(frame, 0, frame.Length);
        await server.FlushAsync();
        continue;
    }

    if (line.StartsWith("INPUT|", StringComparison.OrdinalIgnoreCase))
    {
        InputDispatcher.HandleInputCommand(line);
    }
}

static string? GetArgValue(List<string> args, string name)
{
    var index = args.FindIndex(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0 || index + 1 >= args.Count)
        return null;

    return args[index + 1];
}

static async Task WriteLengthAsync(Stream stream, int length)
{
    var buffer = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(buffer, length);
    await stream.WriteAsync(buffer, 0, buffer.Length);
}

static byte[]? CaptureJpeg()
{
    try
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1024, 768);
        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }
    catch
    {
        return null;
    }
}
