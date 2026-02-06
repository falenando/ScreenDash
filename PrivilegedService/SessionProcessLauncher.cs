using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PrivilegedService;

internal sealed class SessionProcessLauncher
{
    public bool TryStartInActiveSession(string exePath, string arguments, out Process? process, out uint sessionId, out string? error)
    {
        process = null;
        error = null;
        sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            error = "No active console session.";
            return false;
        }

        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        var primaryToken = IntPtr.Zero;
        var env = IntPtr.Zero;
        var processInfo = new PROCESS_INFORMATION();

        try
        {
            if (!DuplicateTokenEx(userToken, TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID, IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out primaryToken))
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            CreateEnvironmentBlock(out env, primaryToken, false);

            var startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = "winsta0\\default"
            };

            var commandLine = $"\"{exePath}\" {arguments}";
            var success = CreateProcessAsUser(primaryToken, null, commandLine, IntPtr.Zero, IntPtr.Zero, false, CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE, env, null, ref startupInfo, out processInfo);
            if (!success)
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            process = Process.GetProcessById((int)processInfo.dwProcessId);
            return true;
        }
        finally
        {
            if (processInfo.hProcess != IntPtr.Zero)
                CloseHandle(processInfo.hProcess);
            if (processInfo.hThread != IntPtr.Zero)
                CloseHandle(processInfo.hThread);
            if (env != IntPtr.Zero)
                DestroyEnvironmentBlock(env);
            if (primaryToken != IntPtr.Zero)
                CloseHandle(primaryToken);
            if (userToken != IntPtr.Zero)
                CloseHandle(userToken);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint SessionId, out IntPtr Token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes, SECURITY_IMPERSONATION_LEVEL ImpersonationLevel, TOKEN_TYPE TokenType, out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr hToken, string? lpApplicationName, string? lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;

    private enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation = 2
    }

    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
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
