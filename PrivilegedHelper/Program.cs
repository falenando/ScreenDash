using RemoteCore;
using RemoteCore.Implementations;
using RemoteCore.Interfaces;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PrivilegedHelper;

internal static class Program
{
    private static readonly Encoding PipeEncoding = new UTF8Encoding(false);
    private static readonly IScreenCapturer Capturer = new ScreenCapturerDesktopDuplication();
    private static readonly IFrameEncoder Encoder = new JpegFrameEncoder(50, 1024);
    private static bool _sessionActive;
    private static string? _accessCode;

    [STAThread]
    private static async Task Main(string[] args)
    {
        var pipeName = RemoteSupportPipe.PipeName;
        if (args.Length >= 2 && string.Equals(args[0], "--pipe", StringComparison.OrdinalIgnoreCase))
        {
            pipeName = args[1];
        }

        await RunServerAsync(pipeName, CancellationToken.None);
    }

    private static async Task RunServerAsync(string pipeName, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(token);
            await HandleConnectionAsync(pipe, token);
        }
    }

    private static async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        while (pipe.IsConnected && !token.IsCancellationRequested)
        {
            var line = await ReadLineAsync(pipe, token);
            if (string.IsNullOrWhiteSpace(line))
                break;

            RemoteSupportRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<RemoteSupportRequest>(line);
            }
            catch
            {
                await WriteResponseAsync(pipe, new RemoteSupportResponse("ERROR", "BadRequest"), token);
                continue;
            }

            if (request == null)
            {
                await WriteResponseAsync(pipe, new RemoteSupportResponse("ERROR", "BadRequest"), token);
                continue;
            }

            switch (request.MessageType)
            {
                case "HealthCheck":
                    await WriteResponseAsync(pipe, new RemoteSupportResponse("OK", "Healthy"), token);
                    break;
                case "StartSession":
                    HandleStartSession(request.Payload);
                    await WriteResponseAsync(pipe, new RemoteSupportResponse("OK", "Started"), token);
                    break;
                case "StopSession":
                    _sessionActive = false;
                    _accessCode = null;
                    await WriteResponseAsync(pipe, new RemoteSupportResponse("OK", "Stopped"), token);
                    break;
                case "SendInput":
                    HandleSendInput(request.Payload);
                    await WriteResponseAsync(pipe, new RemoteSupportResponse("OK", "Input"), token);
                    break;
                case "RequestFrame":
                    var frame = await CaptureFrameAsync();
                    var response = new RemoteSupportResponse("OK", "Frame") { Length = frame.Length };
                    await WriteResponseAsync(pipe, response, token);
                    await pipe.WriteAsync(frame, 0, frame.Length, token);
                    await pipe.FlushAsync(token);
                    break;
                default:
                    await WriteResponseAsync(pipe, new RemoteSupportResponse("ERROR", "UnknownMessage"), token);
                    break;
            }
        }
    }

    private static void HandleStartSession(JsonElement? payload)
    {
        _sessionActive = true;
        if (payload.HasValue && payload.Value.TryGetProperty("AccessCode", out var codeElement))
        {
            _accessCode = codeElement.GetString();
        }
    }

    private static void HandleSendInput(JsonElement? payload)
    {
        if (!_sessionActive)
            return;

        if (!payload.HasValue || !payload.Value.TryGetProperty("Raw", out var rawElement))
            return;

        var raw = rawElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return;

        HandleInputCommand(raw);
    }

    private static async Task<byte[]> CaptureFrameAsync()
    {
        try
        {
            using var bmp = await Capturer.CaptureAsync();
            return await Encoder.EncodeAsync(bmp);
        }
        catch
        {
            using var fallback = new System.Drawing.Bitmap(1, 1);
            return await Encoder.EncodeAsync(fallback);
        }
    }

    private static async Task WriteResponseAsync(NamedPipeServerStream pipe, RemoteSupportResponse response, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(response);
        var bytes = PipeEncoding.GetBytes(json + "\n");
        await pipe.WriteAsync(bytes, 0, bytes.Length, token);
        await pipe.FlushAsync(token);
    }

    private static async Task<string?> ReadLineAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        var buffer = new List<byte>();
        var temp = new byte[1];
        while (true)
        {
            var read = await pipe.ReadAsync(temp, 0, 1, token);
            if (read == 0)
                return null;

            if (temp[0] == (byte)'\n')
                break;

            buffer.Add(temp[0]);
        }

        return PipeEncoding.GetString(buffer.ToArray());
    }

    private static void HandleInputCommand(string line)
    {
        if (!line.StartsWith("INPUT|", StringComparison.OrdinalIgnoreCase))
            return;

        var parts = line.Split('|');
        if (parts.Length < 2)
            return;

        switch (parts[1])
        {
            case "MM":
                if (TryParsePoint(parts, 2, out var mx, out var my))
                    MoveMouseToNormalized(mx, my);
                break;
            case "MD":
                if (parts.Length >= 5 && TryParsePoint(parts, 3, out var mdx, out var mdy))
                    SendMouseButton(parts[2], true, mdx, mdy);
                break;
            case "MU":
                if (parts.Length >= 5 && TryParsePoint(parts, 3, out var mux, out var muy))
                    SendMouseButton(parts[2], false, mux, muy);
                break;
            case "MW":
                if (parts.Length >= 5 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta) && TryParsePoint(parts, 3, out var mwx, out var mwy))
                    SendMouseWheel(delta, mwx, mwy);
                break;
            case "KD":
                if (parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var keyDown))
                    SendKey((ushort)keyDown, false);
                break;
            case "KU":
                if (parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var keyUp))
                    SendKey((ushort)keyUp, true);
                break;
        }
    }

    private static bool TryParsePoint(string[] parts, int startIndex, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (parts.Length < startIndex + 2)
            return false;

        return double.TryParse(parts[startIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            && double.TryParse(parts[startIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
    }

    private static void MoveMouseToNormalized(double x, double y)
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        var px = (int)Math.Round(Math.Clamp(x, 0, 1) * (bounds.Width - 1)) + bounds.Left;
        var py = (int)Math.Round(Math.Clamp(y, 0, 1) * (bounds.Height - 1)) + bounds.Top;
        SetCursorPos(px, py);
    }

    private static void SendMouseButton(string button, bool isDown, double x, double y)
    {
        MoveMouseToNormalized(x, y);

        uint flag = button switch
        {
            "Left" => isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
            "Right" => isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            "Middle" => isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };

        if (flag == 0)
            return;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flag
                }
            }
        };

        _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendMouseWheel(int delta, double x, double y)
    {
        MoveMouseToNormalized(x, y);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = MOUSEEVENTF_WHEEL,
                    mouseData = (uint)delta
                }
            }
        };

        _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendKey(ushort key, bool keyUp)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = key,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                }
            }
        };

        _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
