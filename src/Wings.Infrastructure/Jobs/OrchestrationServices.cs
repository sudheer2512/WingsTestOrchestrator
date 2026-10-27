using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wings.Application.Orchestration;

namespace Wings.Infrastructure.Jobs;

/// <summary>Starts due schedules (cron/once) every few seconds; the claim in <see cref="IScheduleService.RunDueAsync"/> makes this safe on several nodes.</summary>
public sealed class ScheduleRunner(IServiceScopeFactory scopes, IConfiguration config, ILogger<ScheduleRunner> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:RunWorker", true)) return;
        var interval = TimeSpan.FromSeconds(config.GetValue("Schedules:PollSeconds", 20));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var started = await scope.ServiceProvider.GetRequiredService<IScheduleService>().RunDueAsync(stoppingToken);
                if (started > 0) log.LogInformation("Started {Count} scheduled run(s)", started);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Schedule pass failed"); }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}

/// <summary>Probes environment health-check URLs periodically so dispatch and run start can avoid unavailable environments.</summary>
public sealed class EnvironmentHealthMonitor(IServiceScopeFactory scopes, IConfiguration config, ILogger<EnvironmentHealthMonitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:RunWorker", true)) return;
        var interval = TimeSpan.FromSeconds(config.GetValue("Environments:HealthCheckSeconds", 120));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IEnvironmentService>().CheckAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Environment health pass failed"); }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
