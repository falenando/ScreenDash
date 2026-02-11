using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteCore;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

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
    private int _consecutiveStartFailures;
    private readonly string _agentExePath;
    private readonly string _pipeName;

    public PrivilegedWorker(ILogger<PrivilegedWorker> logger)
    {
        _logger = logger;
        _pipeName = RemoteSupportPipe.PipeName;
        _agentExePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "PrivilegedHelper", "PrivilegedHelper.exe"));
        LogInformation($"Privileged service configured. Pipe={_pipeName} AgentPath={_agentExePath} BaseDir={AppContext.BaseDirectory}");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogInformation("Privileged service started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                EnsureAgentRunning();
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on stop
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

        if (_agentProcess != null && !_agentProcess.HasExited)
        {
            // If session changed, restart agent
            if (_agentSessionId == activeSessionId)
                return;

            LogInformation($"Active session changed from {_agentSessionId} to {activeSessionId}; restarting capture agent.");
            StopAgent();
        }

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

        if (_launcher.TryStartAsSystemInSession(activeSessionId, _agentExePath, args, out var systemProcess, out var systemError))
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


    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
