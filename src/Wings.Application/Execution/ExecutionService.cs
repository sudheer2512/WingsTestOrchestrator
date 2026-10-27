using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Administration;
using Wings.Domain.Execution;
using Wings.Domain.Orchestration;
using Wings.Application.Orchestration;
using Wings.Domain.Testing;

namespace Wings.Application.Execution;

public interface IExecutionService
{
    Task<RunSummaryDto> StartAsync(StartRunRequest request, CancellationToken ct = default);
    Task<RunSummaryDto> StartAsync(StartRunRequest request, RunOrigin origin, CancellationToken ct = default);
    Task<TestExecutionDto> RecordManualAsync(Guid runId, Guid testId, ManualResultRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ResourceLockDto>> ResourceLocksAsync(CancellationToken ct = default);
    Task<PagedResult<RunSummaryDto>> ListAsync(RunFilter filter, CancellationToken ct = default);
    Task<RunDetailDto> GetAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<LogLineDto>> LogsAsync(Guid jobId, int afterSequence, int max, CancellationToken ct = default);
    Task<RunSummaryDto> CancelAsync(Guid runId, CancellationToken ct = default);
    Task<RunSummaryDto> SetPausedAsync(Guid runId, bool paused, CancellationToken ct = default);
    Task<RunSummaryDto> SetPriorityAsync(Guid runId, int priority, CancellationToken ct = default);
    Task<(ArtifactDto Meta, Stream Content)> OpenArtifactAsync(Guid runId, Guid artifactId, CancellationToken ct = default);
    Task<IReadOnlyList<ExecutionHistoryDto>> HistoryAsync(Guid? featureId, Guid? testCaseId, int take, CancellationToken ct = default);
}

public sealed class ExecutionOptions
{
    public int LeaseSeconds { get; set; } = 90;
    public int HeartbeatSeconds { get; set; } = 15;
    /// <summary>An agent without a heartbeat for this long is shown offline.</summary>
    public int OfflineAfterSeconds { get; set; } = 60;
    public long MaxArtifactBytes { get; set; } = 500L * 1024 * 1024;
    public int MaxLogLinesPerJob { get; set; } = 20_000;
}

public sealed class ExecutionService(IWingsDbContext db, ISequenceAllocator sequences, ICurrentUser user, TimeProvider clock,
    IChangeNotifier notifier, IFileStore files, ExecutionEngine engine, IProductAccess access, Orchestration.IEnvironmentService environments,
    IPermissionService permissions) : IExecutionService
{
    public const string ScopeKey = "RUN";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Candidate(Guid PackageTestId, Guid VersionId, string Identifier, string DisplayName, Guid? TestCaseId, Guid? FeatureId);

    public Task<RunSummaryDto> StartAsync(StartRunRequest r, CancellationToken ct = default) => StartAsync(r, new RunOrigin(user.UserName), ct);

    public async Task<RunSummaryDto> StartAsync(StartRunRequest r, RunOrigin origin, CancellationToken ct = default)
    {
        var suiteIds = r.SuiteIds.Concat(r.SuiteId is { } one ? [one] : []).Distinct().ToList();
        new Validator()
            .Must(r.TestCaseIds.Count > 0 || r.FeatureId is not null || suiteIds.Count > 0 || r.PackageVersionId is not null || r.SequenceFrom is not null || r.RerunOfRunId is not null,
                "scope", "Select what to execute: test cases, a feature, a suite, a package version, features after a sequence number, or a previous run.")
            .Must(r.SequenceFrom is null || r.ProductId is not null, "productId", "A product is required with a sequence number.")
            .Must(r.Priority is >= 0 and <= 100, "priority", "Priority must be between 0 and 100.")
            .Must(r.MaxInfrastructureRetries is >= 0 and <= 5, "maxInfrastructureRetries", "Use 0–5 retries.")
            .Must(r.TimeoutMinutes is >= 1 and <= 24 * 60, "timeoutMinutes", "Timeout must be between 1 minute and 24 hours.")
            .Must(r.Variables.Keys.All(k => System.Text.RegularExpressions.Regex.IsMatch(k, "^[A-Z][A-Z0-9_]{0,63}$")), "variables", "Variable names must be UPPER_SNAKE_CASE.")
            .MaxLength(r.BuildVersion, "buildVersion", 100)
            .ThrowIfInvalid();

        var scope = new List<string>();
        var testCaseIds = new HashSet<Guid>(r.TestCaseIds);
        var featureIds = new HashSet<Guid>();
        var candidates = new List<Candidate>();

        if (r.TestCaseIds.Count > 0) scope.Add($"{r.TestCaseIds.Count} selected test case(s)");
        foreach (var suiteId in suiteIds)
        {
            var suite = await db.TestSuites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == suiteId, ct) ?? throw new NotFoundException("Test suite", suiteId);
            testCaseIds.UnionWith(await db.TestSuiteItems.Where(i => i.SuiteId == suiteId).Select(i => i.TestCaseId).ToListAsync(ct));
            scope.Add($"Suite {suite.Name}");
        }
        if (r.FeatureId is { } fid)
        {
            var root = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fid, ct) ?? throw new NotFoundException("Feature", fid);
            if (origin.CheckPermissions) await access.EnsureCanViewAsync(root.ProductId, ct);
            var ids = r.IncludeSubfeatures
                ? await db.Features.Where(f => f.SequenceScopeId == root.SequenceScopeId && f.Path.StartsWith(root.Path)).Select(f => f.Id).ToListAsync(ct)
                : [fid];
            featureIds.UnionWith(ids);
            scope.Add($"Feature [{root.SequenceNumber}] {root.Name}{(r.IncludeSubfeatures ? " incl. sub-features" : "")}");
        }
        if (r.SequenceFrom is { } seq)
        {
            var pid = r.ProductId!.Value;
            if (origin.CheckPermissions) await access.EnsureCanViewAsync(pid, ct);
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct) ?? throw new NotFoundException("Product", pid);
            featureIds.UnionWith(await db.Features.Where(f => f.ProductId == pid && f.SequenceNumber >= seq).Select(f => f.Id).ToListAsync(ct));
            scope.Add($"{product.Name} features created at or after sequence {seq}");
        }
        if (featureIds.Count > 0)
            testCaseIds.UnionWith(await db.TestCaseFeatureMappings.Where(m => featureIds.Contains(m.FeatureId)).Select(m => m.TestCaseId).ToListAsync(ct));

        if (r.PackageVersionId is { } pv)
        {
            var version = await db.TestPackageVersions.AsNoTracking().Include(v => v.Package).FirstOrDefaultAsync(v => v.Id == pv, ct) ?? throw new NotFoundException("Package version", pv);
            if (version.Status is not (PackageStatus.Published or PackageStatus.Approved))
                throw new ConflictException($"Only approved or published package versions can be executed (this one is {version.Status}).");
            candidates.AddRange(await Candidates(db.TestPackageTests.Where(t => t.VersionId == pv), ct));
            scope.Add($"Package {version.Package!.Name} v{version.VersionNumber}");
        }

        if (testCaseIds.Count > 0 || featureIds.Count > 0)
        {
            // Latest published version of each package; tests linked to the selected test cases or directly to the selected features.
            var published = db.TestPackageVersions.Where(v => v.Status == PackageStatus.Published);
            var latestPublished = published.Where(v => !published.Any(o => o.PackageId == v.PackageId && o.VersionNumber > v.VersionNumber)).Select(v => v.Id);
            candidates.AddRange(await Candidates(db.TestPackageTests.Where(t => latestPublished.Contains(t.VersionId)
                && ((t.TestCaseId != null && testCaseIds.Contains(t.TestCaseId.Value)) || (t.FeatureId != null && featureIds.Contains(t.FeatureId.Value)))), ct));
        }

        if (r.RerunOfRunId is { } source)
        {
            var src = await db.ExecutionRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == source, ct) ?? throw new NotFoundException("Run", source);
            var prior = db.TestExecutions.Where(t => t.RunId == source && !t.IsSuperseded && t.PackageTestId != null);
            if (r.OnlyFailed) prior = prior.Where(t => t.Outcome == "Failed" || t.Outcome == "InfrastructureError" || t.Outcome == "Blocked");
            var ids = await prior.Select(t => t.PackageTestId!.Value).ToListAsync(ct);
            candidates.AddRange(await Candidates(db.TestPackageTests.Where(t => ids.Contains(t.Id)), ct));
            scope.Add($"{(r.OnlyFailed ? "Failed tests" : "All tests")} of {Format(src.Number)}");
        }

        // ---- environment ----
        ResolvedEnvironment? env = null;
        if (r.EnvironmentId is { } envId)
        {
            env = await environments.ResolveAsync(envId, ct);
            var e = env.Environment;
            if (e.IsProduction && origin.CheckPermissions && (user.UserId is not { } uid || !(await permissions.GetPermissionsAsync(uid, ct)).Contains(Security.Permissions.ProductionRuns)))
                throw new ForbiddenException($"{e.Name} is a production environment; starting runs there needs the {Security.Permissions.ProductionRuns} permission.");
            if (!r.IgnoreEnvironmentHealth && (e.InMaintenance || e.Health == EnvironmentHealth.Unavailable))
                throw new ConflictException($"{e.Name} is {(e.InMaintenance ? "in maintenance" : $"unavailable ({e.HealthMessage})")}. Fix it, or start the run with \"ignore environment health\".");
            scope.Add($"on {e.Name}");
        }

        candidates = candidates.DistinctBy(c => c.PackageTestId).ToList();
        var automatedCaseIds = candidates.Where(c => c.TestCaseId != null).Select(c => c.TestCaseId!.Value).ToHashSet();
        var meta = await db.TestCases.AsNoTracking().Where(t => automatedCaseIds.Contains(t.Id))
            .Select(t => new CaseMeta(t.Id, t.IsDestructive, t.ResourceKeys, t.DataRequirements)).ToDictionaryAsync(t => t.Id, ct);
        var manualCases = await db.TestCases.AsNoTracking().Where(t => testCaseIds.Contains(t.Id) && !automatedCaseIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Number, t.Name, Feature = t.FeatureMappings.OrderByDescending(m => m.IsPrimary).Select(m => (Guid?)m.FeatureId).FirstOrDefault() })
            .ToListAsync(ct);

        // ---- production guard: destructive tests run on production only when explicitly authorised for that environment ----
        var excluded = new List<(Candidate Test, string Reason)>();
        if (env?.Environment.IsProduction == true)
        {
            var allowed = Orchestration.EnvironmentService.ParseIds(env.Environment.AllowedDestructiveTestCaseIds).ToHashSet();
            foreach (var c in candidates)
            {
                if (c.TestCaseId is not { } tc)
                    excluded.Add((c, "Production policy: automated tests must be linked to a test case so it is known whether they change data."));
                else if (meta[tc].IsDestructive && !allowed.Contains(tc))
                    excluded.Add((c, $"Production policy: destructive test case not authorised for {env.Environment.Name}."));
            }
            candidates = candidates.Except(excluded.Select(x => x.Test)).ToList();
        }

        if (candidates.Count == 0 && manualCases.Count == 0 && excluded.Count == 0) throw new ValidationException("scope", "The selection contains no test cases or automated tests.");
        if (candidates.Count == 0 && !(r.IncludeManual && manualCases.Count > 0))
            throw new ValidationException("scope", excluded.Count > 0
                ? $"All {excluded.Count} automated test(s) are blocked by the production policy of {env!.Environment.Name}."
                : $"None of the {manualCases.Count} selected test case(s) has a published automated test. Map uploaded tests to them and publish the package, or include manual tests.");

        // ---- test data: every referenced data set must exist and hold enough usable records for one job ----
        var needs = candidates.Where(c => c.TestCaseId is not null).SelectMany(c => Orchestration.TestDataService.Parse(meta[c.TestCaseId!.Value].DataRequirements))
            .GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Max(x => x.Count));
        foreach (var (code, count) in needs)
        {
            var set = await db.TestDataSets.AsNoTracking().FirstOrDefaultAsync(d => d.Code == code, ct) ?? throw new ValidationException("dataRequirements", $"Test data set '{code}' does not exist.");
            var usable = await db.TestDataRecords.CountAsync(x => x.DataSetId == set.Id && x.IsActive, ct);
            if (set.EnvironmentId is { } setEnv && setEnv != r.EnvironmentId)
                throw new ValidationException("dataRequirements", $"Test data set '{code}' belongs to another environment.");
            if (usable < count) throw new ValidationException("dataRequirements", $"Test data set '{code}' has {usable} active record(s); a test needs {count}.");
        }

        // ---- dependency waves: a test runs after the tests it depends on (test-case and feature prerequisites) ----
        var versions = await db.TestPackageVersions.AsNoTracking().Where(v => candidates.Select(c => c.VersionId).Contains(v.Id))
            .Select(v => new { v.Id, v.DetectedFramework, v.Platform }).ToDictionaryAsync(v => v.Id, ct);
        var tcFeatures = await db.TestCaseFeatureMappings.Where(m => automatedCaseIds.Contains(m.TestCaseId)).OrderByDescending(m => m.IsPrimary)
            .Select(m => new { m.TestCaseId, m.FeatureId }).ToListAsync(ct);
        Guid? FeatureOf(Candidate c) => c.FeatureId ?? tcFeatures.FirstOrDefault(m => m.TestCaseId == c.TestCaseId)?.FeatureId;
        var featureSet = candidates.Select(FeatureOf).Where(f => f is not null).Select(f => f!.Value).ToHashSet();
        var tcEdges = await db.TestCaseDependencies.Where(d => automatedCaseIds.Contains(d.TestCaseId) && d.Type != Domain.Features.DependencyType.Related)
            .Select(d => new { d.TestCaseId, d.DependsOnTestCaseId }).ToListAsync(ct);
        var featureEdges = await db.FeatureDependencies.Where(d => featureSet.Contains(d.FeatureId)
                && (d.Type == Domain.Features.DependencyType.Prerequisite || d.Type == Domain.Features.DependencyType.SetupDependency || d.Type == Domain.Features.DependencyType.DataDependency))
            .Select(d => new { d.FeatureId, d.DependsOnFeatureId }).ToListAsync(ct);
        var plan = DependencyPlanner.Plan(candidates.Select(c => new PlannerNode(c.PackageTestId, c.TestCaseId, FeatureOf(c), c.DisplayName)).ToList(),
            tcEdges.Select(e => (e.TestCaseId, e.DependsOnTestCaseId)), featureEdges.Select(e => (e.FeatureId, e.DependsOnFeatureId)));

        var runId = await db.InTransactionAsync(async () =>
        {
            var number = await sequences.NextAsync((await db.SequenceScopes.FirstAsync(s => s.Key == ScopeKey, ct)).Id, ct);
            var now = clock.GetUtcNow();
            var run = new ExecutionRun
            {
                Number = number, Name = r.Name.Clean() ?? string.Join("; ", scope), ScopeDescription = string.Join("; ", scope), ScopeJson = JsonSerializer.Serialize(r, Json),
                Priority = r.Priority, VariablesJson = JsonSerializer.Serialize(r.Variables, Json), MaxInfrastructureRetries = r.MaxInfrastructureRetries,
                TimeoutMinutes = r.TimeoutMinutes, SourceRunId = r.RerunOfRunId, Status = RunStatus.Queued, EnvironmentId = r.EnvironmentId,
                BuildVersion = r.BuildVersion.Clean() ?? env?.Environment.ApplicationBuildVersion, PlanId = origin.PlanId, CampaignId = origin.CampaignId,
                ScheduleId = origin.ScheduleId, TriggeredBy = origin.TriggeredBy, IncludeManual = r.IncludeManual, CreatedBy = origin.TriggeredBy,
            };
            db.ExecutionRuns.Add(run);

            var executionIds = candidates.ToDictionary(c => c.PackageTestId, _ => Guid.CreateVersion7());
            var jobOf = new Dictionary<Guid, Guid>();
            var jobs = new List<ExecutionJob>();
            foreach (var group in candidates.GroupBy(c => (c.VersionId, Wave: plan.Levels[c.PackageTestId])).OrderBy(g => g.Key.Wave))
            {
                var v = versions[group.Key.VersionId];
                var framework = v.DetectedFramework ?? "UNKNOWN";
                var cases = group.Where(c => c.TestCaseId is not null).Select(c => meta[c.TestCaseId!.Value]).ToList();
                var job = new ExecutionJob
                {
                    RunId = run.Id, PackageVersionId = group.Key.VersionId, Framework = framework, Status = JobStatus.Queued, Wave = group.Key.Wave,
                    RequiresDesktop = framework == "FLAUI" || v.Platform == Domain.Features.ApplicationType.WinForms,
                    TestIdentifiersJson = JsonSerializer.Serialize(group.Select(c => c.Identifier).ToList()),
                    EnvironmentId = r.EnvironmentId, AgentPools = env?.Environment.AgentPools,
                    ResourceKeys = JoinKeys(cases.SelectMany(m => SplitKeys(m.ResourceKeys))),
                    DataRequirementsJson = cases.SelectMany(m => Orchestration.TestDataService.Parse(m.DataRequirements)).GroupBy(d => d.Code)
                        .ToDictionary(g => g.Key, g => g.Max(x => x.Count)) is { Count: > 0 } d ? JsonSerializer.Serialize(d) : null,
                };
                jobs.Add(job);
                db.ExecutionJobs.Add(job);
                foreach (var c in group)
                {
                    jobOf[c.PackageTestId] = job.Id;
                    var pre = plan.Prerequisites.GetValueOrDefault(c.PackageTestId) ?? [];
                    db.TestExecutions.Add(new TestExecution
                    {
                        Id = executionIds[c.PackageTestId], RunId = run.Id, JobId = job.Id, PackageVersionId = group.Key.VersionId, PackageTestId = c.PackageTestId,
                        Identifier = c.Identifier, DisplayName = c.DisplayName, TestCaseId = c.TestCaseId, FeatureId = FeatureOf(c),
                        PrerequisitesJson = pre.Count == 0 ? null : JsonSerializer.Serialize(pre.Select(p => executionIds[p])),
                    });
                }
            }
            foreach (var job in jobs)
            {
                var tests = candidates.Where(c => jobOf[c.PackageTestId] == job.Id);
                foreach (var dep in tests.SelectMany(c => plan.Prerequisites.GetValueOrDefault(c.PackageTestId) ?? []).Select(p => jobOf[p]).Where(j => j != job.Id).Distinct())
                    db.ExecutionJobDependencies.Add(new ExecutionJobDependency { JobId = job.Id, DependsOnJobId = dep });
            }
            foreach (var (c, reason) in excluded)
                db.TestExecutions.Add(new TestExecution
                {
                    RunId = run.Id, PackageVersionId = c.VersionId, PackageTestId = c.PackageTestId, Identifier = c.Identifier, DisplayName = c.DisplayName,
                    TestCaseId = c.TestCaseId, FeatureId = FeatureOf(c), Outcome = "NotRun", Reason = reason, CompletedAt = now,
                });
            foreach (var m in manualCases)
                db.TestExecutions.Add(new TestExecution
                {
                    RunId = run.Id, Identifier = $"TC-{m.Number}", DisplayName = m.Name, TestCaseId = m.Id, FeatureId = m.Feature,
                    Outcome = r.IncludeManual ? "Pending" : "NotRun",
                    Reason = r.IncludeManual ? "Manual test case: awaiting a tester's result." : "Manual test case: no published automated test is mapped to it.",
                    CompletedAt = r.IncludeManual ? null : now,
                });
            if (jobs.Count == 0) { run.Status = RunStatus.Running; run.StartedAt = now; }
            db.AuditLogs.Add(new AuditLog { Timestamp = now, UserName = origin.TriggeredBy, Action = "ExecutionStarted", EntityType = nameof(ExecutionRun), EntityId = run.Id,
                RootEntityType = nameof(ExecutionRun), RootEntityId = run.Id, Summary = $"{Format(number)}: {run.ScopeDescription}" });
            await db.SaveChangesAsync(ct);
            await engine.RecountAsync(run.Id, ct);
            return run.Id;
        }, ct);

        await engine.TryFinishRunAsync(runId, ct);
        await notifier.NotifyAsync("runChanged", new { runId }, ct);
        return (await ListQuery(db.ExecutionRuns.Where(x => x.Id == runId), ct)).Single();
    }

    public async Task<IReadOnlyList<ResourceLockDto>> ResourceLocksAsync(CancellationToken ct = default)
    {
        var rows = await db.ResourceLocks.AsNoTracking().OrderBy(l => l.Key)
            .Join(db.ExecutionRuns, l => l.RunId, r => r.Id, (l, r) => new { l, r.Number, r.Name })
            .Select(x => new { x.l, x.Number, x.Name, Agent = db.ExecutionJobs.Where(j => j.Id == x.l.JobId).Select(j => j.Agent!.Name).FirstOrDefault() }).ToListAsync(ct);
        return rows.Select(x => new ResourceLockDto(x.l.Key, x.l.RunId, Format(x.Number), x.Name, x.l.JobId, x.Agent, x.l.AcquiredAt)).ToList();
    }

    private sealed record CaseMeta(Guid Id, bool IsDestructive, string? ResourceKeys, string? DataRequirements);

    internal static IEnumerable<string> SplitKeys(string? csv) => (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(k => k.ToLowerInvariant());
    private static string? JoinKeys(IEnumerable<string> keys) => keys.Distinct().Order().ToList() is { Count: > 0 } l ? string.Join(',', l) : null;

    public async Task<TestExecutionDto> RecordManualAsync(Guid runId, Guid testId, ManualResultRequest request, CancellationToken ct = default)
    {
        var allowed = new[] { "Passed", "Failed", "Blocked", "Skipped" };
        new Validator().Must(allowed.Contains(request.Outcome), "outcome", "Outcome must be Passed, Failed, Blocked or Skipped.")
            .Must(request.Outcome == "Passed" || !string.IsNullOrWhiteSpace(request.Comment), "comment", "Describe what happened for a non-passing result.")
            .MaxLength(request.Comment, "comment", 4000).ThrowIfInvalid();
        var run = await db.ExecutionRuns.FirstOrDefaultAsync(x => x.Id == runId, ct) ?? throw new NotFoundException("Run", runId);
        if (run.Status == RunStatus.Cancelled) throw new ConflictException("The run was cancelled.");
        var test = await db.TestExecutions.FirstOrDefaultAsync(t => t.Id == testId && t.RunId == runId, ct) ?? throw new NotFoundException("Test", testId);
        if (test.JobId is not null) throw new ConflictException("Automated results come from agents and cannot be entered by hand.");
        var now = clock.GetUtcNow();
        test.Outcome = request.Outcome;
        test.ErrorMessage = request.Outcome == "Passed" ? null : request.Comment.Clean();
        test.Output = request.Outcome == "Passed" ? request.Comment.Clean() : null;
        test.Reason = null;
        test.ExecutedBy = user.UserName;
        test.StartedAt ??= now;
        test.CompletedAt = now;
        db.AuditLogs.Add(new AuditLog { Timestamp = now, UserName = user.UserName, Action = "ManualResultRecorded", EntityType = nameof(TestExecution), EntityId = test.Id,
            RootEntityType = nameof(ExecutionRun), RootEntityId = runId, Summary = $"{test.DisplayName}: {request.Outcome}" });
        await db.SaveChangesAsync(ct);
        await engine.RecountAsync(runId, ct);
        await engine.TryFinishRunAsync(runId, ct);
        await notifier.NotifyAsync("runChanged", new { runId }, ct);
        return (await GetAsync(runId, ct)).Tests.Single(t => t.Id == testId);
    }

    private async Task<List<Candidate>> Candidates(IQueryable<TestPackageTest> q, CancellationToken ct)
        => await q.Select(t => new Candidate(t.Id, t.VersionId, t.Identifier, t.DisplayName, t.TestCaseId, t.FeatureId)).ToListAsync(ct);

    public async Task<PagedResult<RunSummaryDto>> ListAsync(RunFilter filter, CancellationToken ct = default)
    {
        var q = db.ExecutionRuns.AsNoTracking();
        if (filter.Status is { } s) q = q.Where(x => x.Status == s);
        if (filter.PlanId is { } plan) q = q.Where(x => x.PlanId == plan);
        if (filter.CampaignId is { } campaign) q = q.Where(x => x.CampaignId == campaign);
        if (filter.EnvironmentId is { } env) q = q.Where(x => x.EnvironmentId == env);
        if (filter.ScheduleId is { } schedule) q = q.Where(x => x.ScheduleId == schedule);
        if (filter.Text.Clean() is { } text)
        {
            var digits = text.ToUpperInvariant().Replace("RUN-", "").TrimStart('0');
            q = long.TryParse(digits, out var n) ? q.Where(x => x.Number == n || EF.Functions.Like(x.Name, Search.Like(text), Search.Escape)) : q.Where(x => EF.Functions.Like(x.Name, Search.Like(text), Search.Escape) || EF.Functions.Like(x.ScopeDescription, Search.Like(text), Search.Escape));
        }
        var page = new PageRequest(filter.Page, filter.PageSize);
        var total = await q.CountAsync(ct);
        var items = await ListQuery(q.OrderByDescending(x => x.Number).Skip(page.Skip).Take(page.SafePageSize), ct);
        return new PagedResult<RunSummaryDto>(items, total, page.SafePage, page.SafePageSize);
    }

    private async Task<List<RunSummaryDto>> ListQuery(IQueryable<ExecutionRun> q, CancellationToken ct)
    {
        var rows = await q.Select(x => new { x, Jobs = x.Jobs.Count, Env = db.TestEnvironments.IgnoreQueryFilters().Where(e => e.Id == x.EnvironmentId).Select(e => e.Name).FirstOrDefault() }).ToListAsync(ct);
        return rows.Select(r => ToSummary(r.x, r.Jobs, r.Env)).ToList();
    }

    internal static string Format(long number) => SequenceFormat.Format(number, 5, "RUN-");

    private static RunSummaryDto ToSummary(ExecutionRun x, int jobs, string? env) => new(x.Id, x.Number, Format(x.Number), x.Name, x.Status, x.ScopeDescription, x.Priority,
        x.SchedulingPaused, x.CancelRequested, x.TotalTests, x.Passed, x.Failed, x.Skipped, x.Blocked, x.NotRun, x.InfrastructureErrors,
        x.TotalTests - x.Passed - x.Failed - x.Skipped - x.Blocked - x.NotRun - x.InfrastructureErrors, jobs, x.CreatedAt, x.CreatedBy, x.StartedAt, x.CompletedAt, x.SourceRunId,
        x.EnvironmentId, env, x.BuildVersion, x.TriggeredBy, x.PlanId, x.CampaignId, x.ScheduleId, x.IncludeManual);

    public async Task<RunDetailDto> GetAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await db.ExecutionRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId, ct) ?? throw new NotFoundException("Run", runId);
        var jobs = await db.ExecutionJobs.AsNoTracking().Where(j => j.RunId == runId).OrderBy(j => j.CreatedAt)
            .Join(db.TestPackageVersions, j => j.PackageVersionId, v => v.Id, (j, v) => new { j, v.VersionNumber, PackageName = v.Package!.Name, AgentName = j.Agent!.Name })
            .ToListAsync(ct);
        var artifacts = await db.ExecutionArtifacts.AsNoTracking().Where(a => a.RunId == runId).OrderBy(a => a.CreatedAt)
            .Select(a => new ArtifactDto(a.Id, a.JobId, a.TestExecutionId, a.Kind, a.FileName, a.ContentType, a.SizeBytes, a.Sha256, a.CreatedAt)).ToListAsync(ct);
        var tests = await TestRows(db.TestExecutions.Where(t => t.RunId == runId), artifacts, ct);
        var jobIds = jobs.Select(x => x.j.Id).ToList();
        var waiting = await db.ExecutionJobDependencies.Where(d => jobIds.Contains(d.JobId)
                && db.ExecutionJobs.Any(o => o.Id == d.DependsOnJobId && (o.Status == JobStatus.Queued || o.Status == JobStatus.Leased || o.Status == JobStatus.Running)))
            .GroupBy(d => d.JobId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var jobDtos = jobs.Select(x => new JobDto(x.j.Id, x.j.PackageVersionId, x.PackageName, x.VersionNumber, x.j.Framework, x.j.RequiresDesktop, x.j.Status,
            x.j.Attempt, x.j.AgentId, x.AgentName, x.j.LeaseExpiresAt, x.j.StartedAt, x.j.CompletedAt, x.j.CancelRequested, x.j.Error, x.j.ExitCode,
            JsonSerializer.Deserialize<List<string>>(x.j.TestIdentifiersJson)!.Count, x.j.LogLineCount, x.j.RetryOfJobId, x.j.Wave,
            waiting.GetValueOrDefault(x.j.Id), x.j.ResourceKeys, x.j.DataRequirementsJson)).ToList();
        var variables = JsonSerializer.Deserialize<Dictionary<string, string>>(run.VariablesJson ?? "{}") ?? [];
        var envName = run.EnvironmentId is { } envId ? await db.TestEnvironments.IgnoreQueryFilters().Where(e => e.Id == envId).Select(e => e.Name).FirstOrDefaultAsync(ct) : null;
        return new RunDetailDto(ToSummary(run, jobs.Count, envName), variables, jobDtos, tests, artifacts.Where(a => a.TestExecutionId is null).ToList(),
            run.MaxInfrastructureRetries, run.TimeoutMinutes);
    }

    private async Task<List<TestExecutionDto>> TestRows(IQueryable<TestExecution> q, List<ArtifactDto> artifacts, CancellationToken ct)
    {
        var rows = await q.AsNoTracking().OrderBy(t => t.IsSuperseded).ThenBy(t => t.Identifier).ThenByDescending(t => t.Attempt)
            .Select(t => new
            {
                t,
                TcNumber = db.TestCases.Where(c => c.Id == t.TestCaseId).Select(c => (long?)c.Number).FirstOrDefault(),
                FeatureName = db.Features.Where(f => f.Id == t.FeatureId).Select(f => f.Name).FirstOrDefault(),
                AgentName = db.ExecutionAgents.Where(a => a.Id == t.AgentId).Select(a => a.Name).FirstOrDefault(),
            }).ToListAsync(ct);
        var byTest = artifacts.Where(a => a.TestExecutionId != null).ToLookup(a => a.TestExecutionId!.Value);
        return rows.Select(r => new TestExecutionDto(r.t.Id, r.t.RunId, r.t.JobId, r.t.Attempt, r.t.Identifier, r.t.DisplayName, r.t.Outcome, r.t.Reason,
            r.t.DurationMs, r.t.ErrorMessage, r.t.StackTrace, r.t.StartedAt, r.t.CompletedAt, r.t.TestCaseId,
            r.TcNumber is { } n ? SequenceFormat.Format(n, 5, "TC-") : null, r.t.FeatureId, r.FeatureName, r.AgentName, r.t.IsSuperseded, byTest[r.t.Id].ToList(),
            r.t.ExecutedBy, r.t.JobId == null, r.t.PrerequisitesJson == null ? 0 : JsonSerializer.Deserialize<List<Guid>>(r.t.PrerequisitesJson)!.Count)).ToList();
    }

    public async Task<IReadOnlyList<LogLineDto>> LogsAsync(Guid jobId, int afterSequence, int max, CancellationToken ct = default)
        => await db.ExecutionLogs.AsNoTracking().Where(l => l.JobId == jobId && l.Sequence > afterSequence).OrderBy(l => l.Sequence).Take(Math.Clamp(max, 1, 5000))
            .Select(l => new LogLineDto(l.Sequence, l.Timestamp, l.Level, l.Message)).ToListAsync(ct);

    public async Task<RunSummaryDto> CancelAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await db.ExecutionRuns.Include(r => r.Jobs).FirstOrDefaultAsync(r => r.Id == runId, ct) ?? throw new NotFoundException("Run", runId);
        if (run.Status is RunStatus.Completed or RunStatus.Cancelled) throw new ConflictException("The run has already finished.");
        var now = clock.GetUtcNow();
        run.CancelRequested = true;
        foreach (var job in run.Jobs)
        {
            if (job.Status == JobStatus.Queued) { job.Status = JobStatus.Cancelled; job.CompletedAt = now; job.Error = "Cancelled before an agent picked it up."; }
            else if (job.IsActive) job.CancelRequested = true; // the agent stops it on its next heartbeat
        }
        db.AuditLogs.Add(new AuditLog { Timestamp = now, UserName = user.UserName, Action = "ExecutionCancelled", EntityType = nameof(ExecutionRun), EntityId = run.Id, RootEntityType = nameof(ExecutionRun), RootEntityId = run.Id });
        await db.SaveChangesAsync(ct);
        foreach (var job in run.Jobs.Where(j => j.Status == JobStatus.Cancelled)) await engine.SettlePendingTestsAsync(job, "NotRun", "Cancelled before execution.", ct);
        await db.TestExecutions.Where(t => t.RunId == runId && t.JobId == null && t.Outcome == "Pending")
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Outcome, "NotRun").SetProperty(t => t.Reason, "Run cancelled before the manual result was recorded.").SetProperty(t => t.CompletedAt, now), ct);
        await engine.RecountAsync(run.Id, ct);
        await engine.TryFinishRunAsync(run.Id, ct);
        await notifier.NotifyAsync("runChanged", new { runId }, ct);
        return (await ListQuery(db.ExecutionRuns.Where(x => x.Id == runId), ct)).Single();
    }

    public async Task<RunSummaryDto> SetPausedAsync(Guid runId, bool paused, CancellationToken ct = default)
    {
        var run = await db.ExecutionRuns.FirstOrDefaultAsync(r => r.Id == runId, ct) ?? throw new NotFoundException("Run", runId);
        run.SchedulingPaused = paused;
        db.AuditLogs.Add(new AuditLog { Timestamp = clock.GetUtcNow(), UserName = user.UserName, Action = paused ? "SchedulingPaused" : "SchedulingResumed", EntityType = nameof(ExecutionRun), EntityId = run.Id, RootEntityType = nameof(ExecutionRun), RootEntityId = run.Id });
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("runChanged", new { runId }, ct);
        return (await ListQuery(db.ExecutionRuns.Where(x => x.Id == runId), ct)).Single();
    }

    public async Task<RunSummaryDto> SetPriorityAsync(Guid runId, int priority, CancellationToken ct = default)
    {
        new Validator().Must(priority is >= 0 and <= 100, "priority", "Priority must be between 0 and 100.").ThrowIfInvalid();
        var run = await db.ExecutionRuns.FirstOrDefaultAsync(r => r.Id == runId, ct) ?? throw new NotFoundException("Run", runId);
        run.Priority = priority;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("runChanged", new { runId }, ct);
        return (await ListQuery(db.ExecutionRuns.Where(x => x.Id == runId), ct)).Single();
    }

    public async Task<(ArtifactDto Meta, Stream Content)> OpenArtifactAsync(Guid runId, Guid artifactId, CancellationToken ct = default)
    {
        var a = await db.ExecutionArtifacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == artifactId && x.RunId == runId, ct) ?? throw new NotFoundException("Artifact", artifactId);
        return (new ArtifactDto(a.Id, a.JobId, a.TestExecutionId, a.Kind, a.FileName, a.ContentType, a.SizeBytes, a.Sha256, a.CreatedAt), await files.OpenReadAsync(a.StorageKey, ct));
    }

    public async Task<IReadOnlyList<ExecutionHistoryDto>> HistoryAsync(Guid? featureId, Guid? testCaseId, int take, CancellationToken ct = default)
    {
        var q = db.TestExecutions.AsNoTracking().Where(t => t.CompletedAt != null && !t.IsSuperseded);
        if (featureId is { } f)
        {
            var tcIds = db.TestCaseFeatureMappings.Where(m => m.FeatureId == f).Select(m => m.TestCaseId);
            q = q.Where(t => t.FeatureId == f || (t.TestCaseId != null && tcIds.Contains(t.TestCaseId.Value)));
        }
        if (testCaseId is { } tc) q = q.Where(t => t.TestCaseId == tc);
        var rows = await q.OrderByDescending(t => t.CompletedAt).Take(Math.Clamp(take, 1, 500))
            .Join(db.ExecutionRuns, t => t.RunId, r => r.Id, (t, r) => new { t, r.Number, r.Name })
            .Select(x => new
            {
                x.t, x.Number, x.Name,
                Agent = db.ExecutionAgents.Where(a => a.Id == x.t.AgentId).Select(a => a.Name).FirstOrDefault(),
                Version = db.TestPackageVersions.Where(v => v.Id == x.t.PackageVersionId).Select(v => new { v.VersionNumber, v.Package!.Name }).FirstOrDefault(),
            }).ToListAsync(ct);
        return rows.Select(x => new ExecutionHistoryDto(x.t.RunId, Format(x.Number), x.Name, x.t.Id, x.t.Identifier, x.t.DisplayName, x.t.Outcome, x.t.ErrorMessage,
            x.t.DurationMs, x.t.CompletedAt, x.Agent, x.Version?.VersionNumber ?? 0, x.Version?.Name)).ToList();
    }
}
