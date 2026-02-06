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
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }
        finally
        {
            _logger.LogInformation("Privileged service stopping.");
        }
    }

    private void EnsureAgentRunning()
    {
        if (!File.Exists(_agentExePath))
        {
            _logger.LogWarning("Capture agent not found at {Path}.", _agentExePath);
            return;
        }

        if (_agentProcess != null && !_agentProcess.HasExited)
        {
            if (_agentSessionId != 0 && _agentSessionId == GetCurrentSessionId())
                return;

            try { _agentProcess.Kill(); } catch { }
            _agentProcess = null;
        }

        var args = $"--pipe \"{_pipeName}\"";
        if (_launcher.TryStartInActiveSession(_agentExePath, args, out var process, out var sessionId, out var error))
        {
            _agentProcess = process;
            _agentSessionId = sessionId;
            _logger.LogInformation("Capture agent started in session {SessionId}.", sessionId);
            return;
        }

        _logger.LogWarning("Failed to start capture agent: {Error}", error ?? "unknown error");
    }

    private static uint GetCurrentSessionId()
    {
        return WTSGetActiveConsoleSessionId();
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
