using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wings.Application.Abstractions;
using Wings.Domain.Execution;

namespace Wings.Application.Execution;

/// <summary>
/// Shared state transitions for runs and jobs: recounting, settling unreported tests, infrastructure retries,
/// finishing runs and expiring leases of agents that stopped heartbeating.
/// </summary>
public sealed class ExecutionEngine(IWingsDbContext db, TimeProvider clock, IChangeNotifier notifier, IOptions<ExecutionOptions> options, ILogger<ExecutionEngine> log, IJobQueue jobs)
{
    /// <summary>Recomputes run totals from the latest attempt of every test.</summary>
    public async Task RecountAsync(Guid runId, CancellationToken ct)
    {
        var counts = await db.TestExecutions.Where(t => t.RunId == runId && !t.IsSuperseded)
            .GroupBy(t => t.Outcome).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        int C(string k) => counts.GetValueOrDefault(k);
        await db.ExecutionRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.TotalTests, counts.Values.Sum())
            .SetProperty(r => r.Passed, C("Passed"))
            .SetProperty(r => r.Failed, C("Failed"))
            .SetProperty(r => r.Skipped, C("Skipped"))
            .SetProperty(r => r.Blocked, C("Blocked"))
            .SetProperty(r => r.NotRun, C("NotRun"))
            .SetProperty(r => r.InfrastructureErrors, C("InfrastructureError")), ct);
    }

    /// <summary>Gives every still-pending test of the job a final outcome.</summary>
    public async Task SettlePendingTestsAsync(ExecutionJob job, string outcome, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.TestExecutions.Where(t => t.JobId == job.Id && t.Outcome == "Pending")
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Outcome, outcome).SetProperty(t => t.Reason, reason).SetProperty(t => t.CompletedAt, now), ct);
    }

    /// <summary>
    /// Ends a job that failed for infrastructure reasons. Tests that never produced a result are retried on a fresh job
    /// (another agent may pick it up) while retries remain; tests that did report keep their outcome, so nothing that
    /// finished runs twice.
    /// </summary>
    public async Task FailJobAsync(ExecutionJob job, JobStatus status, string error, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var run = await db.ExecutionRuns.FirstAsync(r => r.Id == job.RunId, ct);
        job.Status = status;
        job.Error = error;
        job.CompletedAt = now;
        job.LeaseExpiresAt = null;

        await ReleaseJobResourcesAsync(job.Id, ct);
        var pending = await db.TestExecutions.Where(t => t.JobId == job.Id && t.Outcome == "Pending").ToListAsync(ct);
        var retry = status == JobStatus.InfrastructureError && !run.CancelRequested && !job.CancelRequested && job.Attempt <= run.MaxInfrastructureRetries && pending.Count > 0;
        if (retry)
        {
            var next = new ExecutionJob
            {
                RunId = job.RunId, PackageVersionId = job.PackageVersionId, Framework = job.Framework, RequiresDesktop = job.RequiresDesktop,
                Status = JobStatus.Queued, Attempt = job.Attempt + 1, RetryOfJobId = job.Id, Wave = job.Wave, EnvironmentId = job.EnvironmentId,
                AgentPools = job.AgentPools, ResourceKeys = job.ResourceKeys, DataRequirementsJson = job.DataRequirementsJson,
                TestIdentifiersJson = JsonSerializer.Serialize(pending.Select(p => p.Identifier).ToList()),
            };
            db.ExecutionJobs.Add(next);
            // Jobs waiting on the failed attempt must now also wait for the retry.
            foreach (var dependent in await db.ExecutionJobDependencies.Where(d => d.DependsOnJobId == job.Id).Select(d => d.JobId).ToListAsync(ct))
                db.ExecutionJobDependencies.Add(new ExecutionJobDependency { JobId = dependent, DependsOnJobId = next.Id });
            foreach (var p in pending)
            {
                p.Outcome = "InfrastructureError"; p.Reason = error; p.CompletedAt = now; p.IsSuperseded = true;
                db.TestExecutions.Add(new TestExecution
                {
                    RunId = p.RunId, JobId = next.Id, Attempt = next.Attempt, PackageVersionId = p.PackageVersionId, PackageTestId = p.PackageTestId,
                    TestCaseId = p.TestCaseId, FeatureId = p.FeatureId, Identifier = p.Identifier, DisplayName = p.DisplayName, PrerequisitesJson = p.PrerequisitesJson,
                });
            }
            log.LogWarning("Job {JobId} failed ({Error}); queued retry attempt {Attempt}", job.Id, error, next.Attempt);
        }
        else
        {
            var outcome = status == JobStatus.Cancelled ? "NotRun" : "InfrastructureError";
            foreach (var p in pending) { p.Outcome = outcome; p.Reason = error; p.CompletedAt = now; }
        }
        await db.SaveChangesAsync(ct);
        await RecountAsync(job.RunId, ct);
        await TryFinishRunAsync(job.RunId, ct);
    }

    /// <summary>Marks the run finished once every job is terminal.</summary>
    public async Task TryFinishRunAsync(Guid runId, CancellationToken ct)
    {
        if (await db.ExecutionJobs.AnyAsync(j => j.RunId == runId && (j.Status == JobStatus.Queued || j.Status == JobStatus.Leased || j.Status == JobStatus.Running), ct)) return;
        // Manual tests still awaiting a tester keep the run open.
        if (await db.TestExecutions.AnyAsync(t => t.RunId == runId && t.Outcome == "Pending" && !t.IsSuperseded, ct)) return;
        var run = await db.ExecutionRuns.FirstAsync(r => r.Id == runId, ct);
        if (run.Status is RunStatus.Completed or RunStatus.Cancelled) return;
        run.Status = run.CancelRequested ? RunStatus.Cancelled : RunStatus.Completed;
        run.CompletedAt = clock.GetUtcNow();
        // Defect recurrence, retests, notifications and release gates run in the background, committed with the completion.
        await jobs.EnqueueAsync(Quality.RunFinishedJob.Type, new { runId }, ct);
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("runChanged", new { runId }, ct);
    }

    /// <summary>
    /// Called periodically. A job whose lease lapsed belongs to an agent that crashed, lost its network or was shut down;
    /// a running job far past its timeout is stuck. Either way the result is an infrastructure outcome, not a product failure.
    /// </summary>
    public async Task<int> ExpireLeasesAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var active = await db.ExecutionJobs.Include(j => j.Run)
            .Where(j => j.Status == JobStatus.Leased || j.Status == JobStatus.Running).ToListAsync(ct);
        var expired = active.Where(j => j.LeaseExpiresAt < now).ToList();
        var stuck = active.Except(expired).Where(j => j.Status == JobStatus.Running && j.StartedAt?.AddMinutes(j.Run!.TimeoutMinutes + 5) < now).ToList();

        foreach (var job in expired)
            await FailJobAsync(job, JobStatus.InfrastructureError, $"Agent stopped responding: lease expired at {job.LeaseExpiresAt:u}. Evidence received before the interruption is kept.", ct);
        foreach (var job in stuck)
        {
            job.CancelRequested = true; // the agent kills it on its next heartbeat if it is still alive
            await FailJobAsync(job, JobStatus.TimedOut, $"Exceeded the {job.Run!.TimeoutMinutes}-minute timeout.", ct);
        }
        foreach (var runId in expired.Concat(stuck).Select(j => j.RunId).Distinct()) await notifier.NotifyAsync("runChanged", new { runId }, ct);
        // Safety net: locks and reservations held by jobs that are no longer active.
        await db.ResourceLocks.Where(l => !db.ExecutionJobs.Any(j => j.Id == l.JobId && (j.Status == JobStatus.Leased || j.Status == JobStatus.Running))).ExecuteDeleteAsync(ct);
        await db.TestDataReservations.Where(r => r.Active != null && !db.ExecutionJobs.Any(j => j.Id == r.JobId && (j.Status == JobStatus.Leased || j.Status == JobStatus.Running)))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Active, (int?)null).SetProperty(r => r.ReleasedAt, now), ct);
        return expired.Count + stuck.Count;
    }

    /// <summary>Frees the job's resource locks and test-data reservations.</summary>
    public async Task ReleaseJobResourcesAsync(Guid jobId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.ResourceLocks.Where(l => l.JobId == jobId).ExecuteDeleteAsync(ct);
        await db.TestDataReservations.Where(r => r.JobId == jobId && r.Active != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Active, (int?)null).SetProperty(r => r.ReleasedAt, now), ct);
    }

    public DateTimeOffset NewLease() => clock.GetUtcNow().AddSeconds(options.Value.LeaseSeconds);
}
