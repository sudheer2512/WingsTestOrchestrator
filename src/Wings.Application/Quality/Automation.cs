using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wings.Application.Abstractions;
using Wings.Application.Execution;
using Wings.Domain.Execution;

namespace Wings.Application.Quality;

public sealed class NotificationOptions
{
    /// <summary>A run is "unusually failing" when at least this share of its executed tests failed...</summary>
    public double HighFailureRatePercent { get; set; } = 30;
    /// <summary>...and it executed at least this many tests.</summary>
    public int HighFailureRateMinTests { get; set; } = 5;
    public int MonitorSeconds { get; set; } = 30;
    /// <summary>Absolute base URL used for links in emails, Teams messages and webhooks.</summary>
    public string? PublicUrl { get; set; }
}

/// <summary>Follow-up work when a run finishes: defect recurrence and retests, notifications, release gates.</summary>
public sealed class RunFinishedJob(IWingsDbContext db, IDefectService defects, IReleaseService releases, IBuildService builds, INotificationPublisher notifications,
    IOptions<NotificationOptions> options) : IJobHandler
{
    public const string Type = "run.finished";
    public string JobType => Type;

    private sealed record Payload(Guid RunId);

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var runId = JsonSerializer.Deserialize<Payload>(payloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.RunId;
        var run = await db.ExecutionRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return;

        await defects.ProcessRunAsync(runId, ct);
        await NotifyAsync(run, ct);

        var buildTriggered = await db.ApplicationBuilds.AnyAsync(b => b.TriggeredRunId == runId, ct);
        if (buildTriggered) await builds.OnRunFinishedAsync(runId, ct);
        else if (run.ReleaseId is { } rid) await releases.EvaluateAndNotifyAsync(rid, $"{ExecutionService.Format(run.Number)} finished", ct);
    }

    private async Task NotifyAsync(ExecutionRun run, CancellationToken ct)
    {
        var number = ExecutionService.Format(run.Number);
        var tests = await db.TestExecutions.AsNoTracking().Where(t => t.RunId == run.Id && !t.IsSuperseded).Select(t => new { t.TestCaseId, t.FeatureId, t.Outcome, t.DisplayName }).ToListAsync(ct);
        var featureIds = tests.Where(t => t.FeatureId is not null).Select(t => t.FeatureId!.Value).Distinct().ToList();
        var productId = await db.Features.Where(f => featureIds.Contains(f.Id)).GroupBy(f => f.ProductId).OrderByDescending(g => g.Count()).Select(g => (Guid?)g.Key).FirstOrDefaultAsync(ct);
        var link = $"/runs/{run.Id}";
        var data = new Dictionary<string, string> { ["run"] = number, ["passed"] = run.Passed.ToString(), ["failed"] = run.Failed.ToString(), ["build"] = run.BuildVersion ?? "" };
        var problems = run.Failed + run.Blocked + run.InfrastructureErrors;

        await notifications.PublishAsync(new NotificationMessage(NotificationEvents.RunCompleted, problems == 0 ? NotificationSeverity.Info : NotificationSeverity.Warning,
            $"{number} {(run.Status == RunStatus.Cancelled ? "cancelled" : "completed")}: {run.Passed} passed, {run.Failed} failed" + (run.Blocked > 0 ? $", {run.Blocked} blocked" : "")
                + (run.InfrastructureErrors > 0 ? $", {run.InfrastructureErrors} infrastructure errors" : ""),
            $"{run.Name}\nTriggered by {run.TriggeredBy ?? run.CreatedBy}{(run.BuildVersion is { } b ? $", build {b}" : "")}.", link, productId, data), ct);

        var failedCases = tests.Where(t => t.Outcome == "Failed" && t.TestCaseId is not null).Select(t => t.TestCaseId!.Value).Distinct().ToList();
        var critical = await db.TestCases.AsNoTracking().Where(t => failedCases.Contains(t.Id) && t.Priority == "P1").Select(t => new { t.Number, t.Name }).ToListAsync(ct);
        if (critical.Count > 0)
            await notifications.PublishAsync(new NotificationMessage(NotificationEvents.CriticalFailure, NotificationSeverity.Critical,
                $"{number}: {critical.Count} critical (P1) test(s) failed", string.Join("\n", critical.Select(c => $"- TC-{c.Number:00000} {c.Name}")), link, productId, data), ct);

        var executed = run.Passed + run.Failed + run.Blocked;
        var o = options.Value;
        if (executed >= o.HighFailureRateMinTests && 100.0 * (run.Failed + run.Blocked) / executed >= o.HighFailureRatePercent)
            await notifications.PublishAsync(new NotificationMessage(NotificationEvents.HighFailureRate, NotificationSeverity.Warning,
                $"{number}: {100.0 * (run.Failed + run.Blocked) / executed:0}% of executed tests failed",
                $"{run.Failed + run.Blocked} of {executed} executed tests failed or were blocked (threshold {o.HighFailureRatePercent:0}%). A widespread failure often points to the build or environment.",
                link, productId, data), ct);
    }
}

/// <summary>Periodic checks: agents that went offline, escalation of unacknowledged notifications, scheduled reports.</summary>
public sealed class OperationsWatch(IWingsDbContext db, INotificationPublisher notifications, INotificationService inbox, IReportService reports, TimeProvider clock,
    IOptions<ExecutionOptions> execution)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await CheckAgentsAsync(ct);
        await inbox.EscalateAsync(ct);
        await reports.DeliverDueAsync(ct);
    }

    public async Task<int> CheckAgentsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var threshold = now.AddSeconds(-execution.Value.OfflineAfterSeconds);
        var lost = await db.ExecutionAgents.Where(a => a.IsEnabled && !a.InMaintenance && a.LastHeartbeatAt != null && a.LastHeartbeatAt < threshold
            && (a.OfflineNotifiedAt == null || a.OfflineNotifiedAt < a.LastHeartbeatAt)).ToListAsync(ct);
        foreach (var agent in lost)
        {
            agent.OfflineNotifiedAt = now;
            db.PendingAuditAction = "WentOffline";
            await db.SaveChangesAsync(ct);
            await notifications.PublishAsync(new NotificationMessage(NotificationEvents.AgentOffline, NotificationSeverity.Warning,
                $"Agent {agent.Name} is offline", $"No heartbeat since {agent.LastHeartbeatAt:yyyy-MM-dd HH:mm:ss} UTC ({agent.Kind}, {agent.MachineName ?? "unknown machine"}). Jobs it held are re-queued when their lease expires.",
                "/agents", null, new Dictionary<string, string> { ["agent"] = agent.Name }), ct);
        }
        return lost.Count;
    }
}
