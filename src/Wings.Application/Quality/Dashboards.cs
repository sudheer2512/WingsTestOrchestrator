using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Domain.Execution;
using Wings.Domain.Quality;
using Wings.Domain.Testing;

namespace Wings.Application.Quality;

/// <summary>Coverage and result figures shared by dashboards and reports, so both use the same denominators.</summary>
public static class Coverage
{
    public static readonly IReadOnlyDictionary<string, string> Definitions = new Dictionary<string, string>
    {
        ["features"] = "Features = non-deleted, non-deprecated nodes of a feature type that is not a container (modules and submodules are containers).",
        ["featureCoverage"] = "Feature coverage = features linked to at least one test case ÷ features.",
        ["requirementCoverage"] = "Requirement coverage = feature requirements linked to at least one test case ÷ feature requirements.",
        ["automation"] = "Test-case automation = test cases with automation status Automated ÷ test cases (excluding shared steps, templates and obsolete cases).",
        ["latest"] = "Latest result = outcome of the most recent completed execution of each test case (superseded infrastructure attempts excluded).",
        ["passRate"] = "Pass rate = Passed ÷ (Passed + Failed + Blocked). Skipped, not-run and infrastructure errors are excluded.",
        ["codeCoverage"] = "Code coverage (lines/branches executed) is not measured by the orchestrator; it comes from build tooling.",
        ["untestedChanged"] = "Untested changed features = features changed by a build or in an unreleased release's scope that have no passing test result since the change.",
    };

    public sealed record CaseRow(Guid Id, long Number, string Name, string Priority, AutomationStatus Automation, Guid ProductId, IReadOnlyList<Guid> FeatureIds, IReadOnlyList<string> Tags);
    public sealed record ResultRow(Guid TestCaseId, string Outcome, DateTimeOffset At, Guid RunId);

    public static async Task<List<CaseRow>> CasesAsync(IWingsDbContext db, Guid? productId, CancellationToken ct)
    {
        var q = db.TestCases.AsNoTracking().Where(t => !t.IsSharedSteps && !t.IsTemplate && t.ReviewStatus != ReviewStatus.Obsolete && t.FeatureMappings.Any());
        if (productId is { } p) q = q.Where(t => t.FeatureMappings.Any(m => m.Feature!.ProductId == p));
        return (await q.Select(t => new { t.Id, t.Number, t.Name, t.Priority, t.AutomationStatus, Product = t.FeatureMappings.Select(m => m.Feature!.ProductId).First(),
                Features = t.FeatureMappings.Select(m => m.FeatureId).ToList(), Tags = t.Tags.Select(x => x.Tag).ToList() }).ToListAsync(ct))
            .Select(t => new CaseRow(t.Id, t.Number, t.Name, t.Priority, t.AutomationStatus, t.Product, t.Features, t.Tags)).ToList();
    }

    /// <summary>Latest completed result of each test case (optionally since a date).</summary>
    public static async Task<Dictionary<Guid, ResultRow>> LatestAsync(IWingsDbContext db, IReadOnlyCollection<Guid> testCaseIds, DateTimeOffset? since, CancellationToken ct)
    {
        var q = db.TestExecutions.AsNoTracking().Where(t => t.TestCaseId != null && !t.IsSuperseded && t.Outcome != "Pending" && t.CompletedAt != null);
        if (since is { } s) q = q.Where(t => t.CompletedAt >= s);
        var rows = await q.Select(t => new { TestCaseId = t.TestCaseId!.Value, t.Outcome, t.CompletedAt, t.RunId }).ToListAsync(ct);
        var wanted = testCaseIds as IReadOnlySet<Guid> ?? testCaseIds.ToHashSet();
        return rows.Where(r => wanted.Contains(r.TestCaseId)).GroupBy(r => r.TestCaseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CompletedAt).Select(r => new ResultRow(r.TestCaseId, r.Outcome, r.CompletedAt!.Value, r.RunId)).First());
    }

    public sealed record FeatureRow(Guid Id, Guid ProductId, long SequenceNumber, string Name, string Path, int Depth, Guid? ParentId, string Type, bool IsContainer, bool IsDeprecated, Guid? IntroducedInVersionId, string Status);

    public static async Task<List<FeatureRow>> FeaturesAsync(IWingsDbContext db, Guid? productId, CancellationToken ct)
    {
        var containers = await db.FeatureTypes.AsNoTracking().Where(t => t.IsContainer).Select(t => t.Code).ToListAsync(ct);
        var q = db.Features.AsNoTracking();
        if (productId is { } p) q = q.Where(f => f.ProductId == p);
        return (await q.Select(f => new { f.Id, f.ProductId, f.SequenceNumber, f.Name, f.Path, f.Depth, f.ParentId, f.FeatureTypeCode, f.IsDeprecated, f.IntroducedInVersionId, f.StatusCode }).ToListAsync(ct))
            .Select(f => new FeatureRow(f.Id, f.ProductId, f.SequenceNumber, f.Name, f.Path, f.Depth, f.ParentId, f.FeatureTypeCode, containers.Contains(f.FeatureTypeCode), f.IsDeprecated, f.IntroducedInVersionId, f.StatusCode))
            .ToList();
    }

    public static double? Percent(int numerator, int denominator) => denominator == 0 ? null : Math.Round(100.0 * numerator / denominator, 1);
    public static bool IsExecuted(string? outcome) => outcome is "Passed" or "Failed" or "Blocked";
}

public sealed record TrendPoint(DateOnly Date, int Passed, int Failed, int Blocked, int Other);
public sealed record ProductRowDto(Guid ProductId, string Code, string Name, int Features, int FeaturesWithTests, int TestCases, int Automated, double? AutomationPercent,
    int LatestPassed, int LatestFailed, int OpenDefects, int CriticalDefects, string? NextRelease, string? NextReleaseReadiness);

public sealed record GlobalDashboardDto(int Products, int Features, int FeaturesWithTests, int FeaturesWithoutTests, int AutomatedTests, int ManualTests,
    int LatestPassed, int LatestFailed, int LatestBlocked, int NeverExecuted, int UntestedChangedFeatures, int OpenDefects, IReadOnlyDictionary<string, int> OpenDefectsBySeverity,
    double? AutomationCoverage, double? FeatureCoverage, double? RequirementCoverage, IReadOnlyList<ReleaseSummaryDto> Releases, IReadOnlyList<TrendPoint> Trend,
    IReadOnlyList<ProductRowDto> ProductRows, IReadOnlyDictionary<string, string> Definitions);

public sealed record ModuleRowDto(Guid FeatureId, long SequenceNumber, string Name, int Features, int FeaturesWithTests, int TestCases, int Automated, int Passed, int Failed,
    int NotExecuted, int OpenDefects, double? FeatureCoverage, double? PassRate);
public sealed record FeatureStatusDto(Guid FeatureId, long SequenceNumber, string Name, int Depth, Guid? ModuleId, int TestCases, int Automated, string Coverage, string? LatestOutcome, DateTimeOffset? LastExecutedAt, int OpenDefects);
public sealed record RiskAreaDto(Guid FeatureId, long SequenceNumber, string Name, string Reason, int Score);
public sealed record AutomationPoint(DateOnly Week, int TestCases, int Automated);
public sealed record BuildQualityDto(Guid BuildId, string BuildNumber, DateTimeOffset At, int ImpactedFeatures, string? RunNumber, Guid? RunId, int Passed, int Failed, string? GateStatus);
public sealed record DefectBucketDto(string Key, string Label, int Critical, int Major, int Minor, int Trivial, int Total);

public sealed record ProductDashboardDto(Guid ProductId, string ProductName, int Features, int FeaturesWithTests, int TestCases, int Automated, double? AutomationPercent,
    IReadOnlyList<ModuleRowDto> Modules, IReadOnlyList<FeatureStatusDto> FeatureStatus, IReadOnlyList<RunSummaryDto> LatestRegression, IReadOnlyList<TrendPoint> FailureTrend,
    IReadOnlyList<AutomationPoint> AutomationTrend, IReadOnlyList<RiskAreaDto> RiskAreas, IReadOnlyList<DefectBucketDto> DefectsByModule, IReadOnlyDictionary<string, int> DefectsByStatus,
    IReadOnlyList<BuildQualityDto> BuildQuality, IReadOnlyDictionary<string, string> Definitions);

public sealed record RunningJobDto(Guid JobId, Guid RunId, string RunNumber, string RunName, string Framework, string? AgentName, JobStatus Status, DateTimeOffset? StartedAt, int Tests);
public sealed record QueueRowDto(string Framework, bool RequiresDesktop, int Queued, DateTimeOffset? OldestQueuedAt);
public sealed record AgentUseDto(Guid AgentId, string Name, string Kind, bool IsOnline, bool InMaintenance, int Slots, int ActiveJobs, double BusyHours24h, double? Utilization24h);
public sealed record DurationRowDto(string Framework, int Jobs, double AverageSeconds, double MaxSeconds);
public sealed record ThroughputPoint(DateTimeOffset Hour, int Tests, int Failed);
public sealed record EnvironmentFailureDto(Guid? EnvironmentId, string Name, int InfrastructureErrors, int Runs, string Health);

public sealed record ExecutionDashboardDto(IReadOnlyList<RunningJobDto> Running, IReadOnlyList<QueueRowDto> Queue, int AgentsTotal, int AgentsOnline, int AgentsBusy,
    double? SlotUtilizationNow, IReadOnlyList<AgentUseDto> Agents, IReadOnlyList<RunSummaryDto> FailedRuns, IReadOnlyList<DurationRowDto> Durations,
    IReadOnlyList<ThroughputPoint> Throughput, int Retries7d, int Jobs7d, double? RetryRate, IReadOnlyList<EnvironmentFailureDto> EnvironmentFailures);

public interface IDashboardService
{
    Task<GlobalDashboardDto> GlobalAsync(CancellationToken ct = default);
    Task<ProductDashboardDto> ProductAsync(Guid productId, CancellationToken ct = default);
    Task<ExecutionDashboardDto> ExecutionAsync(CancellationToken ct = default);
}

public sealed partial class DashboardService(IWingsDbContext db, TimeProvider clock, IReleaseService releases, IExecutionService runs, IProductAccess access,
    IOptions<ExecutionOptions> executionOptions) : IDashboardService
{
    public async Task<GlobalDashboardDto> GlobalAsync(CancellationToken ct = default)
    {
        var visible = await access.VisibleProductIdsAsync(ct);
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name).ToListAsync(ct);
        if (visible is not null) products = products.Where(p => visible.Contains(p.Id)).ToList();
        var productIds = products.Select(p => p.Id).ToHashSet();

        var features = (await Coverage.FeaturesAsync(db, null, ct)).Where(f => productIds.Contains(f.ProductId) && !f.IsContainer && !f.IsDeprecated).ToList();
        var cases = (await Coverage.CasesAsync(db, null, ct)).Where(c => productIds.Contains(c.ProductId)).ToList();
        var latest = await Coverage.LatestAsync(db, cases.Select(c => c.Id).ToHashSet(), null, ct);
        var tested = cases.SelectMany(c => c.FeatureIds).ToHashSet();
        var withTests = features.Count(f => tested.Contains(f.Id));

        var requirements = await db.FeatureRequirements.AsNoTracking().Where(r => db.Features.Any(f => f.Id == r.FeatureId && productIds.Contains(f.ProductId))).Select(r => r.Id).ToListAsync(ct);
        var linkedRequirements = await db.TestCaseRequirementLinks.AsNoTracking().Where(l => requirements.Contains(l.RequirementId)).Select(l => l.RequirementId).Distinct().CountAsync(ct);

        var defects = await db.Defects.AsNoTracking().Where(d => DefectWorkflow.Unresolved.Contains(d.Status) && (d.ProductId == null || productIds.Contains(d.ProductId.Value)))
            .Select(d => new { d.ProductId, d.Severity }).ToListAsync(ct);
        var untested = await UntestedChangedAsync(productIds, latest, cases, ct);

        var releaseList = (await releases.ListAsync(null, ct)).Where(r => !r.IsReleased && productIds.Contains(r.ProductId)).ToList();
        var trend = await TrendAsync(null, 30, ct);

        var rows = products.Select(p =>
        {
            var pf = features.Where(f => f.ProductId == p.Id).ToList();
            var pc = cases.Where(c => c.ProductId == p.Id).ToList();
            var next = releaseList.Where(r => r.ProductId == p.Id).OrderBy(r => r.ReleaseDate ?? DateOnly.MaxValue).FirstOrDefault();
            var automated = pc.Count(c => c.Automation == AutomationStatus.Automated);
            return new ProductRowDto(p.Id, p.Code, p.Name, pf.Count, pf.Count(f => tested.Contains(f.Id)), pc.Count, automated, Coverage.Percent(automated, pc.Count),
                pc.Count(c => latest.GetValueOrDefault(c.Id)?.Outcome == "Passed"), pc.Count(c => latest.GetValueOrDefault(c.Id)?.Outcome is "Failed"),
                defects.Count(d => d.ProductId == p.Id), defects.Count(d => d.ProductId == p.Id && d.Severity == DefectSeverity.Critical), next?.VersionLabel, next?.Readiness);
        }).ToList();

        var auto = cases.Count(c => c.Automation == AutomationStatus.Automated);
        return new GlobalDashboardDto(products.Count, features.Count, withTests, features.Count - withTests, auto, cases.Count - auto,
            cases.Count(c => latest.GetValueOrDefault(c.Id)?.Outcome == "Passed"), cases.Count(c => latest.GetValueOrDefault(c.Id)?.Outcome == "Failed"),
            cases.Count(c => latest.GetValueOrDefault(c.Id)?.Outcome == "Blocked"), cases.Count(c => !latest.ContainsKey(c.Id)), untested.Count, defects.Count,
            Enum.GetValues<DefectSeverity>().ToDictionary(s => s.ToString(), s => defects.Count(d => d.Severity == s)),
            Coverage.Percent(auto, cases.Count), Coverage.Percent(withTests, features.Count), Coverage.Percent(linkedRequirements, requirements.Count),
            releaseList, trend, rows, Coverage.Definitions);
    }

    /// <summary>Features changed by builds in the last 30 days or in unreleased release scopes, with no passing result since the change.</summary>
    private async Task<List<Guid>> UntestedChangedAsync(IReadOnlySet<Guid> productIds, Dictionary<Guid, Coverage.ResultRow> latest, List<Coverage.CaseRow> cases, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-30);
        var fromBuilds = await (from i in db.BuildFeatureImpacts.Where(i => i.Reason != ImpactReason.Dependency)
                                join b in db.ApplicationBuilds on i.BuildId equals b.Id
                                where b.CreatedAt >= since && productIds.Contains(b.ProductId)
                                select new { i.FeatureId, b.CreatedAt }).ToListAsync(ct);
        var fromReleases = await (from c in db.ReleaseFeatureChanges.Where(c => c.Kind != FeatureChangeKind.Deprecated)
                                  join v in db.ProductVersions on c.ReleaseId equals v.Id
                                  where !v.IsReleased && productIds.Contains(v.ProductId)
                                  select new { c.FeatureId, c.CreatedAt }).ToListAsync(ct);
        var changed = fromBuilds.Concat(fromReleases).GroupBy(x => x.FeatureId).ToDictionary(g => g.Key, g => g.Max(x => x.CreatedAt));
        var byFeature = cases.SelectMany(c => c.FeatureIds.Select(f => (f, c.Id))).ToLookup(x => x.f, x => x.Id);
        return changed.Where(kv => !byFeature[kv.Key].Any(tc => latest.TryGetValue(tc, out var r) && r.Outcome == "Passed" && r.At >= kv.Value)).Select(kv => kv.Key).ToList();
    }

    private async Task<List<TrendPoint>> TrendAsync(IReadOnlySet<Guid>? testCaseIds, int days, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var since = new DateTimeOffset(today.AddDays(-(days - 1)).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await db.TestExecutions.AsNoTracking().Where(t => !t.IsSuperseded && t.CompletedAt >= since && t.Outcome != "Pending")
            .Select(t => new { t.TestCaseId, t.Outcome, t.CompletedAt }).ToListAsync(ct);
        if (testCaseIds is not null) rows = rows.Where(r => r.TestCaseId is { } id && testCaseIds.Contains(id)).ToList();
        var byDay = rows.GroupBy(r => DateOnly.FromDateTime(r.CompletedAt!.Value.UtcDateTime)).ToDictionary(g => g.Key, g => g.ToList());
        return Enumerable.Range(0, days).Select(i => today.AddDays(i - days + 1)).Select(d =>
        {
            var l = byDay.GetValueOrDefault(d) ?? [];
            return new TrendPoint(d, l.Count(x => x.Outcome == "Passed"), l.Count(x => x.Outcome == "Failed"), l.Count(x => x.Outcome == "Blocked"), l.Count(x => x.Outcome is not ("Passed" or "Failed" or "Blocked")));
        }).ToList();
    }

    public async Task<ProductDashboardDto> ProductAsync(Guid productId, CancellationToken ct = default)
    {
        await access.EnsureCanViewAsync(productId, ct);
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct) ?? throw new NotFoundException("Product", productId);
        var all = await Coverage.FeaturesAsync(db, productId, ct);
        var features = all.Where(f => !f.IsContainer && !f.IsDeprecated).ToList();
        var cases = await Coverage.CasesAsync(db, productId, ct);
        var caseIds = cases.Select(c => c.Id).ToHashSet();
        var latest = await Coverage.LatestAsync(db, caseIds, null, ct);
        var byFeature = cases.SelectMany(c => c.FeatureIds.Select(f => (f, c))).ToLookup(x => x.f, x => x.c);
        var defects = await db.Defects.AsNoTracking().Where(d => d.ProductId == productId).Select(d => new { d.FeatureId, d.Severity, d.Status }).ToListAsync(ct);
        var openDefects = defects.Where(d => DefectWorkflow.Unresolved.Contains(d.Status)).ToList();

        // Modules are the product's top-level nodes; each feature rolls up to the module on its path.
        var roots = all.Where(f => f.ParentId is null).OrderBy(f => f.SequenceNumber).ToList();
        FeatureStatusDto Status(Coverage.FeatureRow f)
        {
            var linked = byFeature[f.Id].DistinctBy(c => c.Id).ToList();
            var results = linked.Select(c => latest.GetValueOrDefault(c.Id)).Where(r => r is not null).ToList();
            var outcome = results.Count == 0 ? null : results.Any(r => r!.Outcome is "Failed" or "Blocked") ? "Failed" : results.Any(r => r!.Outcome == "Passed") ? "Passed" : results.First()!.Outcome;
            var automated = linked.Count(c => c.Automation == AutomationStatus.Automated);
            return new FeatureStatusDto(f.Id, f.SequenceNumber, f.Name, f.Depth, roots.FirstOrDefault(r => f.Path.StartsWith(r.Path))?.Id, linked.Count, automated,
                linked.Count == 0 ? "NONE" : automated == 0 ? "MANUAL" : automated < linked.Count ? "PARTIAL" : "AUTOMATED", outcome, results.Max(r => (DateTimeOffset?)r!.At),
                openDefects.Count(d => d.FeatureId == f.Id));
        }
        var statuses = features.OrderBy(f => f.Path.Length).ThenBy(f => f.SequenceNumber).Select(Status).ToList();

        var modules = roots.Select(m =>
        {
            var inModule = features.Where(f => f.Path.StartsWith(m.Path)).Select(f => f.Id).ToHashSet();
            var moduleCases = cases.Where(c => c.FeatureIds.Any(inModule.Contains) || c.FeatureIds.Contains(m.Id)).ToList();
            var outcomes = moduleCases.Select(c => latest.GetValueOrDefault(c.Id)?.Outcome).ToList();
            var featureIdsWithTests = statuses.Where(s => inModule.Contains(s.FeatureId) && s.TestCases > 0).Count();
            var passed = outcomes.Count(o => o == "Passed");
            var failed = outcomes.Count(o => o is "Failed" or "Blocked");
            var allInModule = all.Where(f => f.Path.StartsWith(m.Path)).Select(f => f.Id).ToHashSet();
            return new ModuleRowDto(m.Id, m.SequenceNumber, m.Name, inModule.Count, featureIdsWithTests, moduleCases.Count, moduleCases.Count(c => c.Automation == AutomationStatus.Automated),
                passed, failed, outcomes.Count(o => o is null), openDefects.Count(d => d.FeatureId is { } fid && allInModule.Contains(fid)),
                Coverage.Percent(featureIdsWithTests, inModule.Count), Coverage.Percent(passed, passed + failed));
        }).ToList();

        // Latest regression: runs from plans or suites of this product, newest first.
        var suiteIds = await db.TestSuites.AsNoTracking().Where(s => s.ProductId == productId).Select(s => s.Id).ToListAsync(ct);
        var recentRuns = await db.ExecutionRuns.AsNoTracking().Where(r => r.Status == RunStatus.Completed && db.TestExecutions.Any(t => t.RunId == r.Id && t.TestCaseId != null && caseIds.Contains(t.TestCaseId.Value)))
            .OrderByDescending(r => r.Number).Select(r => r.Id).Take(5).ToListAsync(ct);
        var regression = new List<RunSummaryDto>();
        foreach (var id in recentRuns) regression.Add((await runs.GetAsync(id, ct)).Run);

        // Risk: failing or never-tested features weighted by open defects, P1 tests and recent change.
        var recentlyChanged = await db.BuildFeatureImpacts.AsNoTracking().Where(i => db.ApplicationBuilds.Any(b => b.Id == i.BuildId && b.ProductId == productId && b.CreatedAt >= clock.GetUtcNow().AddDays(-30)))
            .Select(i => i.FeatureId).Distinct().ToListAsync(ct);
        var risks = new List<RiskAreaDto>();
        foreach (var s in statuses)
        {
            var reasons = new List<string>();
            var score = 0;
            var critical = openDefects.Count(d => d.FeatureId == s.FeatureId && d.Severity == DefectSeverity.Critical);
            if (s.TestCases == 0) { reasons.Add("no test cases"); score += 3; }
            else if (s.LatestOutcome is null) { reasons.Add("never executed"); score += 2; }
            if (s.LatestOutcome == "Failed") { reasons.Add("failing tests"); score += 3; }
            if (critical > 0) { reasons.Add($"{critical} critical defect(s)"); score += 4 * critical; }
            else if (s.OpenDefects > 0) { reasons.Add($"{s.OpenDefects} open defect(s)"); score += s.OpenDefects; }
            if (recentlyChanged.Contains(s.FeatureId)) { reasons.Add("changed in the last 30 days"); score += s.LatestOutcome == "Passed" ? 0 : 2; }
            if (byFeature[s.FeatureId].Any(c => c.Priority == "P1") && s.LatestOutcome != "Passed") { reasons.Add("has P1 tests not passing"); score += 2; }
            if (score >= 3) risks.Add(new RiskAreaDto(s.FeatureId, s.SequenceNumber, s.Name, string.Join(", ", reasons), score));
        }

        var byModule = roots.Select(m =>
        {
            var ids = all.Where(f => f.Path.StartsWith(m.Path)).Select(f => f.Id).ToHashSet();
            var d = openDefects.Where(x => x.FeatureId is { } fid && ids.Contains(fid)).ToList();
            return new DefectBucketDto(m.Id.ToString(), $"[{m.SequenceNumber}] {m.Name}", d.Count(x => x.Severity == DefectSeverity.Critical), d.Count(x => x.Severity == DefectSeverity.Major),
                d.Count(x => x.Severity == DefectSeverity.Minor), d.Count(x => x.Severity == DefectSeverity.Trivial), d.Count);
        }).Where(b => b.Total > 0).ToList();
        var unassigned = openDefects.Where(x => x.FeatureId is null).ToList();
        if (unassigned.Count > 0)
            byModule.Add(new DefectBucketDto("none", "(no feature)", unassigned.Count(x => x.Severity == DefectSeverity.Critical), unassigned.Count(x => x.Severity == DefectSeverity.Major),
                unassigned.Count(x => x.Severity == DefectSeverity.Minor), unassigned.Count(x => x.Severity == DefectSeverity.Trivial), unassigned.Count));

        var builds = await db.ApplicationBuilds.AsNoTracking().Where(b => b.ProductId == productId).OrderByDescending(b => b.CreatedAt).Take(20)
            .Select(b => new { b.Id, b.BuildNumber, b.CreatedAt, b.ImpactedFeatureCount, b.TriggeredRunId, b.GateStatus,
                Run = db.ExecutionRuns.Where(r => r.Id == b.TriggeredRunId).Select(r => new { r.Number, r.Passed, r.Failed }).FirstOrDefault() }).ToListAsync(ct);

        var automatedCount = cases.Count(c => c.Automation == AutomationStatus.Automated);
        return new ProductDashboardDto(productId, product.Name, features.Count, statuses.Count(s => s.TestCases > 0), cases.Count, automatedCount, Coverage.Percent(automatedCount, cases.Count),
            modules, statuses, regression, await TrendAsync(caseIds, 30, ct), await AutomationTrendAsync(caseIds, ct),
            risks.OrderByDescending(r => r.Score).ThenBy(r => r.SequenceNumber).Take(25).ToList(), byModule,
            defects.GroupBy(d => d.Status).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            builds.Select(b => new BuildQualityDto(b.Id, b.BuildNumber, b.CreatedAt, b.ImpactedFeatureCount, b.Run is null ? null : ExecutionService.Format(b.Run.Number), b.TriggeredRunId,
                b.Run?.Passed ?? 0, b.Run?.Failed ?? 0, b.GateStatus)).Reverse().ToList(), Coverage.Definitions);
    }

    /// <summary>Weekly test-case count and automated count over 12 weeks, reconstructed from the immutable test-case revisions.</summary>
    private async Task<List<AutomationPoint>> AutomationTrendAsync(IReadOnlySet<Guid> caseIds, CancellationToken ct)
    {
        var revisions = (await db.TestCaseVersions.AsNoTracking().Select(v => new { v.TestCaseId, v.CreatedAt, v.SnapshotJson }).ToListAsync(ct))
            .Where(v => caseIds.Contains(v.TestCaseId))
            .Select(v => (v.TestCaseId, v.CreatedAt, Automated: AutomationPattern().Match(v.SnapshotJson) is { Success: true } m && (m.Groups[1].Value is "2" or "\"Automated\"" or "\"automated\"")))
            .GroupBy(v => v.TestCaseId).ToDictionary(g => g.Key, g => g.OrderBy(v => v.CreatedAt).ToList());
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return Enumerable.Range(0, 12).Select(i => today.AddDays(-7 * (11 - i))).Select(week =>
        {
            var end = new DateTimeOffset(week.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            var states = revisions.Values.Select(l => l.LastOrDefault(v => v.CreatedAt <= end)).Where(v => v.TestCaseId != Guid.Empty).ToList();
            return new AutomationPoint(week, states.Count, states.Count(s => s.Automated));
        }).ToList();
    }

    [GeneratedRegex("\"automationStatus\"\\s*:\\s*(\\d+|\"\\w+\")", RegexOptions.IgnoreCase)]
    private static partial Regex AutomationPattern();

    public async Task<ExecutionDashboardDto> ExecutionAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var offline = now.AddSeconds(-executionOptions.Value.OfflineAfterSeconds);
        var active = await db.ExecutionJobs.AsNoTracking().Where(j => j.Status == JobStatus.Leased || j.Status == JobStatus.Running)
            .Select(j => new { j.Id, j.RunId, RunNumber = j.Run!.Number, RunName = j.Run.Name, j.Framework, Agent = j.Agent!.Name, j.AgentId, j.Status, j.StartedAt, j.LeasedAt, j.TestIdentifiersJson })
            .ToListAsync(ct);
        var running = active.Select(j => new RunningJobDto(j.Id, j.RunId, ExecutionService.Format(j.RunNumber), j.RunName, j.Framework, j.Agent, j.Status, j.StartedAt ?? j.LeasedAt,
            System.Text.Json.JsonSerializer.Deserialize<List<string>>(j.TestIdentifiersJson)?.Count ?? 0)).OrderBy(j => j.StartedAt).ToList();
        var queue = (await db.ExecutionJobs.AsNoTracking().Where(j => j.Status == JobStatus.Queued).Select(j => new { j.Framework, j.RequiresDesktop, j.CreatedAt }).ToListAsync(ct))
            .GroupBy(j => (j.Framework, j.RequiresDesktop)).Select(g => new QueueRowDto(g.Key.Framework, g.Key.RequiresDesktop, g.Count(), g.Min(j => j.CreatedAt))).OrderByDescending(q => q.Queued).ToList();

        var since24 = now.AddHours(-24);
        var since7 = now.AddDays(-7);
        var jobs7 = await db.ExecutionJobs.AsNoTracking().Where(j => j.CreatedAt >= since7 || j.CompletedAt >= since7)
            .Select(j => new { j.AgentId, j.Framework, j.Status, j.Attempt, j.StartedAt, j.LeasedAt, j.CompletedAt }).ToListAsync(ct);
        // Busy time in the last 24 hours: the overlap of each job's [start, end] with that window.
        double Busy(Guid agentId) => jobs7.Where(j => j.AgentId == agentId && (j.StartedAt ?? j.LeasedAt) is not null).Sum(j =>
        {
            var start = (j.StartedAt ?? j.LeasedAt)!.Value;
            var end = j.CompletedAt ?? now;
            if (start < since24) start = since24;
            return end > start ? (end - start).TotalHours : 0;
        });

        var agents = await db.ExecutionAgents.AsNoTracking().Where(a => a.IsEnabled).OrderBy(a => a.Name).ToListAsync(ct);
        var agentRows = agents.Select(a =>
        {
            var busy = Busy(a.Id);
            return new AgentUseDto(a.Id, a.Name, a.Kind, a.LastHeartbeatAt >= offline, a.InMaintenance, a.Slots, active.Count(j => j.AgentId == a.Id), Math.Round(busy, 2),
                Coverage.Percent((int)Math.Round(busy * 100), Math.Max(1, a.Slots) * 24 * 100));
        }).ToList();
        var online = agentRows.Where(a => a.IsOnline && !a.InMaintenance).ToList();

        var failedRunIds = await db.ExecutionRuns.AsNoTracking().Where(r => r.CompletedAt >= since7 && (r.Failed > 0 || r.InfrastructureErrors > 0 || r.Blocked > 0))
            .OrderByDescending(r => r.Number).Select(r => r.Id).Take(10).ToListAsync(ct);
        var failedRuns = new List<RunSummaryDto>();
        foreach (var id in failedRunIds) failedRuns.Add((await runs.GetAsync(id, ct)).Run);

        var durations = jobs7.Where(j => j.Status == JobStatus.Completed && j.StartedAt is not null && j.CompletedAt is not null).GroupBy(j => j.Framework)
            .Select(g => new DurationRowDto(g.Key, g.Count(), Math.Round(g.Average(j => (j.CompletedAt!.Value - j.StartedAt!.Value).TotalSeconds), 1), Math.Round(g.Max(j => (j.CompletedAt!.Value - j.StartedAt!.Value).TotalSeconds), 1)))
            .OrderByDescending(d => d.Jobs).ToList();

        var tests24 = await db.TestExecutions.AsNoTracking().Where(t => t.CompletedAt >= since24 && t.Outcome != "Pending").Select(t => new { t.CompletedAt, t.Outcome }).ToListAsync(ct);
        var hourStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var throughput = Enumerable.Range(0, 24).Select(i => hourStart.AddHours(i - 23)).Select(h =>
        {
            var inHour = tests24.Where(t => t.CompletedAt >= h && t.CompletedAt < h.AddHours(1)).ToList();
            return new ThroughputPoint(h, inHour.Count, inHour.Count(t => t.Outcome == "Failed"));
        }).ToList();

        var infra = await (from t in db.TestExecutions.AsNoTracking().Where(t => t.CompletedAt >= since7 && t.Outcome == "InfrastructureError")
                           join r in db.ExecutionRuns on t.RunId equals r.Id
                           select new { r.EnvironmentId, t.RunId }).ToListAsync(ct);
        var envs = await db.TestEnvironments.AsNoTracking().ToDictionaryAsync(e => e.Id, e => new { e.Name, Health = e.Health.ToString() }, ct);
        var envFailures = infra.GroupBy(x => x.EnvironmentId).Select(g => new EnvironmentFailureDto(g.Key, g.Key is { } e && envs.TryGetValue(e, out var v) ? v.Name : "(no environment)",
                g.Count(), g.Select(x => x.RunId).Distinct().Count(), g.Key is { } e2 && envs.TryGetValue(e2, out var v2) ? v2.Health : "-"))
            .Concat(envs.Where(e => e.Value.Health == "Unavailable" && !infra.Any(i => i.EnvironmentId == e.Key)).Select(e => new EnvironmentFailureDto(e.Key, e.Value.Name, 0, 0, e.Value.Health)))
            .OrderByDescending(e => e.InfrastructureErrors).ToList();

        var jobs7Count = jobs7.Count(j => j.Status != JobStatus.Queued);
        var retries = jobs7.Count(j => j.Attempt > 1);
        var totalSlots = online.Sum(a => Math.Max(1, a.Slots));
        return new ExecutionDashboardDto(running, queue, agents.Count, online.Count, online.Count(a => a.ActiveJobs > 0),
            totalSlots == 0 ? null : Coverage.Percent(online.Sum(a => a.ActiveJobs), totalSlots), agentRows, failedRuns, durations, throughput,
            retries, jobs7Count, Coverage.Percent(retries, jobs7Count), envFailures);
    }
}
