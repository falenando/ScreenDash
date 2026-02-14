using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteCore;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PrivilegedService;

public sealed class PrivilegedWorker : BackgroundService
{
    private readonly ILogger<PrivilegedWorker> _logger;
    private readonly SessionProcessLauncher _launcher = new();
    private readonly ConnectionLogger _fileLogger = new("privileged-service.log");
    private Process? _agentProcess;
    private uint _agentSessionId;
    private int _agentPid;
    private DateTime _lastStartAttemptUtc;
    private DateTime _suppressStartUntilUtc;
    private int _consecutiveStartFailures;
    private readonly string _agentExePath;
    private readonly string _pipeName;

    private static readonly TimeSpan GracefulExitBackoff = TimeSpan.FromSeconds(15);

    public PrivilegedWorker(ILogger<PrivilegedWorker> logger)
    {
        _logger = logger;
        _pipeName = RemoteSupportPipe.PipeName;
        LogInformation($"Tentativa direta");
        _agentExePath = ResolveAgentExePath();
        LogInformation($"Privileged service configured. Pipe={_pipeName} AgentPath={_agentExePath} BaseDir={AppContext.BaseDirectory}");
    }

    private static string ResolveAgentExePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            // Expected layout: <current>\PrivilegedService\PrivilegedService.exe and <current>\PrivilegedHelper\PrivilegedHelper.exe
            Path.GetFullPath(Path.Combine(baseDir, "..", "PrivilegedHelper", "PrivilegedHelper.exe")),
            // Some packaging layouts may place helper beside the service.
            Path.GetFullPath(Path.Combine(baseDir, "PrivilegedHelper.exe")),
            // When invoked from Velopack hook folder.
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "VelopackHooks", "PrivilegedHelper", "PrivilegedHelper.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "VelopackHooks", "PrivilegedHelper", "PrivilegedHelper.exe"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = candidate,                // ex: ...\PrivilegedHelper.exe
                        Arguments = $"--pipe \"{RemoteSupportPipe.PipeName}\"",
                        WorkingDirectory = Path.GetDirectoryName(candidate) ?? AppContext.BaseDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var process = Process.Start(startInfo);

                    return candidate;
                }
                catch (Exception ex)
                {
        
                    return candidate;
                }

                return candidate;
            }
        }

        return candidates[0];
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogInformation("Privileged service started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    EnsureAgentRunning();
                }
                catch (Exception ex)
                {
                    LogWarning($"Privileged service loop error: {ex}");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }
        catch (Exception ex)
        {
            LogWarning($"Privileged service fatal error: {ex}");
        }
        finally
        {
            StopAgent();
            LogInformation("Privileged service stopping.");
        }
    }

    private void StopAgent()
    {
        if (_agentProcess != null)
        {
            try { _agentProcess.Kill(); } catch { }
            _agentProcess = null;
        }

        _agentPid = 0;
    }

    private void EnsureAgentRunning()
    {
        if (!File.Exists(_agentExePath))
        {
            LogWarning($"Capture agent not found at {_agentExePath}.");
            return;
        }

        uint activeSessionId = WTSGetActiveConsoleSessionId();
        if (activeSessionId == 0xFFFFFFFF)
        {
            LogWarning("No active console session detected (WTSGetActiveConsoleSessionId returned 0xFFFFFFFF).");
            return;
        }

        if (_agentProcess != null)
        {
            if (!TryGetProcessState(_agentPid, out var hasExited, out var exitCode))
                return;

            if (hasExited)
            {
                LogWarning($"Capture agent exited. PID={_agentPid} ExitCode={exitCode} Session={_agentSessionId}.");

                // ExitCode=0 is treated as a graceful shutdown (e.g. agent chose to quit because no session
                // is ready yet, or it was asked to exit). Avoid tight restart loops that spam the Event Log.
                if (exitCode == 0)
                {
                    _suppressStartUntilUtc = DateTime.UtcNow + GracefulExitBackoff;
                    _consecutiveStartFailures = 0;
                }
                else
                {
                    _consecutiveStartFailures++;
                }

                _agentProcess = null;
                _agentPid = 0;
            }
            else
            {
                // If session changed, restart agent
                if (_agentSessionId == activeSessionId)
                    return;

                LogInformation($"Active session changed from {_agentSessionId} to {activeSessionId}; restarting capture agent.");
                StopAgent();
            }
        }

        if (DateTime.UtcNow < _suppressStartUntilUtc)
            return;

        // Avoid spawning duplicates if an agent already exists in the active session.
        // This can happen if the service restarts and loses the Process handle.
        try
        {
            var existing = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_agentExePath));
            foreach (var p in existing)
            {
                try
                {
                    if (p.HasExited)
                        continue;
                    if ((uint)p.SessionId != activeSessionId)
                        continue;

                    _agentProcess = p;
                    _agentSessionId = activeSessionId;
                    _agentPid = p.Id;
                    LogInformation($"Capture agent already running in session {activeSessionId}. PID={_agentPid}.");
                    return;
                }
                catch { }
            }
        }
        catch { }

        // Try to start as SYSTEM in the active session to support UAC capture
        var nowUtc = DateTime.UtcNow;
        var minIntervalMs = _consecutiveStartFailures >= 3 ? 5000 : 500;
        if ((nowUtc - _lastStartAttemptUtc).TotalMilliseconds < minIntervalMs)
            return;

        _lastStartAttemptUtc = nowUtc;

        var args = $"--pipe \"{_pipeName}\"";
        LogInformation($"Starting capture agent. Session={activeSessionId} Args={args}");

        if (_launcher.TryStartAsSystemInSession(activeSessionId, _agentExePath, args, "winsta0\\default", out var systemProcess, out var systemError))
        {
            _agentProcess = systemProcess;
            _agentSessionId = activeSessionId;
            _agentPid = systemProcess?.Id ?? 0;
            _consecutiveStartFailures = 0;
            LogInformation($"Capture agent started as SYSTEM in session {activeSessionId}. PID={_agentPid}.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(systemError))
            LogWarning($"Failed to start capture agent as SYSTEM in session {activeSessionId}: {systemError}");

        if (_launcher.TryStartAsSystemInSession(activeSessionId, _agentExePath, args, "winsta0\\SecureDesktop", out var secureDesktopProcess, out var secureDesktopError))
        {
            _agentProcess = secureDesktopProcess;
            _agentSessionId = activeSessionId;
            _agentPid = secureDesktopProcess?.Id ?? 0;
            _consecutiveStartFailures = 0;
            LogInformation($"Capture agent started as SYSTEM in session {activeSessionId} on SecureDesktop. PID={_agentPid}.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(secureDesktopError))
            LogWarning($"Failed to start capture agent as SYSTEM in session {activeSessionId} on SecureDesktop: {secureDesktopError}");

        if (_launcher.TryStartInActiveSession(_agentExePath, args, out var process, out var sessionId, out var error))
        {
            _agentProcess = process;
            _agentSessionId = sessionId;
            _agentPid = process?.Id ?? 0;
            _consecutiveStartFailures = 0;
            LogInformation($"Capture agent started in session {sessionId}. PID={_agentPid}.");
            return;
        }

        _consecutiveStartFailures++;
        LogWarning($"Failed to start capture agent in session {activeSessionId}: {systemError ?? "system launch failed"} / {error ?? "user launch failed"}");
    }

    private void LogInformation(string message)
    {
        _logger.LogInformation(message);
        _fileLogger.Log(message);
    }

    private void LogWarning(string message)
    {
        _logger.LogWarning(message);
        _fileLogger.Log(message);
    }

    private bool TryGetProcessState(int pid, out bool hasExited, out int exitCode)
    {
        hasExited = false;
        exitCode = 0;

        try
        {
            if (pid == 0)
                return false;

            try
            {
                using var existing = Process.GetProcessById(pid);
                hasExited = existing.HasExited;
                exitCode = hasExited ? existing.ExitCode : 0;
                return true;
            }
            catch (ArgumentException)
            {
                hasExited = true;
                exitCode = 0;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                LogWarning($"Capture agent process state unavailable for PID={pid}: {ex.Message}");
                hasExited = false;
                exitCode = 0;
                return true;
            }
        }
        catch (Exception ex)
        {
            LogWarning($"Capture agent process state check failed for PID={pid}: {ex}");
            return false;
        }
    }


    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
