using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PrivilegedService;

public sealed class PrivilegedWorker : BackgroundService
{
    private readonly ILogger<PrivilegedWorker> _logger;

    public PrivilegedWorker(ILogger<PrivilegedWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Privileged service started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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
}
