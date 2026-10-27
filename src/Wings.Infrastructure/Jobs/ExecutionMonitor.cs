using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wings.Application.Execution;

namespace Wings.Infrastructure.Jobs;

/// <summary>Periodically expires leases of agents that stopped heartbeating and times out stuck jobs.</summary>
public sealed class ExecutionMonitor(IServiceScopeFactory scopes, IConfiguration config, ILogger<ExecutionMonitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:RunWorker", true)) return;
        var interval = TimeSpan.FromSeconds(config.GetValue("Execution:MonitorIntervalSeconds", 10));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var expired = await scope.ServiceProvider.GetRequiredService<ExecutionEngine>().ExpireLeasesAsync(stoppingToken);
                if (expired > 0) log.LogWarning("Expired {Count} job lease(s)", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Execution monitor pass failed"); }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
