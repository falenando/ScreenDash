using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using RemoteCore;

namespace RemoteSupport.Service;

public sealed class SessionManager
{
    private readonly ServiceLogger _logger;
    private readonly object _sync = new();
    private Process? _agentProcess;
    private string? _agentPipeName;
    private int _sessionId;

    public SessionManager(ServiceLogger logger)
    {
        _logger = logger;
    }

    public Task StartSessionAsync(string? accessCode, string? relayAddress, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(accessCode))
        {
            var accessService = new AccessCodeService();
            if (!accessService.TryDecode(accessCode, out _, out _))
            {
                _logger.Error("Invalid access code supplied.");
            }
        }

        lock (_sync)
        {
            _sessionId = (int)WTSGetActiveConsoleSessionId();
            _agentPipeName = $"ScreenDash_RemoteSupport_Agent_{_sessionId}";
            EnsureAgentRunning(_sessionId, _agentPipeName);
        }

        _logger.Info($"Session started for session {_sessionId}.");
        return Task.CompletedTask;
    }

    public void StopSession()
    {
        lock (_sync)
        {
            try { _agentProcess?.Kill(true); } catch { }
            _agentProcess = null;
            _agentPipeName = null;
        }

        _logger.Info("Session stopped.");
    }

    public async Task<byte[]?> RequestFrameAsync(CancellationToken token)
    {
        var pipeName = _agentPipeName;
        if (!string.IsNullOrWhiteSpace(pipeName))
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(1000, token);
                await SendLineAsync(client, "REQUEST_FRAME", token);
                var lengthBytes = await ReadExactAsync(client, 4, token);
                if (lengthBytes.Length < 4)
                    throw new InvalidOperationException("Invalid frame length.");

                var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                if (length <= 0)
                    return null;

                var payload = await ReadExactAsync(client, length, token);
                return payload;
            }
            catch (Exception ex)
            {
                _logger.Error("Agent frame failed: " + ex.Message);
            }
        }

        return ScreenCapture.CaptureJpeg();
    }

    public async Task SendInputAsync(string rawCommand, CancellationToken token)
    {
        var pipeName = _agentPipeName;
        if (!string.IsNullOrWhiteSpace(pipeName))
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await client.ConnectAsync(1000, token);
                await SendLineAsync(client, rawCommand, token);
                return;
            }
            catch (Exception ex)
            {
                _logger.Error("Agent input failed: " + ex.Message);
            }
        }

        InputDispatcher.HandleInputCommand(rawCommand);
    }

    private void EnsureAgentRunning(int sessionId, string pipeName)
    {
        if (_agentProcess != null && !_agentProcess.HasExited)
            return;

        var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteSupport.Service.Agent.exe");
        if (!File.Exists(exePath))
        {
            _logger.Error("Agent executable not found: " + exePath);
            return;
        }

        try
        {
            if (!LaunchAgentInSession(sessionId, exePath, pipeName, out var processId))
            {
                _logger.Error("Failed to launch agent in session.");
                return;
            }

            _agentProcess = Process.GetProcessById(processId);
            _logger.Info("Agent started with PID " + processId);
        }
        catch (Exception ex)
        {
            _logger.Error("Agent start error: " + ex.Message);
        }
    }

    private static async Task SendLineAsync(Stream stream, string line, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes, 0, bytes.Length, token);
        await stream.FlushAsync(token);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken token)
    {
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var chunk = await stream.ReadAsync(buffer, read, length - read, token);
            if (chunk == 0)
                break;
            read += chunk;
        }

        if (read == length)
            return buffer;

        return buffer.AsSpan(0, read).ToArray();
    }

    private static bool LaunchAgentInSession(int sessionId, string exePath, string pipeName, out int processId)
    {
        processId = 0;

        if (!WTSQueryUserToken((uint)sessionId, out var userToken))
            return false;

        try
        {
            if (!DuplicateTokenEx(userToken, 0xF01FF, IntPtr.Zero, 2, 1, out var primaryToken))
                return false;

            try
            {
                if (!CreateEnvironmentBlock(out var environment, primaryToken, false))
                    environment = IntPtr.Zero;

                var startup = new STARTUPINFO
                {
                    cb = Marshal.SizeOf<STARTUPINFO>(),
                    lpDesktop = "winsta0\\default"
                };

                var commandLine = $"\"{exePath}\" --agent --session {sessionId} --pipe {pipeName}";

                var result = CreateProcessAsUser(
                    primaryToken,
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    0x00000400,
                    environment,
                    Path.GetDirectoryName(exePath),
                    ref startup,
                    out var processInfo);

                if (!result)
                    return false;

                processId = (int)processInfo.dwProcessId;
                CloseHandle(processInfo.hThread);
                CloseHandle(processInfo.hProcess);
                return true;
            }
            finally
            {
                if (primaryToken != IntPtr.Zero)
                    CloseHandle(primaryToken);
            }
        }
        finally
        {
            if (userToken != IntPtr.Zero)
                CloseHandle(userToken);
        }
    }

    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes, int impersonationLevel, int tokenType, out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }
}
