using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wings.Agent.Contracts;
using Wings.Agent.Executors;

namespace Wings.Agent;

/// <summary>
/// Main loop: register, then heartbeat (renewing leases and receiving cancellations) and claim work whenever a slot is
/// free. Each claimed job runs on its own task with its own cancellation source.
/// </summary>
public sealed class AgentWorker(OrchestratorClient client, IOptions<AgentOptions> options, IEnumerable<ITestExecutor> executors, ILogger<AgentWorker> log) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();
    private TimeSpan _heartbeat = TimeSpan.FromSeconds(15);
    private bool _enabled = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        Directory.CreateDirectory(opts.WorkDirectory);
        var registration = Capabilities.Detect(opts);
        log.LogInformation("Agent on {Machine} ({Kind}, {Slots} slot(s)); frameworks: {Frameworks}; interactive desktop: {Interactive}",
            registration.MachineName, registration.Kind, registration.Slots, string.Join(", ", registration.Frameworks), registration.InteractiveSession);
        if (registration.Kind == AgentKinds.Desktop && !registration.InteractiveSession)
            log.LogWarning("Desktop agent without an interactive session: FlaUI jobs will not be dispatched here. Run the agent in a logged-in user session, not as a Windows service.");

        await RegisterAsync(registration, stoppingToken);
        var heartbeat = HeartbeatLoopAsync(stoppingToken);
        var exec = executors.ToList();

        while (!stoppingToken.IsCancellationRequested)
        {
            var free = registration.Slots - _running.Count;
            if (_enabled && free > 0)
            {
                try
                {
                    var claim = await client.ClaimAsync(free, stoppingToken);
                    foreach (var job in claim.Jobs)
                    {
                        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        _running[job.JobId] = cts;
                        _ = Task.Run(async () =>
                        {
                            try { await new JobRunner(job, opts, client, exec, log).RunAsync(cts.Token); }
                            catch (Exception ex) { log.LogError(ex, "Job {JobId} crashed the runner", job.JobId); }
                            finally { _running.TryRemove(job.JobId, out _); cts.Dispose(); }
                        }, CancellationToken.None);
                    }
                }
                catch (UnauthorizedAccessException ex) { log.LogError("{Message}", ex.Message); await Delay(TimeSpan.FromMinutes(1), stoppingToken); continue; }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { log.LogWarning("Claim failed: {Message}", ex.Message); }
            }
            await Delay(TimeSpan.FromSeconds(opts.PollSeconds), stoppingToken);
        }

        // Shutting down: running jobs were cancelled through the linked token and report themselves as cancelled.
        while (!_running.IsEmpty) await Task.Delay(200, CancellationToken.None);
        await heartbeat;
    }

    private async Task RegisterAsync(AgentRegistration registration, CancellationToken ct)
    {
        for (var attempt = 1; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                var r = await client.RegisterAsync(registration, ct);
                _heartbeat = TimeSpan.FromSeconds(Math.Max(5, r.HeartbeatSeconds));
                _enabled = r.Enabled;
                log.LogInformation("Registered as \"{Name}\" ({Id}); heartbeat every {Seconds}s{Disabled}", r.Name, r.AgentId, r.HeartbeatSeconds, r.Enabled ? "" : " — currently disabled or in maintenance");
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var wait = TimeSpan.FromSeconds(Math.Min(60, attempt * 5));
                log.LogWarning("Registration failed ({Message}); retrying in {Seconds}s", ex.Message, wait.TotalSeconds);
                await Delay(wait, ct);
            }
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await client.HeartbeatAsync(new HeartbeatRequest(_running.Keys.ToList(), Math.Max(0, options.Value.Slots - _running.Count)), ct);
                _enabled = response.Enabled;
                foreach (var id in response.CancelJobIds)
                    if (_running.TryGetValue(id, out var cts) && !cts.IsCancellationRequested)
                    {
                        log.LogWarning("Server requested cancellation of job {JobId}", id);
                        await cts.CancelAsync();
                    }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { log.LogWarning("Heartbeat failed: {Message}", ex.Message); }
            await Delay(_heartbeat, ct);
        }
    }

    private static async Task Delay(TimeSpan t, CancellationToken ct)
    {
        try { await Task.Delay(t, ct); } catch (OperationCanceledException) { }
    }
}
