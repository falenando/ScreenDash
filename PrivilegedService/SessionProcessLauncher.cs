using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PrivilegedService;

internal sealed class SessionProcessLauncher
{
    private static readonly string[] RequiredPrivileges =
    [
        "SeTcbPrivilege",
        "SeAssignPrimaryTokenPrivilege",
        "SeIncreaseQuotaPrivilege",
        "SeImpersonatePrivilege"
    ];

    private readonly Action<string>? _log;

    public SessionProcessLauncher(Action<string>? log = null)
    {
        _log = log;
    }

    public bool TryStartAsSystemInSession(uint sessionId, string exePath, string arguments, out Process? process, out string? error)
        => TryStartAsSystemInSession(sessionId, exePath, arguments, "winsta0\\default", out process, out error);

    public bool TryStartAsSystemInSession(uint sessionId, string exePath, string arguments, string desktopName, out Process? process, out string? error)
    {
        process = null;
        error = null;

        Log($"TryStartAsSystemInSession: Session={sessionId} Desktop={desktopName} Exe={exePath} Args={arguments}");
        try { Log($"LauncherIdentity={WindowsIdentity.GetCurrent().Name}"); } catch { }

        IntPtr processToken = IntPtr.Zero;
        IntPtr userToken = IntPtr.Zero;
        IntPtr systemToken = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;
        IntPtr env = IntPtr.Zero;
        var processInfo = new PROCESS_INFORMATION();

        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ALL_ACCESS, out processToken))
            {
                error = FormatWin32Error("OpenProcessToken");
                Log(error);
                return false;
            }

            if (!EnableRequiredPrivileges(processToken, out error))
                return false;

            if (!DuplicateTokenEx(processToken, TOKEN_ALL_ACCESS, IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out primaryToken))
            {
                error = FormatWin32Error("DuplicateTokenEx(processToken)");
                Log(error);
                return false;
            }

            if (!SetTokenInformation(primaryToken, TOKEN_INFORMATION_CLASS.TokenSessionId, ref sessionId, sizeof(uint)))
            {
                // Fallback: CreateProcessAsUser can fail to set session id depending on token type.
                // Use the interactive user token + CreateProcessWithTokenW for better reliability.
                var setSessionErr = FormatWin32Error("SetTokenInformation(TokenSessionId)");
                Log(setSessionErr);
                if (!WTSQueryUserToken(sessionId, out userToken))
                {
                    error = $"{setSessionErr}. {FormatWin32Error("WTSQueryUserToken")}";
                    Log(error);
                    return false;
                }

                if (!DuplicateTokenEx(userToken, TOKEN_ALL_ACCESS, IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out systemToken))
                {
                    error = $"{setSessionErr}. {FormatWin32Error("DuplicateTokenEx(userToken)")}";
                    Log(error);
                    return false;
                }

                if (!EnableRequiredPrivileges(systemToken, out error))
                    return false;

                var startupInfoFallback = new STARTUPINFO
                {
                    cb = Marshal.SizeOf<STARTUPINFO>(),
                    lpDesktop = desktopName
                };

                var commandLineFallback = $"\"{exePath}\" {arguments}";
                var currentDirectoryFallback = Path.GetDirectoryName(exePath);
                Log("Launching via CreateProcessWithTokenW (fallback after SetTokenInformation failure).");
                var successFallback = CreateProcessWithTokenW(systemToken, LOGON_WITH_PROFILE, null, commandLineFallback, CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE, IntPtr.Zero, currentDirectoryFallback, ref startupInfoFallback, out processInfo);
                if (!successFallback)
                {
                    error = $"{setSessionErr}. {FormatWin32Error("CreateProcessWithTokenW")}";
                    Log(error);
                    return false;
                }

                process = Process.GetProcessById((int)processInfo.dwProcessId);
                Log($"Launch OK. PID={(int)processInfo.dwProcessId}");
                return true;
            }

            if (!EnableRequiredPrivileges(primaryToken, out error))
                return false;

            if (!CreateEnvironmentBlock(out env, primaryToken, false))
            {
                Log($"{FormatWin32Error("CreateEnvironmentBlock")} (continuing with null env)");
                env = IntPtr.Zero;
            }

            var startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = desktopName
            };

            var commandLine = $"\"{exePath}\" {arguments}";
            var currentDirectory = Path.GetDirectoryName(exePath);
            Log("Launching via CreateProcessAsUser.");
            var success = CreateProcessAsUser(primaryToken, null, commandLine, IntPtr.Zero, IntPtr.Zero, false, CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE, env, currentDirectory, ref startupInfo, out processInfo);
            if (!success)
            {
                // Fallback using interactive session token via CreateProcessWithTokenW (often works when CreateProcessAsUser is blocked)
                var createErr = FormatWin32Error("CreateProcessAsUser");
                Log(createErr);
                if (!WTSQueryUserToken(sessionId, out userToken))
                {
                    error = $"{createErr}. {FormatWin32Error("WTSQueryUserToken")}";
                    Log(error);
                    return false;
                }

                if (!DuplicateTokenEx(userToken, TOKEN_ALL_ACCESS, IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out systemToken))
                {
                    error = $"{createErr}. {FormatWin32Error("DuplicateTokenEx(userToken)")}";
                    Log(error);
                    return false;
                }

                if (!EnableRequiredPrivileges(systemToken, out error))
                    return false;

                Log("Launching via CreateProcessWithTokenW (fallback after CreateProcessAsUser failure).");
                var successFallback = CreateProcessWithTokenW(systemToken, LOGON_WITH_PROFILE, null, commandLine, CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE, IntPtr.Zero, currentDirectory, ref startupInfo, out processInfo);
                if (!successFallback)
                {
                    error = $"{createErr}. {FormatWin32Error("CreateProcessWithTokenW")}";
                    Log(error);
                    return false;
                }
            }

            process = Process.GetProcessById((int)processInfo.dwProcessId);
            Log($"Launch OK. PID={(int)processInfo.dwProcessId}");
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
            if (systemToken != IntPtr.Zero)
                CloseHandle(systemToken);
            if (userToken != IntPtr.Zero)
                CloseHandle(userToken);
            if (processToken != IntPtr.Zero)
                CloseHandle(processToken);
        }
    }

    public bool TryStartInActiveSession(string exePath, string arguments, out Process? process, out uint sessionId, out string? error)
    {
        process = null;
        error = null;
        sessionId = WTSGetActiveConsoleSessionId();

        Log($"TryStartInActiveSession: ActiveSession={sessionId} Exe={exePath} Args={arguments}");
        if (sessionId == 0xFFFFFFFF)
        {
            error = "No active console session.";
            Log(error);
            return false;
        }

        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            error = FormatWin32Error("WTSQueryUserToken");
            Log(error);
            return false;
        }

        var processToken = IntPtr.Zero;
        var primaryToken = IntPtr.Zero;
        var env = IntPtr.Zero;
        var processInfo = new PROCESS_INFORMATION();

        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out processToken))
            {
                error = FormatWin32Error("OpenProcessToken");
                Log(error);
                return false;
            }

            if (!EnableRequiredPrivileges(processToken, out error))
                return false;

            if (!DuplicateTokenEx(userToken, TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID, IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out primaryToken))
            {
                error = FormatWin32Error("DuplicateTokenEx(userToken)");
                Log(error);
                return false;
            }

            if (!EnableRequiredPrivileges(primaryToken, out error))
                return false;

            if (!CreateEnvironmentBlock(out env, primaryToken, false))
            {
                Log($"{FormatWin32Error("CreateEnvironmentBlock")} (continuing with null env)");
                env = IntPtr.Zero;
            }

            var startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = "winsta0\\default"
            };

            var commandLine = $"\"{exePath}\" {arguments}";
            var currentDirectory = Path.GetDirectoryName(exePath);
            Log("Launching via CreateProcessAsUser (active session).");
            var success = CreateProcessAsUser(primaryToken, null, commandLine, IntPtr.Zero, IntPtr.Zero, false, CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE, env, currentDirectory, ref startupInfo, out processInfo);
            if (!success)
            {
                error = FormatWin32Error("CreateProcessAsUser");
                Log(error);
                return false;
            }

            process = Process.GetProcessById((int)processInfo.dwProcessId);
            Log($"Launch OK. PID={(int)processInfo.dwProcessId}");
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
            if (processToken != IntPtr.Zero)
                CloseHandle(processToken);
            if (userToken != IntPtr.Zero)
                CloseHandle(userToken);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint SessionId, out IntPtr Token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr hToken, uint dwLogonFlags, string? lpApplicationName, string? lpCommandLine, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes, SECURITY_IMPERSONATION_LEVEL ImpersonationLevel, TOKEN_TYPE TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass, ref uint TokenInformation, uint TokenInformationLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr hToken, string? lpApplicationName, string? lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, ref LUID lpLuid);

    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    private const uint TOKEN_ALL_ACCESS = 0x000F01FF;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint LOGON_WITH_PROFILE = 0x00000001;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

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

    private enum TOKEN_INFORMATION_CLASS
    {
        TokenUser = 1,
        TokenGroups,
        TokenPrivileges,
        TokenOwner,
        TokenPrimaryGroup,
        TokenDefaultDacl,
        TokenSource,
        TokenType,
        TokenImpersonationLevel,
        TokenStatistics,
        TokenRestrictedSids,
        TokenSessionId,
        TokenGroupsAndPrivileges,
        TokenSessionReference,
        TokenSandBoxInert,
        TokenAuditPolicy,
        TokenOrigin
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

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    private static bool EnableRequiredPrivileges(IntPtr token, out string? error)
    {
        foreach (var privilege in RequiredPrivileges)
        {
            if (!EnablePrivilege(token, privilege, out error))
                return false;
        }

        error = null;
        return true;
    }

    private static bool EnablePrivilege(IntPtr token, string privilege, out string? error)
    {
        var luid = new LUID();
        if (!LookupPrivilegeValue(null, privilege, ref luid))
        {
            error = FormatWin32Error($"LookupPrivilegeValue({privilege})");
            return false;
        }

        var tp = new TOKEN_PRIVILEGES
        {
            PrivilegeCount = 1,
            Privileges = new LUID_AND_ATTRIBUTES
            {
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED
            }
        };

        if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
        {
            error = FormatWin32Error($"AdjustTokenPrivileges({privilege})");
            return false;
        }

        var lastError = Marshal.GetLastWin32Error();
        if (lastError == ERROR_NOT_ALL_ASSIGNED)
        {
            error = $"Privilege not held: {privilege}";
            return false;
        }

        error = null;
        return true;
    }

    private static string FormatWin32Error(string api)
    {
        var code = Marshal.GetLastWin32Error();
        return $"{api} failed. Win32={code} ({new Win32Exception(code).Message})";
    }

    private void Log(string message) => _log?.Invoke(message);
}
