using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wings.Application.Abstractions;
using Wings.Domain.Administration;
using Wings.Infrastructure.Persistence;

namespace Wings.Infrastructure.Jobs;

/// <summary>Wakes the worker immediately after an enqueue instead of waiting for the next poll.</summary>
public sealed class JobSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    public void Notify() => _channel.Writer.TryWrite(true);
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await _channel.Reader.ReadAsync(cts.Token); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}

/// <summary>Durable queue: the job row is written in the caller's unit of work, so it commits (or rolls back) with the business change.</summary>
public sealed class JobQueue(WingsDbContext db, TimeProvider clock, JobSignal signal) : IJobQueue
{
    public Task EnqueueAsync(string type, object payload, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        db.BackgroundJobs.Add(new BackgroundJob { Type = type, PayloadJson = JsonSerializer.Serialize(payload), CreatedAt = now, AvailableAt = now });
        signal.Notify();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Claims jobs with a conditional UPDATE (lease), so several API instances can share the queue safely. A job whose lease
/// expires (crashed worker) becomes claimable again; failures retry with exponential back-off up to MaxAttempts.
/// </summary>
public sealed class JobWorker(IServiceScopeFactory scopes, JobSignal signal, TimeProvider clock, ILogger<JobWorker> log, Microsoft.Extensions.Configuration.IConfiguration config) : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}" is var id && id.Length > 100 ? id[..100] : id;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:RunWorker", true)) return; // another node processes the queue
        while (!stoppingToken.IsCancellationRequested)
        {
            bool worked;
            try { worked = await RunOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Job worker loop failed");
                worked = false;
            }
            if (!worked) await signal.WaitAsync(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    public async Task<bool> RunOneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WingsDbContext>();
        var now = clock.GetUtcNow();

        var candidate = await db.BackgroundJobs.AsNoTracking()
            .Where(j => (j.Status == BackgroundJobStatus.Pending && j.AvailableAt <= now) || (j.Status == BackgroundJobStatus.Running && j.LockedUntil < now))
            .OrderBy(j => j.AvailableAt).Select(j => new { j.Id, j.Status, j.LockedUntil }).FirstOrDefaultAsync(ct);
        if (candidate is null) return false;

        var claimed = await db.BackgroundJobs
            .Where(j => j.Id == candidate.Id && j.Status == candidate.Status && j.LockedUntil == candidate.LockedUntil)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BackgroundJobStatus.Running)
                .SetProperty(j => j.LockedBy, _workerId)
                .SetProperty(j => j.LockedUntil, now + Lease)
                .SetProperty(j => j.Attempts, j => j.Attempts + 1), ct);
        if (claimed != 1) return true; // another worker won; look again

        var job = await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.Id == candidate.Id, ct);
        var handler = scope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(h => h.JobType == job.Type);
        try
        {
            if (handler is null) throw new InvalidOperationException($"No handler registered for job type '{job.Type}'.");
            await handler.HandleAsync(job.PayloadJson, ct);
            await db.BackgroundJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BackgroundJobStatus.Succeeded)
                .SetProperty(j => j.CompletedAt, clock.GetUtcNow())
                .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Job {JobId} ({Type}) attempt {Attempt} failed", job.Id, job.Type, job.Attempts);
            var final = job.Attempts >= job.MaxAttempts;
            var retryAt = clock.GetUtcNow() + TimeSpan.FromSeconds(Math.Pow(4, job.Attempts));
            await db.BackgroundJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, final ? BackgroundJobStatus.Failed : BackgroundJobStatus.Pending)
                .SetProperty(j => j.AvailableAt, retryAt)
                .SetProperty(j => j.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(j => j.LastError, ex.ToString()), ct);
            if (final && handler is IFailureAwareJobHandler aware) await aware.OnFinalFailureAsync(job.PayloadJson, ex, ct);
        }
        return true;
    }
}

public interface IFailureAwareJobHandler : IJobHandler
{
    Task OnFinalFailureAsync(string payloadJson, Exception error, CancellationToken ct);
}
