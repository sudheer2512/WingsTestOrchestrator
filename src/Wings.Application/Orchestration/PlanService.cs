using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Domain.Execution;
using Wings.Domain.Orchestration;

namespace Wings.Application.Orchestration;

public sealed record PlanSummaryDto(Guid Id, long Number, string NumberDisplay, string Name, PlanStatus Status, RiskLevel Risk, Guid? ProductId, Guid? TargetReleaseId,
    string? TargetReleaseLabel, Guid? EnvironmentId, string? EnvironmentName, string? BuildVersion, DateOnly? StartDate, DateOnly? EndDate, bool IsTemplate,
    int SuiteCount, int TestCaseCount, int RunCount, long Version);

public sealed record PlanSuiteDto(Guid SuiteId, string NumberDisplay, string Name, int TestCaseCount, int Order);

/// <summary>Latest outcome of each test case across the plan's runs.</summary>
public sealed record PlanCoverageRow(Guid TestCaseId, string NumberDisplay, string Name, string? Outcome, Guid? RunId, string? RunNumber, DateTimeOffset? At, string? ExecutedBy);

public sealed record PlanDetailDto(PlanSummaryDto Summary, string? Objective, string? Scope, string? Owners, string? Testers, string? EntryCriteria, string? ExitCriteria,
    string? DefectAcceptanceRules, string? ApprovedBy, DateTimeOffset? ApprovedAt, string? ApprovalComment, IReadOnlyList<PlanSuiteDto> Suites,
    IReadOnlyList<RunSummaryDto> Runs, IReadOnlyList<PlanCoverageRow> Coverage, IReadOnlyDictionary<string, int> CoverageTotals);

public sealed record PlanUpsert
{
    public string Name { get; init; } = "";
    public string? Objective { get; init; }
    public string? Scope { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? TargetReleaseId { get; init; }
    public string? BuildVersion { get; init; }
    public Guid? EnvironmentId { get; init; }
    public string? Owners { get; init; }
    public string? Testers { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? EntryCriteria { get; init; }
    public string? ExitCriteria { get; init; }
    public RiskLevel Risk { get; init; } = RiskLevel.Medium;
    public string? DefectAcceptanceRules { get; init; }
    public bool IsTemplate { get; init; }
    public IReadOnlyList<Guid> SuiteIds { get; init; } = [];
    public long? Version { get; init; }
}

public sealed record ExecutePlanRequest(Guid? EnvironmentId = null, string? BuildVersion = null, IReadOnlyDictionary<string, string>? Variables = null, int Priority = 50, bool IgnoreEnvironmentHealth = false);

public interface IPlanService
{
    Task<IReadOnlyList<PlanSummaryDto>> ListAsync(bool templates, CancellationToken ct = default);
    Task<PlanDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<PlanDetailDto> CreateAsync(PlanUpsert request, CancellationToken ct = default);
    Task<PlanDetailDto> UpdateAsync(Guid id, PlanUpsert request, CancellationToken ct = default);
    Task<PlanDetailDto> CloneAsync(Guid id, string name, bool asTemplate, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<PlanDetailDto> SetStatusAsync(Guid id, PlanStatus status, string? comment, CancellationToken ct = default);
    Task<RunSummaryDto> ExecuteAsync(Guid id, ExecutePlanRequest request, RunOrigin? origin = null, CancellationToken ct = default);
}

public sealed class PlanService(IWingsDbContext db, ISequenceAllocator sequences, IExecutionService runs, ICurrentUser user, IPermissionService permissions, TimeProvider clock,
    Quality.INotificationPublisher notifications) : IPlanService
{
    public const string ScopeKey = "PLAN";

    public async Task<IReadOnlyList<PlanSummaryDto>> ListAsync(bool templates, CancellationToken ct = default)
        => await Summaries(db.TestPlans.Where(p => p.IsTemplate == templates), ct);

    private async Task<List<PlanSummaryDto>> Summaries(IQueryable<TestPlan> q, CancellationToken ct)
    {
        var rows = await q.AsNoTracking().OrderByDescending(p => p.Number).Select(p => new
        {
            p,
            Release = db.ProductVersions.Where(v => v.Id == p.TargetReleaseId).Select(v => v.VersionLabel).FirstOrDefault(),
            Env = db.TestEnvironments.Where(e => e.Id == p.EnvironmentId).Select(e => e.Name).FirstOrDefault(),
            Suites = p.Suites.Count,
            Cases = db.TestSuiteItems.Where(i => p.Suites.Select(s => s.SuiteId).Contains(i.SuiteId)).Select(i => i.TestCaseId).Distinct().Count(),
            Runs = db.ExecutionRuns.Count(r => r.PlanId == p.Id),
        }).ToListAsync(ct);
        return rows.Select(x => new PlanSummaryDto(x.p.Id, x.p.Number, SequenceFormat.Format(x.p.Number, 5, "PLAN-"), x.p.Name, x.p.Status, x.p.Risk, x.p.ProductId,
            x.p.TargetReleaseId, x.Release, x.p.EnvironmentId, x.Env, x.p.BuildVersion, x.p.StartDate, x.p.EndDate, x.p.IsTemplate, x.Suites, x.Cases, x.Runs, x.p.Version)).ToList();
    }

    public async Task<PlanDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.TestPlans.AsNoTracking().Include(x => x.Suites).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Test plan", id);
        var summary = (await Summaries(db.TestPlans.Where(x => x.Id == id), ct)).Single();
        var suiteIds = p.Suites.Select(s => s.SuiteId).ToList();
        var suites = await db.TestSuites.AsNoTracking().Where(s => suiteIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Number, s.Name, Count = s.Items.Count }).ToListAsync(ct);
        var planRuns = (await runs.ListAsync(new RunFilter(PageSize: 500, PlanId: id), ct)).Items.ToList();

        var caseIds = await db.TestSuiteItems.Where(i => suiteIds.Contains(i.SuiteId)).Select(i => i.TestCaseId).Distinct().ToListAsync(ct);
        var cases = await db.TestCases.AsNoTracking().Where(c => caseIds.Contains(c.Id)).Select(c => new { c.Id, c.Number, c.Name }).ToListAsync(ct);
        var planRunIds = planRuns.Select(r => r.Id).ToList();
        var results = await db.TestExecutions.AsNoTracking()
            .Where(t => planRunIds.Contains(t.RunId) && t.TestCaseId != null && !t.IsSuperseded && t.Outcome != "Pending" && t.CompletedAt != null)
            .Select(t => new { t.TestCaseId, t.Outcome, t.RunId, t.CompletedAt, t.ExecutedBy }).ToListAsync(ct);
        var latest = results.GroupBy(r => r.TestCaseId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CompletedAt).First());
        var runNumbers = planRuns.ToDictionary(r => r.Id, r => r.NumberDisplay);
        var coverage = cases.OrderBy(c => c.Number).Select(c => latest.TryGetValue(c.Id, out var l)
            ? new PlanCoverageRow(c.Id, SequenceFormat.Format(c.Number, 5, "TC-"), c.Name, l.Outcome, l.RunId, runNumbers.GetValueOrDefault(l.RunId), l.CompletedAt, l.ExecutedBy)
            : new PlanCoverageRow(c.Id, SequenceFormat.Format(c.Number, 5, "TC-"), c.Name, null, null, null, null, null)).ToList();
        var totals = coverage.GroupBy(c => c.Outcome ?? "NotExecuted").ToDictionary(g => g.Key, g => g.Count());

        return new PlanDetailDto(summary, p.Objective, p.Scope, p.Owners, p.Testers, p.EntryCriteria, p.ExitCriteria, p.DefectAcceptanceRules, p.ApprovedBy, p.ApprovedAt,
            p.ApprovalComment, p.Suites.OrderBy(s => s.Order).Select(s => suites.FirstOrDefault(x => x.Id == s.SuiteId) is { } x
                ? new PlanSuiteDto(x.Id, SequenceFormat.Format(x.Number, 5, "TS-"), x.Name, x.Count, s.Order) : null).Where(s => s is not null).ToList()!,
            planRuns, coverage, totals);
    }

    public async Task<PlanDetailDto> CreateAsync(PlanUpsert r, CancellationToken ct = default)
    {
        await ValidateAsync(r, ct);
        var id = await db.InTransactionAsync(async () =>
        {
            var plan = new TestPlan { Number = await sequences.NextAsync((await db.SequenceScopes.FirstAsync(s => s.Key == ScopeKey, ct)).Id, ct) };
            Apply(plan, r);
            db.TestPlans.Add(plan);
            await db.SaveChangesAsync(ct);
            return plan.Id;
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<PlanDetailDto> UpdateAsync(Guid id, PlanUpsert r, CancellationToken ct = default)
    {
        await ValidateAsync(r, ct);
        var plan = await db.TestPlans.Include(p => p.Suites).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Test plan", id);
        if (plan.Status == PlanStatus.Approved) throw new ConflictException("Approved plans are locked. Clone the plan to change it.");
        if (r.Version is { } v) db.SetOriginalVersion(plan, v);
        Apply(plan, r);
        db.Touch(plan);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PlanDetailDto> CloneAsync(Guid id, string name, bool asTemplate, CancellationToken ct = default)
    {
        var src = await db.TestPlans.AsNoTracking().Include(p => p.Suites).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Test plan", id);
        return await CreateAsync(new PlanUpsert
        {
            Name = name, Objective = src.Objective, Scope = src.Scope, ProductId = src.ProductId, TargetReleaseId = asTemplate ? null : src.TargetReleaseId,
            BuildVersion = asTemplate ? null : src.BuildVersion, EnvironmentId = src.EnvironmentId, Owners = src.Owners, Testers = src.Testers,
            EntryCriteria = src.EntryCriteria, ExitCriteria = src.ExitCriteria, Risk = src.Risk, DefectAcceptanceRules = src.DefectAcceptanceRules,
            IsTemplate = asTemplate, SuiteIds = src.Suites.OrderBy(s => s.Order).Select(s => s.SuiteId).ToList(),
        }, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var plan = await db.TestPlans.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Test plan", id);
        if (plan.Status == PlanStatus.Approved) throw new ConflictException("Approved plans are kept as release evidence.");
        plan.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Draft → Active → Completed → Approved. Approval needs plans.approve and a different person from the plan's last editor.</summary>
    public async Task<PlanDetailDto> SetStatusAsync(Guid id, PlanStatus status, string? comment, CancellationToken ct = default)
    {
        var plan = await db.TestPlans.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Test plan", id);
        if (plan.IsTemplate) throw new ConflictException("Templates have no lifecycle; clone the template into a plan first.");
        var allowed = (plan.Status, status) switch
        {
            (PlanStatus.Draft, PlanStatus.Active) or (PlanStatus.Active, PlanStatus.Completed) or (PlanStatus.Completed, PlanStatus.Active)
                or (PlanStatus.Active, PlanStatus.Draft) or (PlanStatus.Completed, PlanStatus.Approved) => true,
            _ => false,
        };
        if (!allowed) throw new ConflictException($"A {plan.Status} plan cannot move to {status}.");
        if (status == PlanStatus.Approved)
        {
            if (user.UserId is not { } uid || !(await permissions.GetPermissionsAsync(uid, ct)).Contains(Security.Permissions.PlansApprove))
                throw new ForbiddenException("Approving a plan's completion needs the plans.approve permission.");
            if (string.Equals(plan.ModifiedBy ?? plan.CreatedBy, user.UserName, StringComparison.OrdinalIgnoreCase))
                throw new ForbiddenException("The plan must be approved by someone other than its last editor.");
            plan.ApprovedBy = user.UserName;
            plan.ApprovedAt = clock.GetUtcNow();
            plan.ApprovalComment = comment.Clean();
        }
        plan.Status = status;
        db.PendingAuditAction = $"Plan{status}";
        db.PendingAuditSummary = comment.Clean();
        await db.SaveChangesAsync(ct);
        if (status == PlanStatus.Completed)
            await notifications.PublishAsync(new Quality.NotificationMessage(Quality.NotificationEvents.ApprovalRequired, Quality.NotificationSeverity.Info,
                $"Plan completion awaiting approval: {SequenceFormat.Format(plan.Number, 5, "PLAN-")} {plan.Name}", $"Marked completed by {user.UserName}.", $"/plans/{id}", plan.ProductId), ct);
        return await GetAsync(id, ct);
    }

    public async Task<RunSummaryDto> ExecuteAsync(Guid id, ExecutePlanRequest request, RunOrigin? origin = null, CancellationToken ct = default)
    {
        var plan = await db.TestPlans.AsNoTracking().Include(p => p.Suites).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Test plan", id);
        if (plan.IsTemplate) throw new ConflictException("Templates cannot be executed; clone the template into a plan.");
        if (plan.Suites.Count == 0) throw new ValidationException("suites", "Add at least one suite to the plan.");
        if (plan.Status == PlanStatus.Draft)
        {
            await db.TestPlans.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PlanStatus.Active), ct);
        }
        var env = request.EnvironmentId ?? plan.EnvironmentId;
        return await runs.StartAsync(new StartRunRequest
        {
            Name = $"{SequenceFormat.Format(plan.Number, 5, "PLAN-")} {plan.Name}", SuiteIds = plan.Suites.OrderBy(s => s.Order).Select(s => s.SuiteId).ToList(),
            EnvironmentId = env, BuildVersion = request.BuildVersion ?? plan.BuildVersion, IncludeManual = true, Priority = request.Priority,
            Variables = request.Variables ?? new Dictionary<string, string>(), IgnoreEnvironmentHealth = request.IgnoreEnvironmentHealth,
        }, (origin ?? new RunOrigin(user.UserName)) with { PlanId = id }, ct);
    }

    private async Task ValidateAsync(PlanUpsert r, CancellationToken ct)
    {
        var v = new Validator().Required(r.Name, "name", 300).Must(r.StartDate is null || r.EndDate is null || r.EndDate >= r.StartDate, "endDate", "The end date is before the start date.");
        var ids = r.SuiteIds.Distinct().ToList();
        if (await db.TestSuites.CountAsync(s => ids.Contains(s.Id), ct) != ids.Count) v.Add("suiteIds", "One or more suites do not exist.");
        if (r.EnvironmentId is { } e && !await db.TestEnvironments.AnyAsync(x => x.Id == e, ct)) v.Add("environmentId", "Unknown environment.");
        if (r.TargetReleaseId is { } rel && !await db.ProductVersions.AnyAsync(x => x.Id == rel, ct)) v.Add("targetReleaseId", "Unknown release.");
        v.ThrowIfInvalid();
    }

    private static void Apply(TestPlan p, PlanUpsert r)
    {
        p.Name = r.Name.Trim(); p.Objective = r.Objective.Clean(); p.Scope = r.Scope.Clean(); p.ProductId = r.ProductId; p.TargetReleaseId = r.TargetReleaseId;
        p.BuildVersion = r.BuildVersion.Clean(); p.EnvironmentId = r.EnvironmentId; p.Owners = r.Owners.Clean(); p.Testers = r.Testers.Clean();
        p.StartDate = r.StartDate; p.EndDate = r.EndDate; p.EntryCriteria = r.EntryCriteria.Clean(); p.ExitCriteria = r.ExitCriteria.Clean(); p.Risk = r.Risk;
        p.DefectAcceptanceRules = r.DefectAcceptanceRules.Clean(); p.IsTemplate = r.IsTemplate;
        p.Suites.Clear();
        var order = 0;
        foreach (var s in r.SuiteIds.Distinct()) p.Suites.Add(new TestPlanSuite { PlanId = p.Id, SuiteId = s, Order = order += 10 });
    }
}

public sealed record CampaignCell(Guid PlanId, Guid EnvironmentId, RunSummaryDto? LatestRun);
public sealed record CampaignDto(Guid Id, long Number, string NumberDisplay, string Name, string? Description, string? BuildVersion, CampaignStatus Status,
    DateTimeOffset? StartedAt, IReadOnlyList<PlanSummaryDto> Plans, IReadOnlyList<EnvironmentRef> Environments, IReadOnlyList<CampaignCell> Cells,
    int Passed, int Failed, int Pending, int Total, long Version);
public sealed record EnvironmentRef(Guid Id, string Code, string Name, EnvironmentHealth Health);
public sealed record CampaignUpsert(string Name, string? Description, string? BuildVersion, IReadOnlyList<Guid> PlanIds, IReadOnlyList<Guid> EnvironmentIds, long? Version = null);

public interface ICampaignService
{
    Task<IReadOnlyList<CampaignDto>> ListAsync(CancellationToken ct = default);
    Task<CampaignDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CampaignDto> CreateAsync(CampaignUpsert request, CancellationToken ct = default);
    Task<CampaignDto> UpdateAsync(Guid id, CampaignUpsert request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Starts one run per plan × environment.</summary>
    Task<IReadOnlyList<RunSummaryDto>> ExecuteAsync(Guid id, RunOrigin? origin = null, CancellationToken ct = default);
}

public sealed class CampaignService(IWingsDbContext db, ISequenceAllocator sequences, IPlanService plans, IExecutionService runs, ICurrentUser user, TimeProvider clock) : ICampaignService
{
    public const string ScopeKey = "CAMPAIGN";

    public async Task<IReadOnlyList<CampaignDto>> ListAsync(CancellationToken ct = default)
    {
        var ids = await db.TestCampaigns.OrderByDescending(c => c.Number).Select(c => c.Id).ToListAsync(ct);
        var list = new List<CampaignDto>();
        foreach (var id in ids) list.Add(await GetAsync(id, ct));
        return list;
    }

    public async Task<CampaignDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.TestCampaigns.AsNoTracking().Include(x => x.Plans).Include(x => x.Environments).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Campaign", id);
        var planIds = c.Plans.Select(p => p.PlanId).ToList();
        var envIds = c.Environments.Select(e => e.EnvironmentId).ToList();
        var planDtos = (await plans.ListAsync(false, ct)).Where(p => planIds.Contains(p.Id)).ToList();
        var envs = await db.TestEnvironments.AsNoTracking().Where(e => envIds.Contains(e.Id)).Select(e => new EnvironmentRef(e.Id, e.Code, e.Name, e.Health)).ToListAsync(ct);
        var campaignRuns = (await runs.ListAsync(new RunFilter(PageSize: 500, CampaignId: id), ct)).Items.ToList();
        var cells = (from p in planDtos from e in envs
                     select new CampaignCell(p.Id, e.Id, campaignRuns.Where(r => r.PlanId == p.Id && r.EnvironmentId == e.Id).MaxBy(r => r.Number))).ToList();
        var latest = cells.Where(x => x.LatestRun is not null).Select(x => x.LatestRun!).ToList();
        var status = c.Status == CampaignStatus.Cancelled ? c.Status
            : latest.Count == 0 ? CampaignStatus.Planned
            : latest.Any(r => r.Status is RunStatus.Queued or RunStatus.Running) ? CampaignStatus.Running : CampaignStatus.Completed;
        return new CampaignDto(c.Id, c.Number, SequenceFormat.Format(c.Number, 5, "CMP-"), c.Name, c.Description, c.BuildVersion, status, c.StartedAt, planDtos, envs, cells,
            latest.Sum(r => r.Passed), latest.Sum(r => r.Failed), latest.Sum(r => r.Pending), latest.Sum(r => r.TotalTests), c.Version);
    }

    public async Task<CampaignDto> CreateAsync(CampaignUpsert r, CancellationToken ct = default)
    {
        await ValidateAsync(r, ct);
        var id = await db.InTransactionAsync(async () =>
        {
            var c = new TestCampaign { Number = await sequences.NextAsync((await db.SequenceScopes.FirstAsync(s => s.Key == ScopeKey, ct)).Id, ct) };
            Apply(c, r);
            db.TestCampaigns.Add(c);
            await db.SaveChangesAsync(ct);
            return c.Id;
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<CampaignDto> UpdateAsync(Guid id, CampaignUpsert r, CancellationToken ct = default)
    {
        await ValidateAsync(r, ct);
        var c = await db.TestCampaigns.Include(x => x.Plans).Include(x => x.Environments).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Campaign", id);
        if (r.Version is { } v) db.SetOriginalVersion(c, v);
        Apply(c, r);
        db.Touch(c);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.TestCampaigns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Campaign", id);
        c.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RunSummaryDto>> ExecuteAsync(Guid id, RunOrigin? origin = null, CancellationToken ct = default)
    {
        var c = await db.TestCampaigns.Include(x => x.Plans).Include(x => x.Environments).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Campaign", id);
        if (c.Plans.Count == 0 || c.Environments.Count == 0) throw new ValidationException("campaign", "Add at least one plan and one environment.");
        var started = new List<RunSummaryDto>();
        var errors = new List<string>();
        foreach (var p in c.Plans)
            foreach (var e in c.Environments)
            {
                try { started.Add(await plans.ExecuteAsync(p.PlanId, new ExecutePlanRequest(e.EnvironmentId, c.BuildVersion), (origin ?? new RunOrigin(user.UserName)) with { CampaignId = id }, ct)); }
                catch (AppException ex) { errors.Add(ex.Message); }
            }
        if (started.Count == 0) throw new ConflictException($"No run could be started: {string.Join(" ", errors.Distinct())}");
        c.StartedAt ??= clock.GetUtcNow();
        c.Status = CampaignStatus.Running;
        db.PendingAuditAction = "CampaignStarted";
        db.PendingAuditSummary = errors.Count == 0 ? $"{started.Count} run(s)" : $"{started.Count} run(s); skipped: {string.Join(" ", errors.Distinct())}";
        await db.SaveChangesAsync(ct);
        return started;
    }

    private async Task ValidateAsync(CampaignUpsert r, CancellationToken ct)
    {
        var v = new Validator().Required(r.Name, "name", 300);
        var planIds = r.PlanIds.Distinct().ToList();
        var envIds = r.EnvironmentIds.Distinct().ToList();
        if (await db.TestPlans.CountAsync(p => planIds.Contains(p.Id) && !p.IsTemplate, ct) != planIds.Count) v.Add("planIds", "Unknown plan (templates cannot be used).");
        if (await db.TestEnvironments.CountAsync(e => envIds.Contains(e.Id), ct) != envIds.Count) v.Add("environmentIds", "Unknown environment.");
        v.ThrowIfInvalid();
    }

    private static void Apply(TestCampaign c, CampaignUpsert r)
    {
        c.Name = r.Name.Trim(); c.Description = r.Description.Clean(); c.BuildVersion = r.BuildVersion.Clean();
        c.Plans.Clear();
        foreach (var p in r.PlanIds.Distinct()) c.Plans.Add(new TestCampaignPlan { CampaignId = c.Id, PlanId = p });
        c.Environments.Clear();
        foreach (var e in r.EnvironmentIds.Distinct()) c.Environments.Add(new TestCampaignEnvironment { CampaignId = c.Id, EnvironmentId = e });
    }
}
