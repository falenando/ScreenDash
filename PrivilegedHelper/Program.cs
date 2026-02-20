using RemoteCore;
using RemoteCore.Implementations;
using RemoteCore.Interfaces;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Text;
using System.Text.Json;

namespace PrivilegedHelper;

internal static class Program
{
    private static readonly Encoding PipeEncoding = new UTF8Encoding(false);
    private static readonly Lazy<IScreenCapturer> CapturerLazy = new(() => new ScreenCapturerDesktopDuplication(), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<IFrameEncoder> EncoderLazy = new(() => new JpegFrameEncoder(50, 1024), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly ConnectionLogger Logger = new(GetLogPath());
    private static bool _sessionActive;
    private static string? _accessCode;

    private static string GetLogPath()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ScreenDash", "Logs");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "privileged-helper.log");
        }
        catch
        {
            return "privileged-helper.log";
        }
    }

    [STAThread]
    private static async Task Main(string[] args)
    {
        var pipeName = RemoteSupportPipe.PipeName;
        if (args.Length >= 2 && string.Equals(args[0], "--pipe", StringComparison.OrdinalIgnoreCase))
        {
            pipeName = args[1];
        }

        Logger.Log($"PrivilegedHelper starting. Pipe={pipeName} BaseDir={AppContext.BaseDirectory}");
        LogStartupDiagnostics();

        while (true)
        {
            try
            {
                await RunServerAsync(pipeName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Log($"PrivilegedHelper loop error: {ex}");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    private static void LogStartupDiagnostics()
    {
        try
        {
            Logger.Log($"UserInteractive={Environment.UserInteractive}");
        }
        catch { }

        try
        {
            Logger.Log($"Identity={WindowsIdentity.GetCurrent().Name} Session={Process.GetCurrentProcess().SessionId} Pid={Environment.ProcessId} Cwd={Environment.CurrentDirectory}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to log identity/process/session/cwd: {ex}");
        }

        try
        {
            var tokenSessionId = TryGetTokenSessionId();
            var tokenType = TryGetTokenType();
            var elevated = TryGetTokenIsElevated();
            Logger.Log($"Token: SessionId={tokenSessionId?.ToString() ?? "?"} Type={tokenType ?? "?"} IsElevated={elevated?.ToString() ?? "?"}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to log token diagnostics: {ex}");
        }

        try
        {
            var winSta = GetUserObjectNameSafe(GetProcessWindowStation());
            var threadDesktop = GetUserObjectNameSafe(GetThreadDesktop(GetCurrentThreadId()));
            var inputDesktop = GetInputDesktopNameSafe(out var inputDesktopErr);
            Logger.Log($"Desktop: WinSta={winSta ?? "?"} ThreadDesktop={threadDesktop ?? "?"} InputDesktop={inputDesktop ?? "?"} InputDesktopErr={inputDesktopErr ?? "-"}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to log desktop diagnostics: {ex}");
        }
    }

    private static uint? TryGetTokenSessionId()
    {
        var token = WindowsIdentity.GetCurrent().Token;

        var size = sizeof(uint);
        var mem = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenSessionId, mem, size, out _))
                return null;

            return (uint)Marshal.ReadInt32(mem);
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }

    private static string? TryGetTokenType()
    {
        var token = WindowsIdentity.GetCurrent().Token;

        var size = sizeof(int);
        var mem = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenType, mem, size, out _))
                return null;

            var type = Marshal.ReadInt32(mem);
            return type switch
            {
                1 => "Primary",
                2 => "Impersonation",
                _ => type.ToString(CultureInfo.InvariantCulture)
            };
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }

    private static bool? TryGetTokenIsElevated()
    {
        var token = WindowsIdentity.GetCurrent().Token;

        var size = Marshal.SizeOf<TOKEN_ELEVATION>();
        var mem = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, mem, size, out _))
                return null;

            var elevation = Marshal.PtrToStructure<TOKEN_ELEVATION>(mem);
            return elevation.TokenIsElevated != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }

    private static string? GetInputDesktopNameSafe(out string? error)
    {
        error = null;

        var hInputDesktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (hInputDesktop == IntPtr.Zero)
        {
            var code = Marshal.GetLastWin32Error();
            error = $"OpenInputDesktop failed. Win32={code} ({new Win32Exception(code).Message})";
            return null;
        }

        try
        {
            return GetUserObjectNameSafe(hInputDesktop);
        }
        finally
        {
            CloseDesktop(hInputDesktop);
        }
    }

    private static string? GetUserObjectNameSafe(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return null;

        var needed = 0;
        _ = GetUserObjectInformation(handle, UOI_NAME, null, 0, ref needed);
        if (needed <= 0)
            return null;

        var sb = new StringBuilder(needed);
        if (!GetUserObjectInformation(handle, UOI_NAME, sb, sb.Capacity, ref needed))
            return null;

        return sb.ToString();
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass, IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

    private enum TOKEN_INFORMATION_CLASS
    {
        TokenType = 8,
        TokenSessionId = 12,
        TokenElevation = 20
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        public int TokenIsElevated;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, StringBuilder? pvInfo, int nLength, ref int lpnLengthNeeded);

    private const int UOI_NAME = 2;

    private static async Task RunServerAsync(string pipeName, CancellationToken token)
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 5, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, pipeSecurity, HandleInheritability.None);
                Logger.Log("Waiting for pipe connection...");
                await pipe.WaitForConnectionAsync(token);
                Logger.Log("Pipe connected.");
                await HandleConnectionAsync(pipe, token);
                Logger.Log("Pipe disconnected.");
            }
            catch (OperationCanceledException)
            {
                Logger.Log("Pipe wait canceled.");
                break;
            }
            catch (Exception ex)
            {
                Logger.Log($"Pipe loop error: {ex}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                pipe?.Dispose();
            }
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
                Logger.Log("Bad request received (invalid JSON).");
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
                    Logger.Log("Session started.");
                    await WriteResponseAsync(pipe, new RemoteSupportResponse("OK", "Started"), token);
                    break;
                case "StopSession":
                    _sessionActive = false;
                    _accessCode = null;
                    Logger.Log("Session stopped.");
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
            using var bmp = await CapturerLazy.Value.CaptureAsync();
            return await EncoderLazy.Value.EncodeAsync(bmp);
        }
        catch
        {
            using var fallback = new System.Drawing.Bitmap(1, 1);
            return await EncoderLazy.Value.EncodeAsync(fallback);
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
        ExecOnInputDesktop(() =>
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            var px = (int)Math.Round(Math.Clamp(x, 0, 1) * (bounds.Width - 1)) + bounds.Left;
            var py = (int)Math.Round(Math.Clamp(y, 0, 1) * (bounds.Height - 1)) + bounds.Top;
            SetCursorPos(px, py);
        });
    }

    private static void SendMouseButton(string button, bool isDown, double x, double y)
    {
        ExecOnInputDesktop(() =>
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
        });
    }

    private static void SendMouseWheel(int delta, double x, double y)
    {
        ExecOnInputDesktop(() =>
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
        });
    }

    private static void SendKey(ushort key, bool keyUp)
    {
        ExecOnInputDesktop(() =>
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
        });
    }

    private static void ExecOnInputDesktop(Action action)
    {
        var hThread = GetCurrentThreadId();
        var hOriginalDesktop = GetThreadDesktop(hThread);
        var hInputDesktop = OpenInputDesktop(0, false, GENERIC_ALL);

        bool switched = false;
        if (hInputDesktop != IntPtr.Zero && hInputDesktop != hOriginalDesktop)
        {
            switched = SetThreadDesktop(hInputDesktop);
        }

        try
        {
            action();
        }
        finally
        {
            if (switched)
            {
                SetThreadDesktop(hOriginalDesktop);
            }
            if (hInputDesktop != IntPtr.Zero)
            {
                CloseDesktop(hInputDesktop);
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const uint GENERIC_ALL = 0x10000000;
    private const uint DESKTOP_READOBJECTS = 0x0001;

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
