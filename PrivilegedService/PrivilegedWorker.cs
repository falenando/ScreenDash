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
    private Process? _agentProcess;
    private uint _agentSessionId;
    private readonly string _agentExePath;
    private readonly string _pipeName;

    public PrivilegedWorker(ILogger<PrivilegedWorker> logger)
    {
        _logger = logger;
        _pipeName = RemoteSupportPipe.PipeName;
        _agentExePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "PrivilegedHelper", "PrivilegedHelper.exe"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Privileged service started.");

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
            _logger.LogInformation("Privileged service stopping.");
        }
    }

    private void StopAgent()
    {
        if (_agentProcess != null)
        {
            try { _agentProcess.Kill(); } catch { }
            _agentProcess = null;
        }
    }

    private void EnsureAgentRunning()
    {
        if (!File.Exists(_agentExePath))
        {
            _logger.LogWarning("Capture agent not found at {Path}.", _agentExePath);
            return;
        }

        uint activeSessionId = WTSGetActiveConsoleSessionId();
        if (activeSessionId == 0xFFFFFFFF) return;

        if (_agentProcess != null && !_agentProcess.HasExited)
        {
            // If session changed, restart agent
            if (_agentSessionId == activeSessionId)
                return;

            StopAgent();
        }

        // Try to start as SYSTEM in the active session to support UAC capture
        var args = $"--pipe \"{_pipeName}\"";

        if (_launcher.TryStartAsSystemInSession(activeSessionId, _agentExePath, args, out var systemProcess, out var systemError))
        {
            _agentProcess = systemProcess;
            _agentSessionId = activeSessionId;
            _logger.LogInformation("Capture agent started as SYSTEM in session {SessionId}.", activeSessionId);
            return;
        }

        if (_launcher.TryStartInActiveSession(_agentExePath, args, out var process, out var sessionId, out var error))
        {
            _agentProcess = process;
            _agentSessionId = sessionId;
            _logger.LogInformation("Capture agent started in session {SessionId}.", sessionId);
            return;
        }


        _logger.LogWarning("Failed to start capture agent: {SystemError} / {UserError}", systemError ?? "system launch failed", error ?? "user launch failed");
    }


    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
