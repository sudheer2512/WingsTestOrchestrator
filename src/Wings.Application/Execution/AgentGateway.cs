using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wings.Agent.Contracts;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Administration;
using Wings.Domain.Execution;
using Wings.Domain.Orchestration;
using Wings.Domain.Testing;

namespace Wings.Application.Execution;

/// <summary>Server side of the agent protocol. Every call is made by an authenticated agent and is checked against its leases.</summary>
public interface IAgentGateway
{
    Task<AgentRegistrationResponse> RegisterAsync(Guid agentId, AgentRegistration registration, CancellationToken ct = default);
    Task<HeartbeatResponse> HeartbeatAsync(Guid agentId, HeartbeatRequest request, CancellationToken ct = default);
    Task<ClaimResponse> ClaimAsync(Guid agentId, ClaimRequest request, CancellationToken ct = default);
    Task<(Stream Content, string FileName)> DownloadPackageAsync(Guid agentId, Guid versionId, CancellationToken ct = default);
    Task StartJobAsync(Guid agentId, Guid jobId, CancellationToken ct = default);
    Task AppendLogsAsync(Guid agentId, Guid jobId, LogBatch batch, CancellationToken ct = default);
    Task ReportResultsAsync(Guid agentId, Guid jobId, ResultBatch batch, CancellationToken ct = default);
    Task<Guid> UploadArtifactAsync(Guid agentId, Guid jobId, string kind, string fileName, string contentType, string? testIdentifier, Stream content, CancellationToken ct = default);
    Task CompleteJobAsync(Guid agentId, Guid jobId, JobCompletion completion, CancellationToken ct = default);
}

public sealed class AgentGateway(IWingsDbContext db, ExecutionEngine engine, IFileStore files, IChangeNotifier notifier, TimeProvider clock, IOptions<ExecutionOptions> options,
    Orchestration.IEnvironmentService environments) : IAgentGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AgentRegistrationResponse> RegisterAsync(Guid agentId, AgentRegistration reg, CancellationToken ct = default)
    {
        var agent = await db.ExecutionAgents.FirstAsync(a => a.Id == agentId, ct);
        agent.MachineName = Trim(reg.MachineName, 200);
        agent.OperatingSystem = Trim(reg.OperatingSystem, 200);
        agent.AgentVersion = Trim(reg.AgentVersion, 50);
        agent.Kind = reg.Kind is AgentKinds.Desktop or AgentKinds.Browser or AgentKinds.Api or AgentKinds.Database ? reg.Kind : agent.Kind;
        // A desktop session can only drive one UI at a time, whatever the agent asks for.
        agent.Slots = agent.Kind == AgentKinds.Desktop ? 1 : Math.Clamp(reg.Slots, 1, 32);
        agent.FrameworksCsv = string.Join(',', reg.Frameworks.Select(f => f.Trim().ToUpperInvariant()).Where(f => f.Length is > 0 and <= 50).Distinct());
        agent.InteractiveSession = reg.InteractiveSession && reg.ProcessSessionId is not 0;
        agent.ScreenResolution = Trim(reg.ScreenResolution, 50);
        agent.DpiScalePercent = reg.DpiScalePercent;
        agent.ProcessorCount = reg.ProcessorCount;
        agent.TotalMemoryMb = reg.TotalMemoryMb;
        agent.CapabilitiesJson = JsonSerializer.Serialize(new { reg.Tools, reg.Applications, reg.Browsers, reg.ProcessSessionId }, Json);
        agent.RegisteredAt = agent.LastHeartbeatAt = clock.GetUtcNow();
        db.PendingAuditAction = "AgentRegistered";
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("agentsChanged", new { agentId }, ct);
        return new AgentRegistrationResponse(agent.Id, agent.Name, options.Value.HeartbeatSeconds, options.Value.LeaseSeconds, agent.IsEnabled && !agent.InMaintenance);
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(Guid agentId, HeartbeatRequest request, CancellationToken ct = default)
    {
        var lease = engine.NewLease();
        await db.ExecutionAgents.Where(a => a.Id == agentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.LastHeartbeatAt, clock.GetUtcNow()), ct);
        var ids = request.ActiveJobIds.ToList();
        // Renew only leases this agent still holds; anything else the agent reports is no longer its job and must stop.
        await db.ExecutionJobs.Where(j => ids.Contains(j.Id) && j.AgentId == agentId && (j.Status == JobStatus.Leased || j.Status == JobStatus.Running))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseExpiresAt, lease), ct);
        var held = await db.ExecutionJobs.Where(j => ids.Contains(j.Id) && j.AgentId == agentId && (j.Status == JobStatus.Leased || j.Status == JobStatus.Running))
            .Select(j => new { j.Id, j.CancelRequested, RunCancel = j.Run!.CancelRequested }).ToListAsync(ct);
        var cancel = ids.Where(id => held.All(h => h.Id != id)).Concat(held.Where(h => h.CancelRequested || h.RunCancel).Select(h => h.Id)).Distinct().ToList();
        var agent = await db.ExecutionAgents.AsNoTracking().FirstAsync(a => a.Id == agentId, ct);
        return new HeartbeatResponse(cancel, agent.IsEnabled && !agent.InMaintenance, lease);
    }

    public async Task<ClaimResponse> ClaimAsync(Guid agentId, ClaimRequest request, CancellationToken ct = default)
    {
        var agent = await db.ExecutionAgents.AsNoTracking().FirstAsync(a => a.Id == agentId, ct);
        await db.ExecutionAgents.Where(a => a.Id == agentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.LastHeartbeatAt, clock.GetUtcNow()), ct);
        if (!agent.IsEnabled || agent.InMaintenance) return new ClaimResponse([]);

        var active = await db.ExecutionJobs.CountAsync(j => j.AgentId == agentId && (j.Status == JobStatus.Leased || j.Status == JobStatus.Running), ct);
        var free = Math.Min(request.FreeSlots, agent.Slots - active);
        if (free <= 0) return new ClaimResponse([]);

        var frameworks = agent.Frameworks.ToList();
        var desktopOk = agent.Kind == AgentKinds.Desktop && agent.InteractiveSession;
        var pool = "," + (agent.Pool ?? "") + ",";
        var candidates = await db.ExecutionJobs
            .Where(j => j.Status == JobStatus.Queued && frameworks.Contains(j.Framework) && (!j.RequiresDesktop || desktopOk)
                        && !j.Run!.SchedulingPaused && !j.Run.CancelRequested
                        // environment pools, and the environment must be usable right now
                        && (j.AgentPools == null || ("," + j.AgentPools + ",").Contains(pool))
                        && (j.EnvironmentId == null || db.TestEnvironments.Any(e => e.Id == j.EnvironmentId && !e.InMaintenance && e.Health != EnvironmentHealth.Unavailable))
                        // every job this one depends on has finished
                        && !db.ExecutionJobDependencies.Any(d => d.JobId == j.Id
                            && db.ExecutionJobs.Any(o => o.Id == d.DependsOnJobId && (o.Status == JobStatus.Queued || o.Status == JobStatus.Leased || o.Status == JobStatus.Running))))
            .OrderByDescending(j => j.Run!.Priority).ThenBy(j => j.Wave).ThenBy(j => j.CreatedAt)
            .Select(j => j.Id).Take(free * 4 + 4).ToListAsync(ct);

        var assigned = new List<JobAssignment>();
        foreach (var id in candidates)
        {
            if (assigned.Count >= free) break;
            if (!await BlockFailedPrerequisitesAsync(id, ct)) continue; // nothing left to run in this job
            var lease = engine.NewLease();
            var now = clock.GetUtcNow();
            var won = await TryAcquireAsync(id, agentId, lease, now, ct);
            if (!won) continue;
            assigned.Add(await BuildAssignmentAsync(id, lease, ct));
        }
        return new ClaimResponse(assigned);
    }

    /// <summary>
    /// Marks tests whose prerequisites did not pass as Blocked and removes them from the job. Returns false when no test is
    /// left (the job is then completed without an agent). Prerequisites are checked at their latest attempt.
    /// </summary>
    private async Task<bool> BlockFailedPrerequisitesAsync(Guid jobId, CancellationToken ct)
    {
        var tests = await db.TestExecutions.Where(t => t.JobId == jobId && t.Outcome == "Pending" && t.PrerequisitesJson != null).ToListAsync(ct);
        if (tests.Count == 0) return true;
        var job = await db.ExecutionJobs.FirstAsync(j => j.Id == jobId, ct);
        var preIds = tests.SelectMany(t => JsonSerializer.Deserialize<List<Guid>>(t.PrerequisitesJson!)!).Distinct().ToList();
        var pre = await db.TestExecutions.AsNoTracking().Where(t => preIds.Contains(t.Id)).Select(t => new { t.Id, t.PackageTestId, t.DisplayName }).ToListAsync(ct);
        var packageTests = pre.Select(p => p.PackageTestId).ToList();
        var latest = await db.TestExecutions.AsNoTracking().Where(t => t.RunId == job.RunId && !t.IsSuperseded && packageTests.Contains(t.PackageTestId))
            .Select(t => new { t.PackageTestId, t.Outcome }).ToListAsync(ct);
        var outcomeOf = pre.ToDictionary(p => p.Id, p => (latest.FirstOrDefault(l => l.PackageTestId == p.PackageTestId)?.Outcome ?? "Pending", p.DisplayName));

        var blocked = new List<TestExecution>();
        foreach (var t in tests)
        {
            var failed = JsonSerializer.Deserialize<List<Guid>>(t.PrerequisitesJson!)!.Select(p => outcomeOf[p]).Where(o => o.Item1 != "Passed").ToList();
            if (failed.Count == 0) continue;
            t.Outcome = "Blocked";
            t.Reason = $"Prerequisite did not pass: {string.Join(", ", failed.Select(f => $"{f.DisplayName} ({f.Item1})"))}.";
            t.CompletedAt = clock.GetUtcNow();
            blocked.Add(t);
        }
        if (blocked.Count == 0) return true;

        var remaining = JsonSerializer.Deserialize<List<string>>(job.TestIdentifiersJson)!.Except(blocked.Select(b => b.Identifier)).ToList();
        job.TestIdentifiersJson = JsonSerializer.Serialize(remaining);
        if (remaining.Count == 0)
        {
            job.Status = JobStatus.Completed;
            job.CompletedAt = clock.GetUtcNow();
            job.Error = "Not dispatched: every test in this job is blocked by a prerequisite that did not pass.";
        }
        await db.SaveChangesAsync(ct);
        await engine.RecountAsync(job.RunId, ct);
        if (remaining.Count == 0) await engine.TryFinishRunAsync(job.RunId, ct);
        await notifier.NotifyAsync("runChanged", new { runId = job.RunId }, ct);
        return remaining.Count > 0;
    }

    /// <summary>
    /// In one transaction: lock the job's shared resources, reserve its test data, and lease the job. If any resource or
    /// record is taken by another running job the whole attempt is rolled back and the job waits for a later poll.
    /// </summary>
    private async Task<bool> TryAcquireAsync(Guid jobId, Guid agentId, DateTimeOffset lease, DateTimeOffset now, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); // each attempt starts clean so a lock taken by an earlier job in this request cannot collide in memory
        var job = await db.ExecutionJobs.AsNoTracking().FirstAsync(j => j.Id == jobId, ct);
        var keys = ExecutionService.SplitKeys(job.ResourceKeys).ToList();
        if (keys.Count > 0 && await db.ResourceLocks.AnyAsync(l => keys.Contains(l.Key), ct)) return false; // held: wait (the unique key still guards races)
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var key in keys)
                db.ResourceLocks.Add(new ResourceLock { Key = key, JobId = jobId, RunId = job.RunId, AcquiredAt = now });
            if (job.DataRequirementsJson is { } json)
            {
                foreach (var (code, count) in JsonSerializer.Deserialize<Dictionary<string, int>>(json)!)
                {
                    var set = await db.TestDataSets.AsNoTracking().FirstOrDefaultAsync(d => d.Code == code, ct);
                    if (set is null) { await tx.RollbackAsync(ct); return false; }
                    var free = await db.TestDataRecords.Where(r => r.DataSetId == set.Id && r.IsActive
                            && !db.TestDataReservations.Any(x => x.RecordId == r.Id && x.Active != null))
                        .OrderBy(r => r.Key).Select(r => r.Id).Take(count).ToListAsync(ct);
                    if (free.Count < count) { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); return false; } // all in use: wait
                    foreach (var recordId in free)
                        db.TestDataReservations.Add(new TestDataReservation { RecordId = recordId, DataSetId = set.Id, RunId = job.RunId, JobId = jobId, ReservedAt = now });
                }
            }
            await db.SaveChangesAsync(ct);
            var won = await db.ExecutionJobs.Where(j => j.Id == jobId && j.Status == JobStatus.Queued)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Leased).SetProperty(j => j.AgentId, agentId)
                    .SetProperty(j => j.LeaseExpiresAt, lease).SetProperty(j => j.LeasedAt, now), ct);
            if (won != 1) { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); return false; }
            await db.TestExecutions.Where(t => t.JobId == jobId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AgentId, agentId), ct);
            await db.ExecutionRuns.Where(r => r.Id == job.RunId && r.Status == RunStatus.Queued)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Running).SetProperty(r => r.StartedAt, now), ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return true;
        }
        catch (DbUpdateException)
        {
            // A resource lock (primary key) or data reservation (unique filtered index) is held by another job.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<JobAssignment> BuildAssignmentAsync(Guid jobId, DateTimeOffset lease, CancellationToken ct)
    {
        var job = await db.ExecutionJobs.AsNoTracking().Include(j => j.Run).FirstAsync(j => j.Id == jobId, ct);
        var version = await db.TestPackageVersions.AsNoTracking().Include(v => v.Package).FirstAsync(v => v.Id == job.PackageVersionId, ct);
        var identifiers = JsonSerializer.Deserialize<List<string>>(job.TestIdentifiersJson)!;

        // Variables: environment (incl. decrypted secrets) < run variables.
        var variables = new Dictionary<string, string>();
        IReadOnlyList<string> secretNames = [];
        object? environment = null;
        if (job.EnvironmentId is { } envId)
        {
            var env = await environments.ResolveAsync(envId, ct);
            foreach (var (k, v) in env.Variables) variables[k] = v;
            secretNames = env.SecretNames;
            environment = new { env.Environment.Code, env.Environment.Name, env.Environment.BaseUrl, env.Environment.ApplicationBuildVersion, env.Environment.DatabaseReference, env.Environment.TenantCompany };
        }
        foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, string>>(job.Run!.VariablesJson ?? "{}") ?? []) variables[k] = v;
        if (job.Run.BuildVersion is { } build) variables.TryAdd("WINGS_APP_BUILD", build);

        var data = await db.TestDataReservations.AsNoTracking().Where(r => r.JobId == jobId && r.Active != null)
            .Join(db.TestDataRecords, r => r.RecordId, rec => rec.Id, (r, rec) => new { r.DataSetId, rec.Key, rec.ValuesJson })
            .Join(db.TestDataSets.IgnoreQueryFilters(), x => x.DataSetId, d => d.Id, (x, d) => new { d.Code, d.DataVersion, x.Key, x.ValuesJson }).ToListAsync(ct);
        var parameters = await db.TestExecutions.AsNoTracking().Where(t => t.JobId == jobId && t.TestCaseId != null)
            .Join(db.TestCases, t => t.TestCaseId, c => c.Id, (t, c) => new { t.Identifier, c.ParametersJson }).Where(x => x.ParametersJson != null).ToListAsync(ct);
        var context = new
        {
            run = new { id = job.RunId, number = ExecutionService.Format(job.Run.Number), name = job.Run.Name, buildVersion = job.Run.BuildVersion, attempt = job.Attempt },
            environment,
            data = data.GroupBy(d => d.Code).ToDictionary(g => g.Key, g => new
            {
                version = g.First().DataVersion,
                records = g.Select(r => new { key = r.Key, values = JsonDocument.Parse(r.ValuesJson).RootElement }).ToList(),
            }),
            parameters = parameters.ToDictionary(p => p.Identifier, p => JsonDocument.Parse(p.ParametersJson!).RootElement),
        };

        await notifier.NotifyAsync("runChanged", new { runId = job.RunId }, ct);
        return new JobAssignment
        {
            JobId = job.Id, RunId = job.RunId, Attempt = job.Attempt, PackageVersionId = version.Id, PackageName = version.Package!.Name,
            PackageVersionNumber = version.VersionNumber, PackageSha256 = version.Sha256, PackageFileName = version.OriginalFileName, Framework = job.Framework,
            TestIdentifiers = identifiers, Variables = variables, SecretNames = secretNames, ContextJson = JsonSerializer.Serialize(context, Json),
            TimeoutMinutes = job.Run.TimeoutMinutes, LeaseExpiresAt = lease,
        };
    }

    public async Task<(Stream Content, string FileName)> DownloadPackageAsync(Guid agentId, Guid versionId, CancellationToken ct = default)
    {
        // Agents may only fetch code they currently hold a job for.
        if (!await db.ExecutionJobs.AnyAsync(j => j.AgentId == agentId && j.PackageVersionId == versionId && (j.Status == JobStatus.Leased || j.Status == JobStatus.Running), ct))
            throw new ForbiddenException("This agent holds no active job for that package version.");
        var v = await db.TestPackageVersions.AsNoTracking().FirstAsync(x => x.Id == versionId, ct);
        return (await files.OpenReadAsync(v.ArchiveStorageKey!, ct), v.OriginalFileName);
    }

    public async Task StartJobAsync(Guid agentId, Guid jobId, CancellationToken ct = default)
    {
        var job = await HeldJobAsync(agentId, jobId, ct);
        if (job.Status == JobStatus.Running) return; // idempotent
        job.Status = JobStatus.Running;
        job.StartedAt = clock.GetUtcNow();
        job.LeaseExpiresAt = engine.NewLease();
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("runChanged", new { runId = job.RunId }, ct);
    }

    public async Task AppendLogsAsync(Guid agentId, Guid jobId, LogBatch batch, CancellationToken ct = default)
    {
        var job = await HeldJobAsync(agentId, jobId, ct, allowFinished: true);
        var max = options.Value.MaxLogLinesPerJob;
        var lines = new List<object>();
        var seq = job.LogLineCount;
        foreach (var l in batch.Lines.Take(2000))
        {
            seq++;
            var message = l.Message.Length > 4000 ? l.Message[..4000] + "…" : l.Message;
            if (seq <= max) db.ExecutionLogs.Add(new ExecutionLog { JobId = jobId, Sequence = seq, Timestamp = l.Timestamp, Level = Trim(l.Level, 20) ?? "info", Message = message });
            lines.Add(new { sequence = seq, timestamp = l.Timestamp, level = l.Level, message });
        }
        job.LogLineCount = seq;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("jobLog", new { runId = job.RunId, jobId, lines }, ct);
    }

    public async Task ReportResultsAsync(Guid agentId, Guid jobId, ResultBatch batch, CancellationToken ct = default)
    {
        var job = await HeldJobAsync(agentId, jobId, ct);
        var tests = await db.TestExecutions.Where(t => t.JobId == jobId).ToDictionaryAsync(t => t.Identifier, ct);
        var allowed = new HashSet<string> { TestOutcomes.Passed, TestOutcomes.Failed, TestOutcomes.Skipped, TestOutcomes.Blocked, TestOutcomes.NotRun, TestOutcomes.InfrastructureError };
        foreach (var r in batch.Results)
        {
            if (!tests.TryGetValue(r.Identifier, out var t)) continue; // not part of this job: ignored rather than trusted
            t.Outcome = allowed.Contains(r.Outcome) ? r.Outcome : TestOutcomes.Failed;
            t.DurationMs = r.DurationMs;
            t.ErrorMessage = Trim(r.ErrorMessage, 8000);
            t.StackTrace = Trim(r.StackTrace, 32000);
            t.Output = Trim(r.Output, 32000);
            t.StartedAt = r.StartedAt;
            t.CompletedAt = r.CompletedAt ?? clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
        var lease = engine.NewLease();
        await db.ExecutionJobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseExpiresAt, lease), ct);
        await engine.RecountAsync(job.RunId, ct);
        await notifier.NotifyAsync("runChanged", new { runId = job.RunId }, ct);
    }

    public async Task<Guid> UploadArtifactAsync(Guid agentId, Guid jobId, string kind, string fileName, string contentType, string? testIdentifier, Stream content, CancellationToken ct = default)
    {
        var job = await HeldJobAsync(agentId, jobId, ct, allowFinished: true);
        await using var limited = new Testing.LimitedReadStream(content, options.Value.MaxArtifactBytes);
        StoredBlob blob;
        try { blob = await files.PutAsync(limited, ct); }
        catch (Testing.LimitExceededException) { throw new ValidationException("file", "The artifact exceeds the configured size limit."); }
        var testId = testIdentifier is null ? null
            : await db.TestExecutions.Where(t => t.JobId == jobId && t.Identifier == testIdentifier).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        var artifact = new ExecutionArtifact
        {
            RunId = job.RunId, JobId = jobId, TestExecutionId = testId,
            Kind = kind is ArtifactKinds.Log or ArtifactKinds.Screenshot or ArtifactKinds.Trace or ArtifactKinds.Video or ArtifactKinds.Report ? kind : ArtifactKinds.Other,
            FileName = Path.GetFileName(fileName) is { Length: > 0 } n ? Trim(n, 400)! : "artifact", ContentType = Trim(contentType, 200) ?? "application/octet-stream",
            SizeBytes = blob.SizeBytes, Sha256 = blob.Sha256, StorageKey = blob.Key, CreatedAt = clock.GetUtcNow(),
        };
        db.ExecutionArtifacts.Add(artifact);
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("runChanged", new { runId = job.RunId }, ct);
        return artifact.Id;
    }

    public async Task CompleteJobAsync(Guid agentId, Guid jobId, JobCompletion completion, CancellationToken ct = default)
    {
        var job = await HeldJobAsync(agentId, jobId, ct, allowFinished: true);
        if (job.IsTerminal) return; // duplicate completion (e.g. retried request): idempotent
        job.ExitCode = completion.ExitCode;
        switch (completion.Status)
        {
            case JobCompletions.Completed:
                job.Status = JobStatus.Completed;
                job.CompletedAt = clock.GetUtcNow();
                job.LeaseExpiresAt = null;
                job.Error = Trim(completion.Error, 4000);
                await db.SaveChangesAsync(ct);
                await engine.ReleaseJobResourcesAsync(job.Id, ct);
                await engine.SettlePendingTestsAsync(job, TestOutcomes.NotRun, "The test runner finished without reporting this test (not found, filtered out or skipped by setup).", ct);
                await engine.RecountAsync(job.RunId, ct);
                await engine.TryFinishRunAsync(job.RunId, ct);
                break;
            case JobCompletions.Cancelled:
                await engine.FailJobAsync(job, JobStatus.Cancelled, completion.Error ?? "Cancelled on request.", ct);
                break;
            case JobCompletions.TimedOut:
                await engine.FailJobAsync(job, JobStatus.TimedOut, completion.Error ?? "The job exceeded its timeout.", ct);
                break;
            default:
                await engine.FailJobAsync(job, JobStatus.InfrastructureError, completion.Error ?? "The agent reported an infrastructure error.", ct);
                break;
        }
        db.AuditLogs.Add(new AuditLog { Timestamp = clock.GetUtcNow(), UserName = $"agent:{agentId}", Action = $"Job{job.Status}", EntityType = nameof(ExecutionJob), EntityId = job.Id,
            RootEntityType = nameof(ExecutionRun), RootEntityId = job.RunId, Summary = Trim(completion.Error, 1000) });
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("runChanged", new { runId = job.RunId }, ct);
    }

    /// <summary>Loads a job and verifies the calling agent still holds its lease. A lost lease means another outcome was already recorded.</summary>
    private async Task<ExecutionJob> HeldJobAsync(Guid agentId, Guid jobId, CancellationToken ct, bool allowFinished = false)
    {
        var job = await db.ExecutionJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw new NotFoundException("Job", jobId);
        if (job.AgentId != agentId) throw new ConflictException("This job is not assigned to this agent.");
        if (!job.IsActive && !(allowFinished && job.IsTerminal)) throw new ConflictException($"The job is {job.Status}; its lease is no longer held.");
        return job;
    }

    private static string? Trim(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;
}

public interface IAgentAdminService
{
    Task<IReadOnlyList<AgentDto>> ListAsync(CancellationToken ct = default);
    Task<AgentKeyResult> CreateAsync(AgentUpsert request, CancellationToken ct = default);
    Task<AgentDto> UpdateAsync(Guid id, AgentUpsert request, CancellationToken ct = default);
    Task<AgentKeyResult> RotateKeyAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Resolves an agent from its presented key; null when unknown or disabled.</summary>
    Task<ExecutionAgent?> AuthenticateAsync(string key, CancellationToken ct = default);
}

public sealed class AgentAdminService(IWingsDbContext db, TimeProvider clock, IOptions<ExecutionOptions> options, IChangeNotifier notifier) : IAgentAdminService
{
    public async Task<IReadOnlyList<AgentDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.ExecutionAgents.AsNoTracking().OrderBy(a => a.Name)
            .Select(a => new { a, Active = db.ExecutionJobs.Count(j => j.AgentId == a.Id && (j.Status == JobStatus.Leased || j.Status == JobStatus.Running)) })
            .ToListAsync(ct);
        return rows.Select(r => ToDto(r.a, r.Active)).ToList();
    }

    public async Task<AgentKeyResult> CreateAsync(AgentUpsert request, CancellationToken ct = default)
    {
        Validate(request);
        if (await db.ExecutionAgents.AnyAsync(a => a.Name == request.Name.Trim(), ct)) throw new ValidationException("name", "An agent with this name already exists.");
        var (key, hash) = NewKey();
        var agent = new ExecutionAgent { KeyHash = hash, KeyPrefix = key[..12] };
        Apply(agent, request);
        db.ExecutionAgents.Add(agent);
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("agentsChanged", new { agentId = agent.Id }, ct);
        return new AgentKeyResult(ToDto(agent, 0), key);
    }

    public async Task<AgentDto> UpdateAsync(Guid id, AgentUpsert request, CancellationToken ct = default)
    {
        Validate(request);
        var agent = await db.ExecutionAgents.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Agent", id);
        if (request.Version is { } v) db.SetOriginalVersion(agent, v);
        if (await db.ExecutionAgents.AnyAsync(a => a.Name == request.Name.Trim() && a.Id != id, ct)) throw new ValidationException("name", "An agent with this name already exists.");
        Apply(agent, request);
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("agentsChanged", new { agentId = id }, ct);
        return (await ListAsync(ct)).Single(a => a.Id == id);
    }

    public async Task<AgentKeyResult> RotateKeyAsync(Guid id, CancellationToken ct = default)
    {
        var agent = await db.ExecutionAgents.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Agent", id);
        var (key, hash) = NewKey();
        agent.KeyHash = hash;
        agent.KeyPrefix = key[..12];
        db.PendingAuditAction = "KeyRotated";
        await db.SaveChangesAsync(ct);
        return new AgentKeyResult((await ListAsync(ct)).Single(a => a.Id == id), key);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var agent = await db.ExecutionAgents.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Agent", id);
        if (await db.ExecutionJobs.AnyAsync(j => j.AgentId == id && (j.Status == JobStatus.Leased || j.Status == JobStatus.Running), ct))
            throw new ConflictException("The agent is running jobs. Put it in maintenance and wait for them to finish first.");
        db.ExecutionAgents.Remove(agent);
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("agentsChanged", new { agentId = id }, ct);
    }

    public async Task<ExecutionAgent?> AuthenticateAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200) return null;
        var hash = Hash(key);
        var agent = await db.ExecutionAgents.AsNoTracking().FirstOrDefaultAsync(a => a.KeyHash == hash, ct);
        return agent is { IsEnabled: true } ? agent : null;
    }

    private AgentDto ToDto(ExecutionAgent a, int active) => new(a.Id, a.Name, a.Kind, a.Pool, a.Description, a.IsEnabled, a.InMaintenance,
        a.LastHeartbeatAt is { } hb && hb > clock.GetUtcNow().AddSeconds(-options.Value.OfflineAfterSeconds), a.MachineName, a.OperatingSystem, a.AgentVersion,
        a.Slots, a.Frameworks, a.InteractiveSession, a.ScreenResolution, a.DpiScalePercent, a.ProcessorCount, a.TotalMemoryMb, a.CapabilitiesJson,
        a.LastHeartbeatAt, a.RegisteredAt, a.KeyPrefix, active, a.Version);

    private static void Validate(AgentUpsert r) => new Validator().Required(r.Name, "name", 100)
        .Must(r.Kind is AgentKinds.Desktop or AgentKinds.Browser or AgentKinds.Api or AgentKinds.Database, "kind", "Kind must be Desktop, Browser, Api or Database.")
        .MaxLength(r.Pool, "pool", 100).ThrowIfInvalid();

    private static void Apply(ExecutionAgent a, AgentUpsert r)
    {
        a.Name = r.Name.Trim(); a.Kind = r.Kind; a.Pool = r.Pool.Clean(); a.Description = r.Description.Clean(); a.IsEnabled = r.IsEnabled; a.InMaintenance = r.InMaintenance;
        if (a.Kind == AgentKinds.Desktop) a.Slots = 1;
    }

    private static (string Key, string Hash) NewKey()
    {
        var key = "wta_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (key, Hash(key));
    }

    /// <summary>Keys are 256-bit random values, so a plain SHA-256 (no salt or stretching) is sufficient to store them.</summary>
    private static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
