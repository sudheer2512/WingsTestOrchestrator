using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Application.Orchestration;
using Wings.Domain.Execution;
using Wings.Domain.Features;
using Wings.Domain.Orchestration;
using Wings.Domain.Quality;
using Wings.Domain.Testing;

namespace Wings.Application.Quality;

public static class Readiness
{
    public const string Ready = "Ready";
    public const string Warning = "Warning";
    public const string Blocked = "Blocked";
}

public static class GateStatuses
{
    public const string Passed = "Passed";
    public const string Failed = "Failed";
    public const string Waived = "Waived";
    /// <summary>A non-blocking gate that is not met.</summary>
    public const string Warning = "Warning";
}

public sealed record ReleaseSummaryDto(Guid Id, Guid ProductId, string ProductName, string VersionLabel, ReleaseStatus Status, DateOnly? TestingStartDate,
    DateOnly? ReleaseDate, bool IsReleased, string? BuildVersion, string Readiness, int GatesPassed, int GatesTotal, long Version);

public sealed record ReleaseFeatureDto(Guid FeatureId, long SequenceNumber, string Name, FeatureChangeKind Kind, string Source, RiskLevel Risk, Guid? ChangeId,
    string? Notes, int TestCases, int Automated, int Executed, int Passed, string Status);

public sealed record RequiredTestDto(Guid TestCaseId, string NumberDisplay, string Name, string Priority, bool IsAutomated, IReadOnlyList<string> Reasons,
    string? Outcome, DateTimeOffset? At, Guid? RunId, string? RunNumber, string? EnvironmentName);

public sealed record WaiverDto(Guid Id, string Justification, string ApprovedBy, DateTimeOffset ApprovedAt);

public sealed record GateResultDto(Guid GateId, string Name, GateType Type, string? Parameter, bool IsBlocking, bool IsReleaseSpecific, string Status, string Detail,
    double? Measured, int? Numerator, int? Denominator, string Definition, WaiverDto? Waiver);

public sealed record EnvironmentCoverageDto(Guid? EnvironmentId, string Name, int Executed, int Passed, int Failed, DateTimeOffset? LastRunAt);

public sealed record ReleaseMetricsDto(int FeaturesAdded, int FeaturesModified, int FeaturesDeprecated, int TestsRequired, int TestsCompleted, int TestsPassed,
    int TestsFailed, int FailedCritical, int OpenCriticalDefects, int RegressionTotal, int RegressionCompleted, double? PassRate);

public sealed record ApprovalDto(Guid Id, ReleaseDecision Decision, string Comment, string ApprovedBy, DateTimeOffset ApprovedAt, string ReportSha256);

public sealed record ReleaseWorkspaceDto(ReleaseSummaryDto Release, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, string? KnownRisks, ReleaseMetricsDto Metrics,
    IReadOnlyList<ReleaseFeatureDto> Features, IReadOnlyList<RequiredTestDto> RequiredTests, IReadOnlyList<GateResultDto> Gates,
    IReadOnlyList<DefectSummaryDto> OpenCriticalDefects, IReadOnlyList<EnvironmentCoverageDto> Environments, IReadOnlyList<string> Risks,
    IReadOnlyList<PlanSummaryDto> Plans, IReadOnlyList<RunSummaryDto> Runs, IReadOnlyList<ApprovalDto> Approvals, IReadOnlyList<Guid> FailedCriticalTestCaseIds);

public sealed record ReleaseUpdate(ReleaseStatus Status, DateOnly? TestingStartDate, DateOnly? ReleaseDate, string? BuildVersion, string? KnownRisks, long? Version = null);
public sealed record ReleaseChangeUpsert(Guid FeatureId, FeatureChangeKind Kind, RiskLevel Risk = RiskLevel.Medium, string? Notes = null);

public sealed record GateDto(Guid Id, Guid ProductId, Guid? ReleaseId, string Name, GateType Type, string? Parameter, bool IsBlocking, int Order, bool IsEnabled, string Definition, long Version);
public sealed record GateUpsert(Guid ProductId, Guid? ReleaseId, string Name, GateType Type, string? Parameter, bool IsBlocking = true, int Order = 0, bool IsEnabled = true, long? Version = null);

public sealed record ExecuteReleaseRequest(Guid? EnvironmentId = null, string? BuildVersion = null, bool IncludeManual = true, bool OnlyNotPassed = false, bool IgnoreEnvironmentHealth = false);
public sealed record SignOffRequest(ReleaseDecision Decision, string Comment);

/// <summary>Gate results for CI: whether a release candidate may be promoted.</summary>
public sealed record GateCheckDto(Guid ReleaseId, string Product, string Release, string Readiness, bool Blocked, IReadOnlyList<GateResultDto> Gates);

public interface IReleaseService
{
    Task<IReadOnlyList<ReleaseSummaryDto>> ListAsync(Guid? productId, CancellationToken ct = default);
    Task<ReleaseWorkspaceDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ReleaseWorkspaceDto> UpdateAsync(Guid id, ReleaseUpdate request, CancellationToken ct = default);
    Task<ReleaseWorkspaceDto> AddChangeAsync(Guid id, ReleaseChangeUpsert request, CancellationToken ct = default);
    Task<ReleaseWorkspaceDto> RemoveChangeAsync(Guid id, Guid changeId, CancellationToken ct = default);

    Task<IReadOnlyList<GateDto>> GatesAsync(Guid productId, CancellationToken ct = default);
    Task<GateDto> SaveGateAsync(Guid? id, GateUpsert request, CancellationToken ct = default);
    Task DeleteGateAsync(Guid id, CancellationToken ct = default);
    Task<GateCheckDto> CheckAsync(Guid releaseId, CancellationToken ct = default);
    Task<GateCheckDto> CheckAsync(string productCode, string versionLabel, CancellationToken ct = default);

    Task<ReleaseWorkspaceDto> WaiveAsync(Guid releaseId, Guid gateId, string justification, CancellationToken ct = default);
    Task<ReleaseWorkspaceDto> RevokeWaiverAsync(Guid releaseId, Guid waiverId, CancellationToken ct = default);

    Task<RunSummaryDto> ExecuteAsync(Guid releaseId, ExecuteReleaseRequest request, RunOrigin? origin = null, CancellationToken ct = default);
    Task RequestSignOffAsync(Guid releaseId, CancellationToken ct = default);
    Task<ReleaseWorkspaceDto> SignOffAsync(Guid releaseId, SignOffRequest request, CancellationToken ct = default);
    Task<byte[]> ReportAsync(Guid releaseId, CancellationToken ct = default);
    Task<(Stream Content, string FileName)> ApprovalReportAsync(Guid releaseId, Guid approvalId, CancellationToken ct = default);
    /// <summary>Re-evaluates gates after a release run finished and notifies when the release is blocked.</summary>
    Task<string> EvaluateAndNotifyAsync(Guid releaseId, string trigger, CancellationToken ct = default);
}

public sealed class ReleaseService(IWingsDbContext db, ICurrentUser user, TimeProvider clock, IExecutionService runs, IPlanService plans, IDefectService defects,
    IFileStore files, IPdfRenderer pdf, INotificationPublisher notifications, IProductAccess access, IChangeNotifier notifier) : IReleaseService
{
    private static readonly string[] Executed = ["Passed", "Failed", "Blocked"];

    // ---- listing ----

    public async Task<IReadOnlyList<ReleaseSummaryDto>> ListAsync(Guid? productId, CancellationToken ct = default)
    {
        var q = db.ProductVersions.AsNoTracking().Include(v => v.Product).Where(v => v.Product != null);
        if (productId is { } p) q = q.Where(v => v.ProductId == p);
        if (await access.VisibleProductIdsAsync(ct) is { } visible) q = q.Where(v => visible.Contains(v.ProductId));
        var releases = await q.ToListAsync(ct);
        var result = new List<ReleaseSummaryDto>();
        foreach (var r in releases.OrderBy(r => r.Product!.Name).ThenByDescending(r => r.ReleaseDate ?? DateOnly.MaxValue).ThenByDescending(r => r.CreatedAt))
        {
            var (readiness, passed, total) = r.IsReleased ? (Readiness.Ready, 0, 0) : Summarize((await ComputeAsync(r, ct)).Gates);
            result.Add(Summary(r, readiness, passed, total));
        }
        return result;
    }

    private static ReleaseSummaryDto Summary(ProductVersion r, string readiness, int passed, int total)
        => new(r.Id, r.ProductId, r.Product?.Name ?? "", r.VersionLabel, r.Status, r.TestingStartDate, r.ReleaseDate, r.IsReleased, r.BuildVersion, readiness, passed, total, r.Version);

    private static (string Readiness, int Passed, int Total) Summarize(IReadOnlyList<GateResultDto> gates)
    {
        var readiness = gates.Any(g => g.Status == GateStatuses.Failed) ? Readiness.Blocked
            : gates.Any(g => g.Status is GateStatuses.Warning or GateStatuses.Waived) ? Readiness.Warning : Readiness.Ready;
        return (readiness, gates.Count(g => g.Status == GateStatuses.Passed), gates.Count);
    }

    public async Task<ReleaseWorkspaceDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var release = await LoadAsync(id, ct);
        await access.EnsureCanViewAsync(release.ProductId, ct);
        var c = await ComputeAsync(release, ct);
        var (readiness, passed, total) = Summarize(c.Gates);

        var planIds = await db.TestPlans.Where(p => p.TargetReleaseId == id).Select(p => p.Id).ToListAsync(ct);
        var allPlans = await plans.ListAsync(false, ct);
        var runIds = await db.ExecutionRuns.Where(r => r.ReleaseId == id || (r.PlanId != null && planIds.Contains(r.PlanId.Value))).OrderByDescending(r => r.Number).Select(r => r.Id).Take(20).ToListAsync(ct);
        var runList = new List<RunSummaryDto>();
        foreach (var rid in runIds) runList.Add((await runs.GetAsync(rid, ct)).Run);
        var openCritical = c.OpenCriticalDefectIds.Count == 0 ? [] : (await defects.ListAsync(new DefectFilter(ProductId: release.ProductId, Severity: DefectSeverity.Critical, OpenOnly: true, PageSize: 200), ct)).Items;
        var approvals = await db.ReleaseApprovals.AsNoTracking().Where(a => a.ReleaseId == id).OrderByDescending(a => a.ApprovedAt)
            .Select(a => new ApprovalDto(a.Id, a.Decision, a.Comment, a.ApprovedBy, a.ApprovedAt, a.ReportSha256)).ToListAsync(ct);

        return new ReleaseWorkspaceDto(Summary(release, readiness, passed, total), c.WindowStart, c.WindowEnd, release.KnownRisks, c.Metrics, c.Features, c.Required, c.Gates,
            openCritical, c.Environments, c.Risks, allPlans.Where(p => planIds.Contains(p.Id)).ToList(), runList, approvals, c.FailedCritical);
    }

    // ---- computation ----

    private sealed record Computation(DateTimeOffset WindowStart, DateTimeOffset WindowEnd, List<ReleaseFeatureDto> Features, List<RequiredTestDto> Required,
        List<GateResultDto> Gates, ReleaseMetricsDto Metrics, List<EnvironmentCoverageDto> Environments, List<string> Risks, List<Guid> OpenCriticalDefectIds, List<Guid> FailedCritical);

    private sealed record Latest(Guid TestCaseId, string Outcome, DateTimeOffset At, Guid RunId, long RunNumber, Guid? EnvironmentId);

    private sealed record CaseInfo(Guid Id, long Number, string Name, string Priority, AutomationStatus Automation, IReadOnlyList<Guid> FeatureIds, IReadOnlyList<string> Tags);

    private async Task<(DateTimeOffset Start, DateTimeOffset End)> WindowAsync(ProductVersion r, CancellationToken ct)
    {
        DateTimeOffset start;
        if (r.TestingStartDate is { } s) start = new DateTimeOffset(s.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        else
        {
            var previous = await db.ProductVersions.AsNoTracking().Where(v => v.ProductId == r.ProductId && v.Id != r.Id && v.ReleaseDate != null
                    && (r.ReleaseDate == null || v.ReleaseDate < r.ReleaseDate)).OrderByDescending(v => v.ReleaseDate).Select(v => v.ReleaseDate).FirstOrDefaultAsync(ct);
            start = previous is { } p ? new DateTimeOffset(p.ToDateTime(TimeOnly.MinValue).AddDays(1), TimeSpan.Zero) : r.CreatedAt;
        }
        var end = r.IsReleased && r.ReleaseDate is { } rd ? new DateTimeOffset(rd.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero) : clock.GetUtcNow();
        return (start > end ? end : start, end);
    }

    private async Task<Computation> ComputeAsync(ProductVersion release, CancellationToken ct)
    {
        var (start, end) = await WindowAsync(release, ct);
        var productId = release.ProductId;

        // ---- scope: features added, modified, deprecated ----
        var changes = await db.ReleaseFeatureChanges.AsNoTracking().Where(c => c.ReleaseId == release.Id).ToListAsync(ct);
        var introduced = await db.Features.AsNoTracking().Where(f => f.IntroducedInVersionId == release.Id).Select(f => f.Id).ToListAsync(ct);
        var deprecatedInWindow = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == nameof(Feature) && a.Action == "Deprecated" && a.Timestamp >= start && a.Timestamp <= end)
            .Select(a => a.EntityId).Distinct().ToListAsync(ct);
        var scopeRows = new Dictionary<(Guid, FeatureChangeKind), (string Source, RiskLevel Risk, Guid? ChangeId, string? Notes)>();
        foreach (var f in introduced) scopeRows[(f, FeatureChangeKind.Added)] = ("Introduced", RiskLevel.Medium, null, null);
        foreach (var f in deprecatedInWindow) scopeRows[(f, FeatureChangeKind.Deprecated)] = ("Deprecated", RiskLevel.Low, null, null);
        foreach (var c in changes) scopeRows[(c.FeatureId, c.Kind)] = (c.Source.ToString(), c.Risk, c.Id, c.Notes);
        // A feature added in this release is not also "modified".
        foreach (var key in scopeRows.Keys.Where(k => k.Item2 == FeatureChangeKind.Modified && scopeRows.ContainsKey((k.Item1, FeatureChangeKind.Added))).ToList()) scopeRows.Remove(key);

        var scopeFeatureIds = scopeRows.Keys.Select(k => k.Item1).Distinct().ToList();
        var features = await db.Features.AsNoTracking().Where(f => scopeFeatureIds.Contains(f.Id) && f.ProductId == productId).ToDictionaryAsync(f => f.Id, ct);

        // Test cases of a changed feature include those of its sub-features.
        var productFeatures = await db.Features.AsNoTracking().Where(f => f.ProductId == productId).Select(f => new { f.Id, f.Path }).ToListAsync(ct);
        var descendants = features.Values.ToDictionary(f => f.Id, f => productFeatures.Where(x => x.Path.StartsWith(f.Path)).Select(x => x.Id).ToHashSet());

        // ---- test cases of the product ----
        var cases = (await db.TestCases.AsNoTracking()
                .Where(t => !t.IsSharedSteps && !t.IsTemplate && t.ReviewStatus != ReviewStatus.Obsolete && t.FeatureMappings.Any(m => m.Feature!.ProductId == productId))
                .Select(t => new { t.Id, t.Number, t.Name, t.Priority, t.AutomationStatus, Features = t.FeatureMappings.Select(m => m.FeatureId).ToList(), Tags = t.Tags.Select(x => x.Tag).ToList() })
                .ToListAsync(ct))
            .ToDictionary(t => t.Id, t => new CaseInfo(t.Id, t.Number, t.Name, t.Priority, t.AutomationStatus, t.Features, t.Tags));

        // ---- required tests and why ----
        var reasons = new Dictionary<Guid, List<string>>();
        void Require(Guid tc, string reason) { if (!reasons.TryGetValue(tc, out var l)) reasons[tc] = l = []; if (!l.Contains(reason)) l.Add(reason); }
        foreach (var ((fid, kind), _) in scopeRows.Where(r => r.Key.Item2 != FeatureChangeKind.Deprecated))
        {
            if (!features.TryGetValue(fid, out var f)) continue;
            foreach (var tc in cases.Values.Where(t => t.FeatureIds.Any(descendants[fid].Contains)))
                Require(tc.Id, $"{(kind == FeatureChangeKind.Added ? "Added" : "Modified")} feature [{f.SequenceNumber}] {f.Name}");
        }
        var gates = await db.QualityGates.AsNoTracking().Where(g => g.ProductId == productId && g.IsEnabled && (g.ReleaseId == null || g.ReleaseId == release.Id))
            .OrderBy(g => g.Order).ThenBy(g => g.Name).ToListAsync(ct);
        var suiteMembers = new Dictionary<Guid, (string Name, List<Guid> Cases)>();
        foreach (var g in gates.Where(g => g.Type == GateType.SuitePassed && Guid.TryParse(g.Parameter, out _)))
        {
            var sid = Guid.Parse(g.Parameter!);
            var suite = await db.TestSuites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct);
            var members = await db.TestSuiteItems.Where(i => i.SuiteId == sid).Select(i => i.TestCaseId).ToListAsync(ct);
            suiteMembers[sid] = (suite?.Name ?? "(deleted suite)", members);
            foreach (var tc in members) Require(tc, $"Gate \"{g.Name}\"");
        }
        foreach (var g in gates.Where(g => g.Type == GateType.TaggedTestsPassed && g.Parameter.Clean() is not null))
            foreach (var tc in cases.Values.Where(t => t.Tags.Contains(g.Parameter!.Trim().ToLowerInvariant())))
                Require(tc.Id, $"Gate \"{g.Name}\"");
        var planCases = await (from p in db.TestPlans.Where(p => p.TargetReleaseId == release.Id)
                               from s in p.Suites
                               join i in db.TestSuiteItems on s.SuiteId equals i.SuiteId
                               select new { i.TestCaseId, p.Number }).ToListAsync(ct);
        foreach (var pc in planCases) Require(pc.TestCaseId, $"Plan {SequenceFormat.Format(pc.Number, 5, "PLAN-")}");
        var regressionSuiteCases = await (from s in db.TestSuites.Where(s => s.ProductId == productId && s.Tags != null && EF.Functions.Like(s.Tags, "%regression%"))
                                          join i in db.TestSuiteItems on s.Id equals i.SuiteId
                                          select i.TestCaseId).Distinct().ToListAsync(ct);
        var regression = planCases.Select(p => p.TestCaseId).Concat(regressionSuiteCases).ToHashSet();

        // Suite and plan members may belong to other products; describe them too.
        var missing = reasons.Keys.Where(k => !cases.ContainsKey(k)).ToList();
        if (missing.Count > 0)
            foreach (var t in await db.TestCases.AsNoTracking().Where(t => missing.Contains(t.Id))
                         .Select(t => new { t.Id, t.Number, t.Name, t.Priority, t.AutomationStatus, Tags = t.Tags.Select(x => x.Tag).ToList() }).ToListAsync(ct))
                cases[t.Id] = new CaseInfo(t.Id, t.Number, t.Name, t.Priority, t.AutomationStatus, [], t.Tags);
        foreach (var gone in reasons.Keys.Where(k => !cases.ContainsKey(k)).ToList()) reasons.Remove(gone); // deleted test cases

        // ---- latest result of every relevant test case in the window ----
        var relevant = cases.Keys.ToList();
        var executions = await (from t in db.TestExecutions.AsNoTracking()
                                where t.TestCaseId != null && relevant.Contains(t.TestCaseId.Value) && !t.IsSuperseded && t.CompletedAt >= start && t.CompletedAt <= end
                                      && t.Outcome != "Pending"
                                join r in db.ExecutionRuns on t.RunId equals r.Id
                                select new { TestCaseId = t.TestCaseId!.Value, t.Outcome, t.CompletedAt, t.RunId, r.Number, r.EnvironmentId }).ToListAsync(ct);
        var latest = executions.GroupBy(e => e.TestCaseId).ToDictionary(g => g.Key, g =>
        {
            var e = g.OrderByDescending(x => x.CompletedAt).First();
            return new Latest(e.TestCaseId, e.Outcome, e.CompletedAt!.Value, e.RunId, e.Number, e.EnvironmentId);
        });
        var envNames = await db.TestEnvironments.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Name, ct);
        string? Outcome(Guid tc) => latest.TryGetValue(tc, out var l) ? l.Outcome : null;

        // ---- feature rows ----
        var featureRows = new List<ReleaseFeatureDto>();
        foreach (var ((fid, kind), info) in scopeRows)
        {
            if (!features.TryGetValue(fid, out var f)) continue;
            var linked = kind == FeatureChangeKind.Deprecated ? [] : cases.Values.Where(t => t.FeatureIds.Any(descendants[fid].Contains)).ToList();
            var outcomes = linked.Select(t => Outcome(t.Id)).ToList();
            var status = kind == FeatureChangeKind.Deprecated ? "Deprecated"
                : linked.Count == 0 ? "NoTests"
                : outcomes.Any(o => o is "Failed" or "Blocked") ? "Failed"
                : outcomes.Any(o => o == "Passed") ? "Passed" : "NotRun";
            featureRows.Add(new ReleaseFeatureDto(fid, f.SequenceNumber, f.Name, kind, info.Source, info.Risk, info.ChangeId, info.Notes, linked.Count,
                linked.Count(t => t.Automation == AutomationStatus.Automated), outcomes.Count(o => o is not null && Executed.Contains(o)), outcomes.Count(o => o == "Passed"), status));
        }
        featureRows = featureRows.OrderBy(r => r.Kind).ThenBy(r => r.SequenceNumber).ToList();

        var required = reasons.Select(kv =>
        {
            var t = cases[kv.Key];
            latest.TryGetValue(t.Id, out var l);
            return new RequiredTestDto(t.Id, SequenceFormat.Format(t.Number, 5, "TC-"), t.Name, t.Priority, t.Automation == AutomationStatus.Automated, kv.Value,
                l?.Outcome, l?.At, l?.RunId, l is null ? null : ExecutionService.Format(l.RunNumber), l?.EnvironmentId is { } e ? envNames.GetValueOrDefault(e) : null);
        }).OrderBy(t => t.Priority).ThenBy(t => t.NumberDisplay).ToList();

        // ---- defects ----
        var unresolved = await db.Defects.AsNoTracking().Where(d => d.ProductId == productId && DefectWorkflow.Unresolved.Contains(d.Status))
            .Select(d => new { d.Id, d.Severity }).ToListAsync(ct);

        // ---- gate evaluation ----
        var waivers = await db.GateWaivers.AsNoTracking().Where(w => w.ReleaseId == release.Id && w.RevokedAt == null).ToListAsync(ct);
        var results = new List<GateResultDto>();
        foreach (var g in gates)
        {
            var (passed, detail, measured, num, den) = Evaluate(g);
            var waiver = passed ? null : waivers.Where(w => w.GateId == g.Id).OrderByDescending(w => w.ApprovedAt).FirstOrDefault();
            var status = passed ? GateStatuses.Passed : waiver is not null ? GateStatuses.Waived : g.IsBlocking ? GateStatuses.Failed : GateStatuses.Warning;
            results.Add(new GateResultDto(g.Id, g.Name, g.Type, g.Parameter, g.IsBlocking, g.ReleaseId is not null, status, detail, measured, num, den, Definition(g.Type, g.Parameter, suiteMembers),
                waiver is null ? null : new WaiverDto(waiver.Id, waiver.Justification, waiver.ApprovedBy, waiver.ApprovedAt)));
        }

        (bool, string, double?, int?, int?) AllPassed(IReadOnlyCollection<Guid> ids, string what)
        {
            if (ids.Count == 0) return (false, $"No test cases in {what}.", null, 0, 0);
            var outcomes = ids.Select(Outcome).ToList();
            var ok = outcomes.Count(o => o == "Passed");
            var failed = outcomes.Count(o => o is "Failed" or "Blocked");
            var notRun = ids.Count - ok - failed;
            return (ok == ids.Count, $"{ok}/{ids.Count} passed" + (failed > 0 ? $", {failed} failed" : "") + (notRun > 0 ? $", {notRun} not executed" : ""), 100.0 * ok / ids.Count, ok, ids.Count);
        }

        (bool, string, double?, int?, int?) Evaluate(QualityGate g)
        {
            switch (g.Type)
            {
                case GateType.SuitePassed:
                    if (!Guid.TryParse(g.Parameter, out var sid) || !suiteMembers.TryGetValue(sid, out var suite)) return (false, "The gate's suite is not set or no longer exists.", null, null, null);
                    return AllPassed(suite.Cases.Where(cases.ContainsKey).ToList(), $"suite {suite.Name}");
                case GateType.NoOpenDefects:
                    var threshold = Enum.TryParse<DefectSeverity>(g.Parameter, true, out var s) ? s : DefectSeverity.Critical;
                    var open = unresolved.Count(d => d.Severity <= threshold);
                    return (open == 0, open == 0 ? $"No unresolved {Label(threshold)} defects." : $"{open} unresolved {Label(threshold)} defect(s).", open, open, null);
                case GateType.ChangedFeaturesTested:
                    var minRisk = Enum.TryParse<RiskLevel>(g.Parameter, true, out var risk) ? risk : RiskLevel.Low;
                    var changed = featureRows.Where(f => f.Kind != FeatureChangeKind.Deprecated && f.Risk >= minRisk).ToList();
                    if (changed.Count == 0) return (true, "No changed features in scope.", 100, 0, 0);
                    var tested = changed.Count(f => f.Status == "Passed");
                    var untested = changed.Where(f => f.Status != "Passed").Select(f => $"[{f.SequenceNumber}] {f.Name} ({f.Status})").Take(5);
                    return (tested == changed.Count, $"{tested}/{changed.Count} tested" + (tested < changed.Count ? $"; not yet: {string.Join(", ", untested)}" : ""), 100.0 * tested / changed.Count, tested, changed.Count);
                case GateType.MinAutomationCoverage:
                    var min = Threshold(g.Parameter, 70);
                    var productCases = cases.Values.Where(t => t.FeatureIds.Count > 0).ToList();
                    if (productCases.Count == 0) return (false, "The product has no test cases.", 0, 0, 0);
                    var automated = productCases.Count(t => t.Automation == AutomationStatus.Automated);
                    var pct = 100.0 * automated / productCases.Count;
                    return (pct >= min, $"{pct:0.#}% automated ({automated}/{productCases.Count}); minimum {min:0.#}%.", pct, automated, productCases.Count);
                case GateType.TaggedTestsPassed:
                    var tag = g.Parameter.Clean()?.ToLowerInvariant();
                    if (tag is null) return (false, "The gate's tag is not set.", null, null, null);
                    return AllPassed(cases.Values.Where(t => t.Tags.Contains(tag)).Select(t => t.Id).ToList(), $"tag \"{tag}\"");
                case GateType.MinPassRate:
                    var minRate = Threshold(g.Parameter, 95);
                    var done = required.Where(t => t.Outcome is not null && Executed.Contains(t.Outcome)).ToList();
                    if (done.Count == 0) return (false, "No required test has been executed yet.", null, 0, 0);
                    var ok = done.Count(t => t.Outcome == "Passed");
                    var rate = 100.0 * ok / done.Count;
                    return (rate >= minRate, $"{rate:0.#}% pass rate ({ok}/{done.Count} executed); minimum {minRate:0.#}%.", rate, ok, done.Count);
                default:
                    return (false, "Unknown gate type.", null, null, null);
            }
        }

        // ---- metrics, environments and risks ----
        var completed = required.Count(t => t.Outcome is not null && Executed.Contains(t.Outcome));
        var passedCount = required.Count(t => t.Outcome == "Passed");
        var failedCount = required.Count(t => t.Outcome is "Failed" or "Blocked");
        var failedCritical = required.Where(t => t.Priority == "P1" && t.Outcome is "Failed" or "Blocked").Select(t => t.TestCaseId).ToList();
        var openCritical = unresolved.Where(d => d.Severity == DefectSeverity.Critical).Select(d => d.Id).ToList();
        var metrics = new ReleaseMetricsDto(featureRows.Count(f => f.Kind == FeatureChangeKind.Added), featureRows.Count(f => f.Kind == FeatureChangeKind.Modified),
            featureRows.Count(f => f.Kind == FeatureChangeKind.Deprecated), required.Count, completed, passedCount, failedCount, failedCritical.Count, openCritical.Count,
            regression.Count, regression.Count(tc => Outcome(tc) is { } o && Executed.Contains(o)), completed == 0 ? null : Math.Round(100.0 * passedCount / completed, 1));

        var requiredIds = reasons.Keys.ToHashSet();
        var environments = executions.Where(e => requiredIds.Contains(e.TestCaseId)).GroupBy(e => e.EnvironmentId)
            .Select(g =>
            {
                var perCase = g.GroupBy(x => x.TestCaseId).Select(x => x.OrderByDescending(y => y.CompletedAt).First()).ToList();
                return new EnvironmentCoverageDto(g.Key, g.Key is { } e ? envNames.GetValueOrDefault(e, "(deleted)") : "(no environment)", perCase.Count(x => Executed.Contains(x.Outcome)),
                    perCase.Count(x => x.Outcome == "Passed"), perCase.Count(x => x.Outcome is "Failed" or "Blocked"), g.Max(x => x.CompletedAt));
            }).OrderByDescending(e => e.Executed).ToList();

        var risks = new List<string>();
        if (gates.Count == 0) risks.Add("No quality gates are configured for this product.");
        foreach (var f in featureRows.Where(f => f.Status == "NoTests")) risks.Add($"Changed feature [{f.SequenceNumber}] {f.Name} has no test cases.");
        foreach (var f in featureRows.Where(f => f.Status == "NotRun" && f.Risk >= RiskLevel.High)) risks.Add($"{f.Risk}-risk feature [{f.SequenceNumber}] {f.Name} has not been tested in this release.");
        if (failedCritical.Count > 0) risks.Add($"{failedCritical.Count} critical (P1) test(s) are failing.");
        if (openCritical.Count > 0) risks.Add($"{openCritical.Count} critical defect(s) are unresolved.");
        var notExecuted = required.Count - completed;
        if (notExecuted > 0) risks.Add($"{notExecuted} of {required.Count} required test(s) have not been executed in the release window.");
        if (required.Count > 0 && environments.Count == 1) risks.Add($"Required tests were executed in a single environment ({environments[0].Name}).");

        return new Computation(start, end, featureRows, required, results, metrics, environments, risks, openCritical, failedCritical);
    }

    private static string Label(DefectSeverity s) => s == DefectSeverity.Critical ? "critical" : $"{s.ToString().ToLowerInvariant()}-or-worse";

    private static double Threshold(string? parameter, double fallback)
        => double.TryParse(parameter?.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 100) : fallback;

    /// <summary>Plain-language definition of each gate, including its denominator.</summary>
    public static string Definition(GateType type, string? parameter, IReadOnlyDictionary<Guid, (string Name, List<Guid> Cases)>? suites = null) => type switch
    {
        GateType.SuitePassed => $"Every test case in suite {(Guid.TryParse(parameter, out var s) && suites?.TryGetValue(s, out var x) == true ? x.Name : "(set the suite)")} has Passed as its latest result in the release window. Denominator: the suite's test cases.",
        GateType.NoOpenDefects => $"No unresolved defects of the product with severity {(Enum.TryParse<DefectSeverity>(parameter, true, out var sev) ? sev : DefectSeverity.Critical)} or worse. Unresolved = Open, Assigned, In progress, Fixed, Ready for retest or Reopened (Deferred defects are accepted for a later release).",
        GateType.ChangedFeaturesTested => $"Every feature added or modified in the release{(Enum.TryParse<RiskLevel>(parameter, true, out var r) && r > RiskLevel.Low ? $" with change risk {r} or higher" : "")} has at least one linked test case (including sub-features) passed in the window and none failing. Denominator: those features.",
        GateType.MinAutomationCoverage => $"Test-case automation percentage: test cases linked to the product's features (excluding shared steps, templates and obsolete cases) whose automation status is Automated, divided by all of them, is at least {Threshold(parameter, 70):0.#}%. This is not code coverage.",
        GateType.TaggedTestsPassed => $"Every test case of the product tagged \"{parameter}\" has Passed as its latest result in the window. Denominator: the tagged test cases.",
        GateType.MinPassRate => $"Passed ÷ executed (Passed + Failed + Blocked) over the latest results of the release's required tests is at least {Threshold(parameter, 95):0.#}%. Tests not yet executed are not in the denominator.",
        _ => "",
    };

    // ---- release edits ----

    public async Task<ReleaseWorkspaceDto> UpdateAsync(Guid id, ReleaseUpdate r, CancellationToken ct = default)
    {
        var release = await LoadAsync(id, ct);
        await access.EnsureCanEditAsync(release.ProductId, ct);
        if (r.Version is { } v) db.SetOriginalVersion(release, v);
        new Validator().MaxLength(r.BuildVersion, "buildVersion", 100).MaxLength(r.KnownRisks, "knownRisks", 8000)
            .Must(r.Status != ReleaseStatus.SignedOff || release.Status == ReleaseStatus.SignedOff, "status", "Releases are signed off through the sign-off action.")
            .ThrowIfInvalid();
        release.Status = r.Status;
        release.TestingStartDate = r.TestingStartDate;
        release.ReleaseDate = r.ReleaseDate;
        release.IsReleased = r.Status == ReleaseStatus.Released;
        release.BuildVersion = r.BuildVersion.Clean();
        release.KnownRisks = r.KnownRisks.Clean();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReleaseWorkspaceDto> AddChangeAsync(Guid id, ReleaseChangeUpsert r, CancellationToken ct = default)
    {
        var release = await LoadAsync(id, ct);
        await access.EnsureCanEditAsync(release.ProductId, ct);
        if (!await db.Features.AnyAsync(f => f.Id == r.FeatureId && f.ProductId == release.ProductId, ct))
            throw new ValidationException("featureId", "The feature must belong to the release's product.");
        var row = await db.ReleaseFeatureChanges.FirstOrDefaultAsync(c => c.ReleaseId == id && c.FeatureId == r.FeatureId && c.Kind == r.Kind, ct);
        if (row is null) db.ReleaseFeatureChanges.Add(row = new ReleaseFeatureChange { ReleaseId = id, FeatureId = r.FeatureId, Kind = r.Kind });
        row.Source = FeatureChangeSource.Declared;
        row.Risk = r.Risk;
        row.Notes = r.Notes.Clean();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReleaseWorkspaceDto> RemoveChangeAsync(Guid id, Guid changeId, CancellationToken ct = default)
    {
        var release = await LoadAsync(id, ct);
        await access.EnsureCanEditAsync(release.ProductId, ct);
        var row = await db.ReleaseFeatureChanges.FirstOrDefaultAsync(c => c.Id == changeId && c.ReleaseId == id, ct) ?? throw new NotFoundException("Release change", changeId);
        db.ReleaseFeatureChanges.Remove(row);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---- gates ----

    public async Task<IReadOnlyList<GateDto>> GatesAsync(Guid productId, CancellationToken ct = default)
    {
        await access.EnsureCanViewAsync(productId, ct);
        var gates = await db.QualityGates.AsNoTracking().Where(g => g.ProductId == productId).OrderBy(g => g.ReleaseId != null).ThenBy(g => g.Order).ThenBy(g => g.Name).ToListAsync(ct);
        var suiteIds = gates.Where(g => g.Type == GateType.SuitePassed && Guid.TryParse(g.Parameter, out _)).Select(g => Guid.Parse(g.Parameter!)).ToList();
        var suites = await db.TestSuites.AsNoTracking().Where(s => suiteIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => (s.Name, new List<Guid>()), ct);
        return gates.Select(g => new GateDto(g.Id, g.ProductId, g.ReleaseId, g.Name, g.Type, g.Parameter, g.IsBlocking, g.Order, g.IsEnabled, Definition(g.Type, g.Parameter, suites), g.Version)).ToList();
    }

    public async Task<GateDto> SaveGateAsync(Guid? id, GateUpsert r, CancellationToken ct = default)
    {
        await access.EnsureCanEditAsync(r.ProductId, ct);
        var v = new Validator().Required(r.Name, "name", 200).MaxLength(r.Parameter, "parameter", 200);
        switch (r.Type)
        {
            case GateType.SuitePassed:
                v.Must(Guid.TryParse(r.Parameter, out var sid) && await db.TestSuites.AnyAsync(s => s.Id == sid, ct), "parameter", "Choose the suite that must pass.");
                break;
            case GateType.NoOpenDefects:
                v.Must(r.Parameter.Clean() is null || Enum.TryParse<DefectSeverity>(r.Parameter, true, out _), "parameter", "Use a severity: Critical, Major, Minor or Trivial.");
                break;
            case GateType.ChangedFeaturesTested:
                v.Must(r.Parameter.Clean() is null || Enum.TryParse<RiskLevel>(r.Parameter, true, out _), "parameter", "Use a minimum risk: Low, Medium, High or Critical.");
                break;
            case GateType.MinAutomationCoverage or GateType.MinPassRate:
                v.Must(double.TryParse(r.Parameter?.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) && pct is >= 0 and <= 100, "parameter", "Enter a percentage between 0 and 100.");
                break;
            case GateType.TaggedTestsPassed:
                v.Must(r.Parameter.Clean() is not null, "parameter", "Enter the tag (e.g. accounting).");
                break;
        }
        if (r.ReleaseId is { } rid) v.Must(await db.ProductVersions.AnyAsync(x => x.Id == rid && x.ProductId == r.ProductId, ct), "releaseId", "The release must belong to the product.");
        v.ThrowIfInvalid();

        QualityGate g;
        if (id is { } existing)
        {
            g = await db.QualityGates.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Quality gate", existing);
            if (r.Version is { } ver) db.SetOriginalVersion(g, ver);
        }
        else db.QualityGates.Add(g = new QualityGate());
        g.ProductId = r.ProductId;
        g.ReleaseId = r.ReleaseId;
        g.Name = r.Name.Trim();
        g.Type = r.Type;
        g.Parameter = r.Type == GateType.TaggedTestsPassed ? r.Parameter!.Trim().ToLowerInvariant() : r.Parameter.Clean();
        g.IsBlocking = r.IsBlocking;
        g.Order = r.Order;
        g.IsEnabled = r.IsEnabled;
        await db.SaveChangesAsync(ct);
        return (await GatesAsync(r.ProductId, ct)).Single(x => x.Id == g.Id);
    }

    public async Task DeleteGateAsync(Guid id, CancellationToken ct = default)
    {
        var g = await db.QualityGates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Quality gate", id);
        await access.EnsureCanEditAsync(g.ProductId, ct);
        db.QualityGates.Remove(g);
        await db.SaveChangesAsync(ct);
    }

    public async Task<GateCheckDto> CheckAsync(Guid releaseId, CancellationToken ct = default)
    {
        var release = await LoadAsync(releaseId, ct);
        var gates = (await ComputeAsync(release, ct)).Gates;
        var readiness = Summarize(gates).Readiness;
        return new GateCheckDto(release.Id, release.Product!.Code, release.VersionLabel, readiness, readiness == Readiness.Blocked, gates);
    }

    public async Task<GateCheckDto> CheckAsync(string productCode, string versionLabel, CancellationToken ct = default)
    {
        var id = await db.ProductVersions.Where(v => v.Product!.Code == productCode && v.VersionLabel == versionLabel).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct)
                 ?? throw new NotFoundException("Release", $"{productCode} {versionLabel}");
        return await CheckAsync(id, ct);
    }

    // ---- exceptions ----

    public async Task<ReleaseWorkspaceDto> WaiveAsync(Guid releaseId, Guid gateId, string justification, CancellationToken ct = default)
    {
        var release = await LoadAsync(releaseId, ct);
        await access.EnsureCanEditAsync(release.ProductId, ct);
        new Validator().Must(justification.Clean() is { Length: >= 20 }, "justification", "Record a justification of at least 20 characters.").MaxLength(justification, "justification", 4000).ThrowIfInvalid();
        var gate = (await ComputeAsync(release, ct)).Gates.FirstOrDefault(g => g.GateId == gateId) ?? throw new NotFoundException("Quality gate", gateId);
        if (gate.Status is GateStatuses.Passed) throw new ConflictException("The gate is passing; there is nothing to waive.");
        if (gate.Status is GateStatuses.Waived) throw new ConflictException("The gate already has an active exception.");
        db.GateWaivers.Add(new GateWaiver { GateId = gateId, ReleaseId = releaseId, Justification = justification.Trim(), ApprovedBy = user.UserName, ApprovedAt = clock.GetUtcNow() });
        db.PendingAuditAction = "ExceptionApproved";
        db.PendingAuditSummary = $"Exception for gate \"{gate.Name}\" ({gate.Detail})";
        await db.SaveChangesAsync(ct);
        return await GetAsync(releaseId, ct);
    }

    public async Task<ReleaseWorkspaceDto> RevokeWaiverAsync(Guid releaseId, Guid waiverId, CancellationToken ct = default)
    {
        var w = await db.GateWaivers.FirstOrDefaultAsync(x => x.Id == waiverId && x.ReleaseId == releaseId, ct) ?? throw new NotFoundException("Exception", waiverId);
        if (w.RevokedAt is null)
        {
            w.RevokedAt = clock.GetUtcNow();
            db.PendingAuditAction = "ExceptionRevoked";
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(releaseId, ct);
    }

    // ---- execution and sign-off ----

    public async Task<RunSummaryDto> ExecuteAsync(Guid releaseId, ExecuteReleaseRequest r, RunOrigin? origin = null, CancellationToken ct = default)
    {
        var release = await LoadAsync(releaseId, ct);
        var c = await ComputeAsync(release, ct);
        var ids = c.Required.Where(t => !r.OnlyNotPassed || t.Outcome != "Passed").Select(t => t.TestCaseId).ToList();
        if (ids.Count == 0)
            throw new ValidationException("scope", r.OnlyNotPassed ? "Every required test has already passed." : "The release has no required tests: add changed features, plans or gates first.");
        var run = await runs.StartAsync(new StartRunRequest
        {
            Name = $"Release validation: {release.Product!.Name} {release.VersionLabel}", TestCaseIds = ids, EnvironmentId = r.EnvironmentId,
            BuildVersion = r.BuildVersion.Clean() ?? release.BuildVersion, IncludeManual = r.IncludeManual, IgnoreEnvironmentHealth = r.IgnoreEnvironmentHealth,
        }, origin ?? new RunOrigin(user.UserName), ct);
        await db.ExecutionRuns.Where(x => x.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReleaseId, releaseId), ct);
        if (release.Status == ReleaseStatus.Planning)
        {
            release.Status = ReleaseStatus.Testing;
            await db.SaveChangesAsync(ct);
        }
        return run;
    }

    public async Task RequestSignOffAsync(Guid releaseId, CancellationToken ct = default)
    {
        var release = await LoadAsync(releaseId, ct);
        var gates = (await ComputeAsync(release, ct)).Gates;
        var readiness = Summarize(gates).Readiness;
        await notifications.PublishAsync(new NotificationMessage(NotificationEvents.ApprovalRequired, NotificationSeverity.Info,
            $"Sign-off requested: {release.Product!.Name} {release.VersionLabel} ({readiness})",
            $"{user.UserName} asks for release testing sign-off. Gates: {gates.Count(g => g.Status == GateStatuses.Passed)}/{gates.Count} passed.",
            $"/releases/{releaseId}", release.ProductId, new Dictionary<string, string> { ["release"] = release.VersionLabel, ["readiness"] = readiness }), ct);
    }

    public async Task<ReleaseWorkspaceDto> SignOffAsync(Guid releaseId, SignOffRequest r, CancellationToken ct = default)
    {
        var release = await LoadAsync(releaseId, ct);
        await access.EnsureCanEditAsync(release.ProductId, ct);
        new Validator().Required(r.Comment, "comment", 4000).ThrowIfInvalid();
        var workspace = await GetAsync(releaseId, ct);
        if (r.Decision == ReleaseDecision.Approved && workspace.Release.Readiness == Readiness.Blocked)
            throw new ConflictException($"Blocking gates are failing: {string.Join("; ", workspace.Gates.Where(g => g.Status == GateStatuses.Failed).Select(g => $"{g.Name} ({g.Detail})"))}. Fix them or approve an exception first.");

        var now = clock.GetUtcNow();
        var approval = new ReleaseApproval { ReleaseId = releaseId, Decision = r.Decision, Comment = r.Comment.Trim(), ApprovedBy = user.UserName, ApprovedAt = now };
        var bytes = Render(workspace, approval);
        var blob = await files.PutAsync(new MemoryStream(bytes), ct);
        approval.ReportStorageKey = blob.Key;
        approval.ReportSha256 = blob.Sha256;
        approval.GateSummaryJson = JsonSerializer.Serialize(workspace.Gates.Select(g => new { g.Name, g.Status, g.Detail, Waiver = g.Waiver?.Justification }));
        db.ReleaseApprovals.Add(approval);
        if (r.Decision == ReleaseDecision.Approved) release.Status = ReleaseStatus.SignedOff;
        db.PendingAuditAction = r.Decision == ReleaseDecision.Approved ? "SignedOff" : "SignOffRejected";
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("releasesChanged", new { releaseId }, ct);
        return await GetAsync(releaseId, ct);
    }

    public async Task<byte[]> ReportAsync(Guid releaseId, CancellationToken ct = default) => Render(await GetAsync(releaseId, ct), null);

    public async Task<(Stream Content, string FileName)> ApprovalReportAsync(Guid releaseId, Guid approvalId, CancellationToken ct = default)
    {
        var release = await LoadAsync(releaseId, ct);
        await access.EnsureCanViewAsync(release.ProductId, ct);
        var a = await db.ReleaseApprovals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == approvalId && x.ReleaseId == releaseId, ct) ?? throw new NotFoundException("Approval", approvalId);
        return (await files.OpenReadAsync(a.ReportStorageKey, ct), $"{release.Product!.Code}-{release.VersionLabel}-signoff-{a.ApprovedAt:yyyyMMdd-HHmm}.pdf");
    }

    public async Task<string> EvaluateAndNotifyAsync(Guid releaseId, string trigger, CancellationToken ct = default)
    {
        var release = await db.ProductVersions.AsNoTracking().Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == releaseId, ct);
        if (release is null) return Readiness.Ready;
        var gates = (await ComputeAsync(release, ct)).Gates;
        var readiness = Summarize(gates).Readiness;
        var failing = gates.Where(g => g.Status is GateStatuses.Failed or GateStatuses.Warning).ToList();
        if (failing.Count > 0)
            await notifications.PublishAsync(new NotificationMessage(NotificationEvents.GateFailed,
                readiness == Readiness.Blocked ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                $"{release.Product!.Name} {release.VersionLabel}: {failing.Count} quality gate(s) not met ({readiness})",
                $"After {trigger}:\n" + string.Join("\n", failing.Select(g => $"- {g.Name}: {g.Detail}")),
                $"/releases/{releaseId}", release.ProductId, new Dictionary<string, string> { ["release"] = release.VersionLabel, ["readiness"] = readiness }), ct);
        return readiness;
    }

    private byte[] Render(ReleaseWorkspaceDto w, ReleaseApproval? approval)
    {
        var r = w.Release;
        var m = w.Metrics;
        var doc = new PdfDocumentModel
        {
            Title = $"Release testing report: {r.ProductName} {r.VersionLabel}",
            Subtitle = approval is null ? $"Readiness snapshot generated {clock.GetUtcNow():yyyy-MM-dd HH:mm} UTC by {user.UserName} (not signed off)"
                : $"{(approval.Decision == ReleaseDecision.Approved ? "SIGNED OFF" : "SIGN-OFF REJECTED")} by {approval.ApprovedBy} on {approval.ApprovedAt:yyyy-MM-dd HH:mm} UTC",
            Footer = "Wings Test Orchestrator: release testing report",
        };
        doc.Fields([
            ("Product", r.ProductName), ("Release", r.VersionLabel), ("Release candidate build", r.BuildVersion), ("Status", r.Status.ToString()),
            ("Readiness", $"{r.Readiness} ({r.GatesPassed}/{r.GatesTotal} gates passed)"),
            ("Testing window", $"{w.WindowStart:yyyy-MM-dd HH:mm} to {w.WindowEnd:yyyy-MM-dd HH:mm} UTC"),
        ]);
        if (approval is not null) doc.Heading("Decision").Fields([("Decision", approval.Decision.ToString()), ("By", approval.ApprovedBy), ("At", $"{approval.ApprovedAt:u}"), ("Comment", approval.Comment)]);
        doc.Heading("Summary").Fields([
            ("Features added / modified / deprecated", $"{m.FeaturesAdded} / {m.FeaturesModified} / {m.FeaturesDeprecated}"),
            ("Required tests", m.TestsRequired.ToString()), ("Completed (latest result Passed, Failed or Blocked)", m.TestsCompleted.ToString()),
            ("Passed", m.TestsPassed.ToString()), ("Failed or blocked", m.TestsFailed.ToString()),
            ("Pass rate (passed ÷ completed)", m.PassRate is { } p ? $"{p:0.#}%" : "n/a"), ("Failed critical (P1) tests", m.FailedCritical.ToString()),
            ("Unresolved critical defects", m.OpenCriticalDefects.ToString()), ("Regression completion", $"{m.RegressionCompleted}/{m.RegressionTotal}"),
        ]);
        doc.Heading("Quality gates").Table(["Gate", "Blocking", "Status", "Result", "Exception"],
            w.Gates.Select(g => (IReadOnlyList<string?>)[g.Name, g.IsBlocking ? "Yes" : "No", g.Status, g.Detail, g.Waiver is { } x ? $"{x.ApprovedBy}: {x.Justification}" : null]), [2, 0.8, 0.9, 3, 3]);
        if (w.Features.Count > 0)
            doc.Heading("Release scope").Table(["Seq", "Feature", "Change", "Risk", "Tests", "Passed", "Status"],
                w.Features.Select(f => (IReadOnlyList<string?>)[f.SequenceNumber.ToString(), f.Name, $"{f.Kind} ({f.Source})", f.Risk.ToString(), f.TestCases.ToString(), f.Passed.ToString(), f.Status]), [0.6, 3, 1.6, 0.9, 0.7, 0.7, 1]);
        if (w.Environments.Count > 0)
            doc.Heading("Environment coverage").Table(["Environment", "Executed", "Passed", "Failed", "Last run"],
                w.Environments.Select(e => (IReadOnlyList<string?>)[e.Name, e.Executed.ToString(), e.Passed.ToString(), e.Failed.ToString(), e.LastRunAt?.ToString("yyyy-MM-dd HH:mm")]));
        var failing = w.RequiredTests.Where(t => t.Outcome is "Failed" or "Blocked" || t.Outcome is null).ToList();
        if (failing.Count > 0)
            doc.Heading("Required tests not passed").Table(["Test case", "Name", "Priority", "Latest result", "Run"],
                failing.Take(300).Select(t => (IReadOnlyList<string?>)[t.NumberDisplay, t.Name, t.Priority, t.Outcome ?? "Not executed", t.RunNumber]), [1, 4, 0.7, 1.2, 1]);
        if (w.OpenCriticalDefects.Count > 0)
            doc.Heading("Unresolved critical defects").Table(["Defect", "Title", "Status", "Assigned to"],
                w.OpenCriticalDefects.Select(d => (IReadOnlyList<string?>)[d.NumberDisplay, d.Title, d.Status.ToString(), d.AssignedTo]), [1, 4, 1.2, 1.2]);
        var risks = w.Risks.ToList();
        if (w.KnownRisks is { } known) risks.Insert(0, known);
        if (risks.Count > 0) { doc.Heading("Known risks"); foreach (var risk in risks) doc.Paragraph("• " + risk); }
        if (w.Runs.Count > 0)
            doc.Heading("Runs").Table(["Run", "Name", "Status", "Passed", "Failed", "Completed"],
                w.Runs.Select(x => (IReadOnlyList<string?>)[x.NumberDisplay, x.Name, x.Status.ToString(), x.Passed.ToString(), x.Failed.ToString(), x.CompletedAt?.ToString("yyyy-MM-dd HH:mm")]), [1, 4, 1, 0.7, 0.7, 1.4]);
        doc.Heading("Definitions");
        foreach (var g in w.Gates) doc.Paragraph($"{g.Name}: {g.Definition}");
        return pdf.Render(doc);
    }

    private async Task<ProductVersion> LoadAsync(Guid id, CancellationToken ct)
        => await db.ProductVersions.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == id, ct) is { Product: not null } r ? r : throw new NotFoundException("Release", id);
}
