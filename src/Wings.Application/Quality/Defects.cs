using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Domain.Execution;
using Wings.Domain.Quality;

namespace Wings.Application.Quality;

/// <summary>
/// Failure signatures. Volatile parts of an error (numbers, ids, timestamps, temp paths) are masked so the same defect
/// produces the same signature on every run. The fingerprint identifies "this test failing this way" (one defect);
/// the cluster key identifies "this error", across tests (a shared root cause).
/// </summary>
public static partial class FailureSignature
{
    public static string Normalize(string? message)
    {
        var lines = (message ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(3);
        var text = string.Join(" | ", lines).ToLowerInvariant();
        text = GuidPattern().Replace(text, "<id>");
        text = TimestampPattern().Replace(text, "<time>");
        text = HexPattern().Replace(text, "<hex>");
        text = PathPattern().Replace(text, "<path>");
        text = NumberPattern().Replace(text, "<n>");
        text = SpacePattern().Replace(text, " ").Trim();
        return text.Length > 400 ? text[..400] : text;
    }

    public static string Fingerprint(Guid? testCaseId, string identifier, string? message)
        => Hash($"{testCaseId?.ToString() ?? identifier}\n{Normalize(message)}", 32);

    public static string ClusterKey(string? message) => Hash(Normalize(message), 16);

    private static string Hash(string s, int length) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..length];

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b")] private static partial Regex GuidPattern();
    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[t ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?(z|[+-]\d{2}:?\d{2})?|\d{1,2}:\d{2}:\d{2}(\.\d+)?")] private static partial Regex TimestampPattern();
    [GeneratedRegex(@"\b0x[0-9a-f]+\b|\b[0-9a-f]{16,}\b")] private static partial Regex HexPattern();
    [GeneratedRegex(@"([a-z]:\\|/)(tmp|temp|var|private|users|home)[^\s'"":|]*")] private static partial Regex PathPattern();
    [GeneratedRegex(@"\d+([.,]\d+)*")] private static partial Regex NumberPattern();
    [GeneratedRegex(@"\s+")] private static partial Regex SpacePattern();
}

public static class DefectWorkflow
{
    /// <summary>An array (not a set) so EF Core translates Contains to SQL IN.</summary>
    public static readonly DefectStatus[] Unresolved = [DefectStatus.Open, DefectStatus.Assigned, DefectStatus.InProgress, DefectStatus.Fixed, DefectStatus.ReadyForRetest, DefectStatus.Reopened];

    private static readonly Dictionary<DefectStatus, DefectStatus[]> Transitions = new()
    {
        [DefectStatus.Open] = [DefectStatus.Assigned, DefectStatus.InProgress, DefectStatus.Duplicate, DefectStatus.Rejected, DefectStatus.Deferred],
        [DefectStatus.Assigned] = [DefectStatus.InProgress, DefectStatus.Fixed, DefectStatus.Duplicate, DefectStatus.Rejected, DefectStatus.Deferred],
        [DefectStatus.InProgress] = [DefectStatus.Fixed, DefectStatus.Assigned, DefectStatus.Duplicate, DefectStatus.Rejected, DefectStatus.Deferred],
        [DefectStatus.Fixed] = [DefectStatus.ReadyForRetest, DefectStatus.Reopened],
        [DefectStatus.ReadyForRetest] = [DefectStatus.Verified, DefectStatus.Reopened],
        [DefectStatus.Verified] = [DefectStatus.Closed, DefectStatus.Reopened],
        [DefectStatus.Closed] = [DefectStatus.Reopened],
        [DefectStatus.Reopened] = [DefectStatus.Assigned, DefectStatus.InProgress, DefectStatus.Fixed, DefectStatus.Duplicate, DefectStatus.Rejected, DefectStatus.Deferred],
        [DefectStatus.Duplicate] = [DefectStatus.Reopened],
        [DefectStatus.Rejected] = [DefectStatus.Reopened],
        [DefectStatus.Deferred] = [DefectStatus.Reopened, DefectStatus.Assigned, DefectStatus.InProgress],
    };

    public static IReadOnlyList<DefectStatus> Allowed(DefectStatus from) => Transitions[from];
}

public sealed record DefectSummaryDto(Guid Id, long Number, string NumberDisplay, string Title, DefectStatus Status, DefectSeverity Severity, string Priority,
    Guid? ProductId, string? ProductName, Guid? FeatureId, string? FeatureName, long? FeatureSequence, Guid? TestCaseId, string? TestCaseNumber, string? AssignedTo,
    int OccurrenceCount, DateTimeOffset? LastSeenAt, string? ExternalKey, string? ExternalUrl, DateTimeOffset CreatedAt, string CreatedBy, long Version);

public sealed record DefectEventDto(long Id, DateTimeOffset Timestamp, string UserName, string Kind, DefectStatus? FromStatus, DefectStatus? ToStatus, string? Text);

public sealed record DefectOccurrenceDto(Guid TestExecutionId, Guid RunId, string RunNumber, string Outcome, string? ErrorMessage, DateTimeOffset? CompletedAt,
    string? BuildVersion, string? EnvironmentName);

public sealed record DefectDetailDto(DefectSummaryDto Summary, string? Description, Guid? FailingRunId, string? FailingRunNumber, Guid? FailingTestExecutionId,
    string? BuildVersion, Guid? EnvironmentId, string? EnvironmentName, string? ReproductionSteps, string? ExpectedResult, string? ActualResult, string? RootCause,
    Guid? FixVersionId, string? FixVersionLabel, string? VerificationResult, Guid? DuplicateOfId, string? DuplicateOfNumber, string? Fingerprint,
    IReadOnlyList<DefectEventDto> History, IReadOnlyList<DefectOccurrenceDto> Occurrences, IReadOnlyList<ArtifactDto> Evidence,
    IReadOnlyList<DefectStatus> AllowedTransitions, IReadOnlyList<RunSummaryDto> Retests);

public sealed record DefectUpsert
{
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? FeatureId { get; init; }
    public Guid? TestCaseId { get; init; }
    public string? BuildVersion { get; init; }
    public Guid? EnvironmentId { get; init; }
    public DefectSeverity Severity { get; init; } = DefectSeverity.Major;
    public string Priority { get; init; } = "P2";
    public string? ReproductionSteps { get; init; }
    public string? ExpectedResult { get; init; }
    public string? ActualResult { get; init; }
    public string? AssignedTo { get; init; }
    public string? RootCause { get; init; }
    public Guid? FixVersionId { get; init; }
    public long? Version { get; init; }
}

/// <summary>Moves a defect through its workflow. <see cref="Retest"/> on ReadyForRetest starts a run of the linked test case.</summary>
public sealed record DefectTransition(DefectStatus To, string? Comment = null, string? AssignedTo = null, Guid? DuplicateOfId = null, string? RootCause = null,
    Guid? FixVersionId = null, string? VerificationResult = null, bool Retest = false, Guid? RetestEnvironmentId = null, long? Version = null);

public sealed record DefectFilter(Guid? ProductId = null, Guid? FeatureId = null, Guid? TestCaseId = null, DefectStatus? Status = null, DefectSeverity? Severity = null,
    string? AssignedTo = null, string? Text = null, bool OpenOnly = false, int Page = 1, int PageSize = 50);

public sealed record FailureSuggestionDto(Guid TestExecutionId, Guid RunId, string RunNumber, string DisplayName, Guid? TestCaseId, string? TestCaseNumber,
    Guid? FeatureId, string? FeatureName, string? ErrorMessage, string Fingerprint, string ClusterKey, DateTimeOffset? CompletedAt,
    Guid? ExistingDefectId, string? ExistingDefectNumber, DefectStatus? ExistingDefectStatus, Guid? PossibleDuplicateId, string? PossibleDuplicateNumber,
    string SuggestedTitle, DefectSeverity SuggestedSeverity, string SuggestedPriority);

public sealed record CreateFromFailureRequest(Guid TestExecutionId, string? Title = null, DefectSeverity? Severity = null, string? Priority = null,
    string? AssignedTo = null, string? Description = null);

public sealed record CreateFromFailureResult(DefectDetailDto Defect, bool LinkedToExisting);

public sealed record FailureClusterDto(string ClusterKey, string SampleMessage, int Failures, int DistinctTests, DateTimeOffset FirstSeen, DateTimeOffset LastSeen,
    IReadOnlyList<string> Tests, IReadOnlyList<string> DefectNumbers);

public sealed record RecurringFailureDto(Guid? TestCaseId, string? TestCaseNumber, string Identifier, string DisplayName, int Executions, int Failures,
    int ConsecutiveFailures, int Flips, DateTimeOffset LastFailedAt, string? LastError, Guid? DefectId, string? DefectNumber);

public interface IDefectService
{
    Task<PagedResult<DefectSummaryDto>> ListAsync(DefectFilter filter, CancellationToken ct = default);
    Task<DefectDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<DefectDetailDto> CreateAsync(DefectUpsert request, CancellationToken ct = default);
    Task<DefectDetailDto> UpdateAsync(Guid id, DefectUpsert request, CancellationToken ct = default);
    Task<DefectDetailDto> TransitionAsync(Guid id, DefectTransition request, CancellationToken ct = default);
    Task<DefectDetailDto> CommentAsync(Guid id, string text, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FailureSuggestionDto>> SuggestionsAsync(Guid? runId, Guid? productId, int days, CancellationToken ct = default);
    Task<CreateFromFailureResult> CreateFromFailureAsync(CreateFromFailureRequest request, CancellationToken ct = default);
    Task<DefectDetailDto> LinkFailureAsync(Guid id, Guid testExecutionId, CancellationToken ct = default);
    Task<IReadOnlyList<FailureClusterDto>> ClustersAsync(Guid? productId, int days, CancellationToken ct = default);
    Task<IReadOnlyList<RecurringFailureDto>> RecurringAsync(Guid? productId, int days, int minFailures, CancellationToken ct = default);
    /// <summary>After a run finishes: attaches recurrences, reopens resolved defects that failed again, and settles automated retests.</summary>
    Task ProcessRunAsync(Guid runId, CancellationToken ct = default);
}

public sealed class DefectService(IWingsDbContext db, ISequenceAllocator sequences, ICurrentUser user, TimeProvider clock, IExecutionService runs,
    INotificationPublisher notifications, IUserDirectory users, IProductAccess access, IChangeNotifier notifier) : IDefectService
{
    public const string ScopeKey = "DEFECT";
    private const string SystemUser = "system";

    public static string Format(long n) => SequenceFormat.Format(n, 5, "DEF-");

    // ---- queries ----

    public async Task<PagedResult<DefectSummaryDto>> ListAsync(DefectFilter f, CancellationToken ct = default)
    {
        var q = db.Defects.AsNoTracking();
        if (await access.VisibleProductIdsAsync(ct) is { } visible) q = q.Where(d => d.ProductId == null || visible.Contains(d.ProductId.Value));
        if (f.ProductId is { } p) q = q.Where(d => d.ProductId == p);
        if (f.FeatureId is { } fid)
        {
            var root = await db.Features.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fid, ct) ?? throw new NotFoundException("Feature", fid);
            var ids = db.Features.Where(x => x.SequenceScopeId == root.SequenceScopeId && x.Path.StartsWith(root.Path)).Select(x => x.Id);
            q = q.Where(d => d.FeatureId != null && ids.Contains(d.FeatureId.Value));
        }
        if (f.TestCaseId is { } tc) q = q.Where(d => d.TestCaseId == tc);
        if (f.Status is { } s) q = q.Where(d => d.Status == s);
        if (f.OpenOnly) q = q.Where(d => DefectWorkflow.Unresolved.Contains(d.Status));
        if (f.Severity is { } sev) q = q.Where(d => d.Severity == sev);
        if (f.AssignedTo.Clean() is { } a) q = q.Where(d => d.AssignedTo == a);
        if (f.Text.Clean() is { } text)
        {
            var like = Search.Like(text);
            long.TryParse(text.Replace("DEF-", "", StringComparison.OrdinalIgnoreCase), out var number);
            q = q.Where(d => EF.Functions.Like(d.Title, like, Search.Escape) || d.Number == number || (d.ExternalKey != null && EF.Functions.Like(d.ExternalKey, like, Search.Escape)));
        }
        var page = new PageRequest(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await Summaries(q.OrderByDescending(d => d.Number).Skip(page.Skip).Take(page.SafePageSize), ct);
        return new PagedResult<DefectSummaryDto>(rows, total, page.SafePage, page.SafePageSize);
    }

    private async Task<List<DefectSummaryDto>> Summaries(IQueryable<Defect> q, CancellationToken ct)
    {
        var rows = await q.Select(d => new
        {
            d,
            Product = db.Products.Where(p => p.Id == d.ProductId).Select(p => p.Name).FirstOrDefault(),
            Feature = db.Features.Where(x => x.Id == d.FeatureId).Select(x => new { x.Name, x.SequenceNumber }).FirstOrDefault(),
            Tc = db.TestCases.IgnoreQueryFilters().Where(t => t.Id == d.TestCaseId).Select(t => (long?)t.Number).FirstOrDefault(),
        }).ToListAsync(ct);
        return rows.Select(x => new DefectSummaryDto(x.d.Id, x.d.Number, Format(x.d.Number), x.d.Title, x.d.Status, x.d.Severity, x.d.Priority, x.d.ProductId, x.Product,
            x.d.FeatureId, x.Feature?.Name, x.Feature?.SequenceNumber, x.d.TestCaseId, x.Tc is { } n ? SequenceFormat.Format(n, 5, "TC-") : null, x.d.AssignedTo,
            x.d.OccurrenceCount, x.d.LastSeenAt, x.d.ExternalKey, x.d.ExternalUrl, x.d.CreatedAt, x.d.CreatedBy, x.d.Version)).ToList();
    }

    public async Task<DefectDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var d = await db.Defects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Defect", id);
        if (d.ProductId is { } pid) await access.EnsureCanViewAsync(pid, ct);
        var summary = (await Summaries(db.Defects.Where(x => x.Id == id), ct)).Single();
        var history = await db.DefectEvents.AsNoTracking().Where(e => e.DefectId == id).OrderBy(e => e.Id)
            .Select(e => new DefectEventDto(e.Id, e.Timestamp, e.UserName, e.Kind, e.FromStatus, e.ToStatus, e.Text)).ToListAsync(ct);
        var occurrences = await (from o in db.DefectOccurrences.Where(o => o.DefectId == id)
                                 join t in db.TestExecutions on o.TestExecutionId equals t.Id
                                 join r in db.ExecutionRuns on t.RunId equals r.Id
                                 orderby o.LinkedAt descending
                                 select new { t.Id, t.RunId, r.Number, t.Outcome, t.ErrorMessage, t.CompletedAt, r.BuildVersion, Env = db.TestEnvironments.Where(e => e.Id == r.EnvironmentId).Select(e => e.Name).FirstOrDefault() })
            .Take(100).ToListAsync(ct);
        var failing = d.FailingTestExecutionId is { } fe ? await db.TestExecutions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == fe, ct) : null;
        var evidence = failing is null ? [] : await db.ExecutionArtifacts.AsNoTracking()
            .Where(a => a.TestExecutionId == failing.Id || (a.JobId == failing.JobId && a.TestExecutionId == null)).OrderBy(a => a.CreatedAt)
            .Select(a => new ArtifactDto(a.Id, a.JobId, a.TestExecutionId, a.Kind, a.FileName, a.ContentType, a.SizeBytes, a.Sha256, a.CreatedAt)).ToListAsync(ct);
        var retestIds = await db.ExecutionRuns.AsNoTracking().Where(r => r.DefectId == id).OrderByDescending(r => r.Number).Select(r => r.Id).Take(10).ToListAsync(ct);
        var retests = new List<RunSummaryDto>();
        foreach (var rid in retestIds) retests.Add((await runs.GetAsync(rid, ct)).Run);

        return new DefectDetailDto(summary, d.Description, d.FailingRunId,
            d.FailingRunId is { } fr ? await db.ExecutionRuns.Where(r => r.Id == fr).Select(r => ExecutionService.Format(r.Number)).FirstOrDefaultAsync(ct) : null,
            d.FailingTestExecutionId, d.BuildVersion, d.EnvironmentId,
            d.EnvironmentId is { } env ? await db.TestEnvironments.IgnoreQueryFilters().Where(e => e.Id == env).Select(e => e.Name).FirstOrDefaultAsync(ct) : null,
            d.ReproductionSteps, d.ExpectedResult, d.ActualResult, d.RootCause, d.FixVersionId,
            d.FixVersionId is { } fv ? await db.ProductVersions.Where(v => v.Id == fv).Select(v => v.VersionLabel).FirstOrDefaultAsync(ct) : null,
            d.VerificationResult, d.DuplicateOfId,
            d.DuplicateOfId is { } dup ? await db.Defects.IgnoreQueryFilters().Where(x => x.Id == dup).Select(x => Format(x.Number)).FirstOrDefaultAsync(ct) : null,
            d.Fingerprint, history,
            occurrences.Select(o => new DefectOccurrenceDto(o.Id, o.RunId, ExecutionService.Format(o.Number), o.Outcome, o.ErrorMessage, o.CompletedAt, o.BuildVersion, o.Env)).ToList(),
            evidence, DefectWorkflow.Allowed(d.Status), retests);
    }

    // ---- commands ----

    public async Task<DefectDetailDto> CreateAsync(DefectUpsert r, CancellationToken ct = default)
    {
        var d = new Defect();
        await ApplyAsync(d, r, ct);
        d.Status = d.AssignedTo is null ? DefectStatus.Open : DefectStatus.Assigned;
        await db.InTransactionAsync(async () =>
        {
            d.Number = await sequences.NextAsync(await ScopeIdAsync(ct), ct);
            db.Defects.Add(d);
            AddEvent(d.Id, "Created", null, d.Status, null);
            await db.SaveChangesAsync(ct);
        }, ct);
        await AfterAssignmentAsync(d, null, ct);
        await notifier.NotifyAsync("defectsChanged", new { defectId = d.Id }, ct);
        return await GetAsync(d.Id, ct);
    }

    public async Task<DefectDetailDto> UpdateAsync(Guid id, DefectUpsert r, CancellationToken ct = default)
    {
        var d = await LoadAsync(id, ct);
        if (r.Version is { } v) db.SetOriginalVersion(d, v);
        var previousAssignee = d.AssignedTo;
        await ApplyAsync(d, r, ct);
        if (!string.Equals(previousAssignee, d.AssignedTo, StringComparison.OrdinalIgnoreCase))
        {
            AddEvent(d.Id, "Assigned", null, null, d.AssignedTo is null ? "Unassigned" : $"Assigned to {d.AssignedTo}");
            if (d.AssignedTo is not null && d.Status == DefectStatus.Open) { AddEvent(d.Id, "Status", d.Status, DefectStatus.Assigned, null); d.Status = DefectStatus.Assigned; }
        }
        await db.SaveChangesAsync(ct);
        await AfterAssignmentAsync(d, previousAssignee, ct);
        await notifier.NotifyAsync("defectsChanged", new { defectId = d.Id }, ct);
        return await GetAsync(id, ct);
    }

    private async Task ApplyAsync(Defect d, DefectUpsert r, CancellationToken ct)
    {
        new Validator()
            .Required(r.Title, "title", 300)
            .Must(r.Priority is "P1" or "P2" or "P3" or "P4", "priority", "Priority must be P1–P4.")
            .MaxLength(r.BuildVersion, "buildVersion", 100)
            .ThrowIfInvalid();
        var productId = r.ProductId;
        if (r.FeatureId is { } fid)
            productId = await db.Features.Where(f => f.Id == fid).Select(f => (Guid?)f.ProductId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Feature", fid);
        if (productId is { } pid)
        {
            if (!await db.Products.AnyAsync(p => p.Id == pid, ct)) throw new NotFoundException("Product", pid);
            await access.EnsureCanEditAsync(pid, ct);
        }
        if (r.TestCaseId is { } tc && !await db.TestCases.AnyAsync(t => t.Id == tc, ct)) throw new NotFoundException("Test case", tc);
        if (r.FixVersionId is { } fv && !await db.ProductVersions.AnyAsync(v => v.Id == fv && v.ProductId == productId, ct))
            throw new ValidationException("fixVersionId", "The fix version must be a release of the defect's product.");
        d.AssignedTo = await ResolveAssigneeAsync(r.AssignedTo, ct);
        d.Title = r.Title.Trim();
        d.Description = r.Description.Clean();
        d.ProductId = productId;
        d.FeatureId = r.FeatureId;
        d.TestCaseId = r.TestCaseId;
        d.BuildVersion = r.BuildVersion.Clean();
        d.EnvironmentId = r.EnvironmentId;
        d.Severity = r.Severity;
        d.Priority = r.Priority;
        d.ReproductionSteps = r.ReproductionSteps.Clean();
        d.ExpectedResult = r.ExpectedResult.Clean();
        d.ActualResult = r.ActualResult.Clean();
        d.RootCause = r.RootCause.Clean();
        d.FixVersionId = r.FixVersionId;
    }

    private async Task<string?> ResolveAssigneeAsync(string? name, CancellationToken ct)
    {
        if (name.Clean() is not { } n) return null;
        var u = await users.FindByUserNameAsync(n, ct);
        if (u is null || !u.IsActive) throw new ValidationException("assignedTo", $"User '{n}' does not exist or is inactive.");
        return u.UserName;
    }

    private async Task AfterAssignmentAsync(Defect d, string? previous, CancellationToken ct)
    {
        if (d.AssignedTo is null || string.Equals(previous, d.AssignedTo, StringComparison.OrdinalIgnoreCase)) return;
        if (await users.FindByUserNameAsync(d.AssignedTo, ct) is not { } assignee) return;
        await notifications.PublishAsync(new NotificationMessage(NotificationEvents.DefectAssigned,
            d.Severity == DefectSeverity.Critical ? NotificationSeverity.Warning : NotificationSeverity.Info,
            $"{Format(d.Number)} assigned to you: {d.Title}", $"Severity {d.Severity}, priority {d.Priority}. Assigned by {user.UserName}.",
            $"/defects/{d.Id}", d.ProductId, new Dictionary<string, string> { ["defect"] = Format(d.Number), ["severity"] = d.Severity.ToString() }, [assignee.Id]), ct);
    }

    public async Task<DefectDetailDto> TransitionAsync(Guid id, DefectTransition r, CancellationToken ct = default)
    {
        var d = await LoadAsync(id, ct);
        if (r.Version is { } v) db.SetOriginalVersion(d, v);
        if (!DefectWorkflow.Allowed(d.Status).Contains(r.To))
            throw new ConflictException($"A defect cannot move from {d.Status} to {r.To}. Allowed: {string.Join(", ", DefectWorkflow.Allowed(d.Status))}.");
        var comment = r.Comment.Clean();
        var previousAssignee = d.AssignedTo;
        var val = new Validator();
        switch (r.To)
        {
            case DefectStatus.Assigned:
                d.AssignedTo = await ResolveAssigneeAsync(r.AssignedTo ?? d.AssignedTo, ct);
                val.Must(d.AssignedTo is not null, "assignedTo", "Choose who the defect is assigned to.");
                break;
            case DefectStatus.Duplicate:
                val.Must(r.DuplicateOfId is { } dup && dup != id, "duplicateOfId", "Choose the defect this one duplicates.");
                if (r.DuplicateOfId is { } dupId && !await db.Defects.AnyAsync(x => x.Id == dupId, ct)) throw new NotFoundException("Defect", dupId);
                d.DuplicateOfId = r.DuplicateOfId;
                break;
            case DefectStatus.Rejected or DefectStatus.Deferred or DefectStatus.Reopened:
                val.Must(comment is not null, "comment", $"Explain why the defect is {r.To.ToString().ToLowerInvariant()}.");
                break;
            case DefectStatus.Fixed:
                if (r.RootCause.Clean() is { } rc) d.RootCause = rc;
                if (r.FixVersionId is { } fv)
                {
                    if (!await db.ProductVersions.AnyAsync(x => x.Id == fv && x.ProductId == d.ProductId, ct)) throw new ValidationException("fixVersionId", "The fix version must be a release of the defect's product.");
                    d.FixVersionId = fv;
                }
                break;
            case DefectStatus.Verified:
                d.VerificationResult = r.VerificationResult.Clean() ?? comment ?? d.VerificationResult;
                val.Must(d.VerificationResult is not null, "verificationResult", "Record how the fix was verified.");
                break;
            case DefectStatus.InProgress when d.AssignedTo is null:
                d.AssignedTo = user.UserName;
                break;
        }
        val.ThrowIfInvalid();

        AddEvent(d.Id, "Status", d.Status, r.To, comment);
        d.Status = r.To;
        await db.SaveChangesAsync(ct);
        await AfterAssignmentAsync(d, previousAssignee, ct);

        if (r.To == DefectStatus.ReadyForRetest && r.Retest)
        {
            if (d.TestCaseId is not { } tc) throw new ConflictException("Link a test case to the defect to retest it.");
            var run = await runs.StartAsync(new StartRunRequest
            {
                Name = $"Retest {Format(d.Number)}: {d.Title}", TestCaseIds = [tc], EnvironmentId = r.RetestEnvironmentId ?? d.EnvironmentId, IncludeManual = true,
                BuildVersion = d.FixVersionId is { } fv ? await db.ProductVersions.Where(x => x.Id == fv).Select(x => x.BuildVersion).FirstOrDefaultAsync(ct) : null,
            }, new RunOrigin($"defect:{Format(d.Number)} ({user.UserName})"), ct);
            await db.ExecutionRuns.Where(x => x.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DefectId, d.Id), ct);
            AddEvent(d.Id, "Retest", null, null, $"Retest started as {run.NumberDisplay}.");
            await db.SaveChangesAsync(ct);
        }
        await notifier.NotifyAsync("defectsChanged", new { defectId = d.Id }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<DefectDetailDto> CommentAsync(Guid id, string text, CancellationToken ct = default)
    {
        new Validator().Required(text, "text", 8000).ThrowIfInvalid();
        var d = await LoadAsync(id, ct);
        AddEvent(d.Id, "Comment", null, null, text.Trim());
        db.Touch(d);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var d = await LoadAsync(id, ct);
        d.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    // ---- failures → defects ----

    public async Task<IReadOnlyList<FailureSuggestionDto>> SuggestionsAsync(Guid? runId, Guid? productId, int days, CancellationToken ct = default)
    {
        var since = clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 90));
        var q = db.TestExecutions.AsNoTracking().Where(t => t.Outcome == "Failed" && !t.IsSuperseded);
        q = runId is { } rid ? q.Where(t => t.RunId == rid) : q.Where(t => t.CompletedAt >= since);
        if (productId is { } pid) q = q.Where(t => t.FeatureId != null && db.Features.Any(f => f.Id == t.FeatureId && f.ProductId == pid));
        var failures = await q.OrderByDescending(t => t.CompletedAt).Take(500).ToListAsync(ct);
        if (failures.Count == 0) return [];

        // Only the most recent failure of each distinct signature matters.
        var latest = failures.Select(t => (t, Fp: FailureSignature.Fingerprint(t.TestCaseId, t.Identifier, t.ErrorMessage ?? t.Reason)))
            .GroupBy(x => x.Fp).Select(g => g.First()).ToList();
        var fps = latest.Select(x => x.Fp).ToList();
        var known = await db.Defects.AsNoTracking().Where(d => d.Fingerprint != null && fps.Contains(d.Fingerprint))
            .OrderByDescending(d => d.Number).Select(d => new { d.Id, d.Number, d.Status, d.Fingerprint }).ToListAsync(ct);
        var tcIds = latest.Where(x => x.t.TestCaseId is not null).Select(x => x.t.TestCaseId!.Value).Distinct().ToList();
        var byTestCase = await db.Defects.AsNoTracking().Where(d => d.TestCaseId != null && tcIds.Contains(d.TestCaseId.Value) && DefectWorkflow.Unresolved.Contains(d.Status))
            .Select(d => new { d.Id, d.Number, d.TestCaseId }).ToListAsync(ct);
        var cases = await db.TestCases.IgnoreQueryFilters().AsNoTracking().Where(t => tcIds.Contains(t.Id)).Select(t => new { t.Id, t.Number, t.Name, t.Priority, t.FailureSeverity }).ToDictionaryAsync(t => t.Id, ct);
        var featureIds = latest.Where(x => x.t.FeatureId is not null).Select(x => x.t.FeatureId!.Value).Distinct().ToList();
        var features = await db.Features.AsNoTracking().Where(f => featureIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        var runIds = latest.Select(x => x.t.RunId).Distinct().ToList();
        var runNumbers = await db.ExecutionRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, ct);

        return latest.Select(x =>
        {
            var t = x.t;
            var existing = known.FirstOrDefault(k => k.Fingerprint == x.Fp && DefectWorkflow.Unresolved.Contains(k.Status)) ?? known.FirstOrDefault(k => k.Fingerprint == x.Fp);
            var possible = existing is null ? byTestCase.FirstOrDefault(b => b.TestCaseId == t.TestCaseId) : null;
            var tc = t.TestCaseId is { } id ? cases.GetValueOrDefault(id) : null;
            var (severity, priority) = Suggest(tc?.Priority, tc?.FailureSeverity);
            return new FailureSuggestionDto(t.Id, t.RunId, ExecutionService.Format(runNumbers.GetValueOrDefault(t.RunId)), t.DisplayName, t.TestCaseId,
                tc is null ? null : SequenceFormat.Format(tc.Number, 5, "TC-"), t.FeatureId, t.FeatureId is { } f ? features.GetValueOrDefault(f) : null,
                t.ErrorMessage ?? t.Reason, x.Fp, FailureSignature.ClusterKey(t.ErrorMessage ?? t.Reason), t.CompletedAt,
                existing?.Id, existing is null ? null : Format(existing.Number), existing?.Status, possible?.Id, possible is null ? null : Format(possible.Number),
                SuggestTitle(tc?.Name ?? t.DisplayName, t.ErrorMessage ?? t.Reason), severity, priority);
        }).ToList();
    }

    private static (DefectSeverity, string) Suggest(string? priority, string? failureSeverity) => (failureSeverity?.ToUpperInvariant()) switch
    {
        "CRITICAL" or "BLOCKER" => (DefectSeverity.Critical, "P1"),
        "MINOR" => (DefectSeverity.Minor, priority ?? "P3"),
        "TRIVIAL" => (DefectSeverity.Trivial, "P4"),
        _ => (priority == "P1" ? DefectSeverity.Critical : DefectSeverity.Major, priority ?? "P2"),
    };

    /// <summary>Test name plus the most telling line of the error: "Expected … / Received …" for assertion failures, else the first meaningful line.</summary>
    internal static string SuggestTitle(string test, string? message)
    {
        var lines = (message ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var expected = lines.FirstOrDefault(l => l.StartsWith("Expected", StringComparison.OrdinalIgnoreCase));
        var received = lines.FirstOrDefault(l => l.StartsWith("Received", StringComparison.OrdinalIgnoreCase) || l.StartsWith("But was", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Actual", StringComparison.OrdinalIgnoreCase));
        string[] noise = ["Error: expect(", "expect(", "Locator:", "Call log", "- ", "at ", "Timeout:"];
        var detail = expected is not null && received is not null ? $"{expected}, {received}"
            : lines.FirstOrDefault(l => !noise.Any(n => l.StartsWith(n, StringComparison.Ordinal))) ?? lines.FirstOrDefault();
        var title = string.IsNullOrEmpty(detail) ? $"{test} fails" : $"{test}: {detail}";
        return title.Length > 300 ? title[..299] + "…" : title;
    }

    public async Task<CreateFromFailureResult> CreateFromFailureAsync(CreateFromFailureRequest r, CancellationToken ct = default)
    {
        var t = await db.TestExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.TestExecutionId, ct) ?? throw new NotFoundException("Test execution", r.TestExecutionId);
        if (t.Outcome is not ("Failed" or "Blocked")) throw new ConflictException($"Only failed or blocked tests can raise defects (this one is {t.Outcome}).");
        var fp = FailureSignature.Fingerprint(t.TestCaseId, t.Identifier, t.ErrorMessage ?? t.Reason);

        // The same failure signature already has an unresolved defect: record another occurrence instead of a duplicate.
        var existing = await db.Defects.Where(d => d.Fingerprint == fp && DefectWorkflow.Unresolved.Contains(d.Status)).OrderByDescending(d => d.Number).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            await AttachAsync(existing, t, $"Linked by {user.UserName} from {await RunNumberAsync(t.RunId, ct)}.", ct);
            await db.SaveChangesAsync(ct);
            return new CreateFromFailureResult(await GetAsync(existing.Id, ct), true);
        }

        var run = await db.ExecutionRuns.AsNoTracking().FirstAsync(x => x.Id == t.RunId, ct);
        var tc = t.TestCaseId is { } tcId ? await db.TestCases.IgnoreQueryFilters().AsNoTracking().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == tcId, ct) : null;
        var (severity, priority) = Suggest(tc?.Priority, tc?.FailureSeverity);
        var steps = tc?.Steps.OrderBy(s => s.StepNumber).Select(s => $"{s.StepNumber}. {s.Action}" + (s.ExpectedResult is { } e ? $" → {e}" : "")).ToList() ?? [];
        var repro = new StringBuilder();
        if (tc?.Preconditions is { } pre) repro.AppendLine($"Preconditions: {pre}").AppendLine();
        if (steps.Count > 0) repro.AppendLine(string.Join("\n", steps)).AppendLine();
        repro.Append($"Automated test: {t.Identifier}\nRun: {ExecutionService.Format(run.Number)}{(run.BuildVersion is { } b ? $", build {b}" : "")}");
        var featureId = t.FeatureId ?? (tc is null ? null : await db.TestCaseFeatureMappings.Where(m => m.TestCaseId == tc.Id).OrderByDescending(m => m.IsPrimary).Select(m => (Guid?)m.FeatureId).FirstOrDefaultAsync(ct));

        var d = new Defect
        {
            Title = (r.Title.Clean() ?? SuggestTitle(tc?.Name ?? t.DisplayName, t.ErrorMessage ?? t.Reason)),
            Description = r.Description.Clean() ?? $"Raised from the failure of \"{t.DisplayName}\" in {ExecutionService.Format(run.Number)} ({run.Name}).",
            FeatureId = featureId, TestCaseId = t.TestCaseId, FailingTestExecutionId = t.Id, FailingRunId = t.RunId, BuildVersion = run.BuildVersion,
            EnvironmentId = run.EnvironmentId, Severity = r.Severity ?? severity, Priority = r.Priority ?? priority, ReproductionSteps = repro.ToString(),
            ExpectedResult = tc?.ExpectedResults ?? tc?.Steps.OrderBy(s => s.StepNumber).LastOrDefault(s => s.ExpectedResult is not null)?.ExpectedResult,
            ActualResult = Truncate(t.ErrorMessage ?? t.Reason, 8000), Fingerprint = fp, OccurrenceCount = 0, LastSeenAt = t.CompletedAt,
        };
        new Validator().Must(d.Priority is "P1" or "P2" or "P3" or "P4", "priority", "Priority must be P1–P4.").ThrowIfInvalid();
        if (featureId is { } f) d.ProductId = await db.Features.Where(x => x.Id == f).Select(x => (Guid?)x.ProductId).FirstOrDefaultAsync(ct);
        if (d.ProductId is { } pid) await access.EnsureCanEditAsync(pid, ct);
        d.AssignedTo = await ResolveAssigneeAsync(r.AssignedTo, ct);
        d.Status = d.AssignedTo is null ? DefectStatus.Open : DefectStatus.Assigned;

        await db.InTransactionAsync(async () =>
        {
            d.Number = await sequences.NextAsync(await ScopeIdAsync(ct), ct);
            db.Defects.Add(d);
            AddEvent(d.Id, "Created", null, d.Status, $"Raised from a failed test in {ExecutionService.Format(run.Number)}.");
            await AttachAsync(d, t, null, ct);
            await db.SaveChangesAsync(ct);
        }, ct);
        await AfterAssignmentAsync(d, null, ct);
        await notifier.NotifyAsync("defectsChanged", new { defectId = d.Id }, ct);
        return new CreateFromFailureResult(await GetAsync(d.Id, ct), false);
    }

    public async Task<DefectDetailDto> LinkFailureAsync(Guid id, Guid testExecutionId, CancellationToken ct = default)
    {
        var d = await LoadAsync(id, ct);
        var t = await db.TestExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == testExecutionId, ct) ?? throw new NotFoundException("Test execution", testExecutionId);
        await AttachAsync(d, t, $"Linked by {user.UserName} from {await RunNumberAsync(t.RunId, ct)}.", ct);
        d.FailingTestExecutionId ??= t.Id;
        d.FailingRunId ??= t.RunId;
        d.TestCaseId ??= t.TestCaseId;
        d.Fingerprint ??= FailureSignature.Fingerprint(t.TestCaseId, t.Identifier, t.ErrorMessage ?? t.Reason);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task AttachAsync(Defect d, TestExecution t, string? note, CancellationToken ct)
    {
        if (await db.DefectOccurrences.AnyAsync(o => o.DefectId == d.Id && o.TestExecutionId == t.Id, ct)) return;
        db.DefectOccurrences.Add(new DefectOccurrence { DefectId = d.Id, TestExecutionId = t.Id, RunId = t.RunId, LinkedAt = clock.GetUtcNow() });
        d.OccurrenceCount++;
        if (t.CompletedAt > d.LastSeenAt || d.LastSeenAt is null) d.LastSeenAt = t.CompletedAt;
        if (note is not null) AddEvent(d.Id, "Occurrence", null, null, note);
    }

    // ---- analysis ----

    public async Task<IReadOnlyList<FailureClusterDto>> ClustersAsync(Guid? productId, int days, CancellationToken ct = default)
    {
        var failures = await RecentAsync(productId, days, ct, failedOnly: true);
        var fps = failures.Select(f => FailureSignature.Fingerprint(f.TestCaseId, f.Identifier, f.ErrorMessage ?? f.Reason)).Distinct().ToList();
        var defects = await db.Defects.AsNoTracking().Where(d => d.Fingerprint != null && fps.Contains(d.Fingerprint)).Select(d => new { d.Fingerprint, d.Number }).ToListAsync(ct);
        return failures.GroupBy(f => FailureSignature.ClusterKey(f.ErrorMessage ?? f.Reason))
            .Select(g =>
            {
                var groupFps = g.Select(f => FailureSignature.Fingerprint(f.TestCaseId, f.Identifier, f.ErrorMessage ?? f.Reason)).ToHashSet();
                return new FailureClusterDto(g.Key, (g.First().ErrorMessage ?? g.First().Reason ?? "(no message)").Split('\n')[0].Trim(), g.Count(),
                    g.Select(f => f.Identifier).Distinct().Count(), g.Min(f => f.CompletedAt!.Value), g.Max(f => f.CompletedAt!.Value),
                    g.Select(f => f.DisplayName).Distinct().Take(20).ToList(),
                    defects.Where(d => groupFps.Contains(d.Fingerprint!)).Select(d => Format(d.Number)).Distinct().ToList());
            })
            .OrderByDescending(c => c.DistinctTests).ThenByDescending(c => c.Failures).Take(100).ToList();
    }

    public async Task<IReadOnlyList<RecurringFailureDto>> RecurringAsync(Guid? productId, int days, int minFailures, CancellationToken ct = default)
    {
        var all = await RecentAsync(productId, days, ct, failedOnly: false);
        var groups = all.Where(t => t.Outcome is "Passed" or "Failed").GroupBy(t => t.TestCaseId?.ToString() ?? t.Identifier).ToList();
        var tcIds = all.Where(t => t.TestCaseId is not null).Select(t => t.TestCaseId!.Value).Distinct().ToList();
        var numbers = await db.TestCases.IgnoreQueryFilters().Where(t => tcIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Number, ct);
        var open = await db.Defects.AsNoTracking().Where(d => d.TestCaseId != null && tcIds.Contains(d.TestCaseId.Value) && DefectWorkflow.Unresolved.Contains(d.Status))
            .Select(d => new { d.Id, d.Number, d.TestCaseId }).ToListAsync(ct);
        var result = new List<RecurringFailureDto>();
        foreach (var g in groups)
        {
            var ordered = g.OrderBy(t => t.CompletedAt).ToList();
            var failures = ordered.Count(t => t.Outcome == "Failed");
            if (failures < Math.Max(1, minFailures)) continue;
            var streak = ordered.AsEnumerable().Reverse().TakeWhile(t => t.Outcome == "Failed").Count();
            var flips = ordered.Zip(ordered.Skip(1)).Count(p => p.First.Outcome != p.Second.Outcome);
            var last = ordered.Last(t => t.Outcome == "Failed");
            var defect = last.TestCaseId is { } tc ? open.FirstOrDefault(d => d.TestCaseId == tc) : null;
            result.Add(new RecurringFailureDto(last.TestCaseId, last.TestCaseId is { } id && numbers.TryGetValue(id, out var n) ? SequenceFormat.Format(n, 5, "TC-") : null,
                last.Identifier, last.DisplayName, ordered.Count, failures, streak, flips, last.CompletedAt!.Value, last.ErrorMessage ?? last.Reason,
                defect?.Id, defect is null ? null : Format(defect.Number)));
        }
        return result.OrderByDescending(r => r.ConsecutiveFailures).ThenByDescending(r => r.Failures).Take(200).ToList();
    }

    private async Task<List<TestExecution>> RecentAsync(Guid? productId, int days, CancellationToken ct, bool failedOnly)
    {
        var since = clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 365));
        var q = db.TestExecutions.AsNoTracking().Where(t => !t.IsSuperseded && t.CompletedAt >= since);
        q = failedOnly ? q.Where(t => t.Outcome == "Failed") : q.Where(t => t.Outcome == "Failed" || t.Outcome == "Passed");
        if (productId is { } pid) q = q.Where(t => t.FeatureId != null && db.Features.Any(f => f.Id == t.FeatureId && f.ProductId == pid));
        return await q.OrderByDescending(t => t.CompletedAt).Take(20_000).ToListAsync(ct);
    }

    // ---- automation after runs ----

    public async Task ProcessRunAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await db.ExecutionRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return;
        var runNumber = ExecutionService.Format(run.Number);
        var tests = await db.TestExecutions.AsNoTracking().Where(t => t.RunId == runId && !t.IsSuperseded).ToListAsync(ct);
        var recurred = new List<Defect>();

        // Recurrences: a failure with the signature of a known defect is another occurrence of it.
        var failed = tests.Where(t => t.Outcome == "Failed").Select(t => (t, Fp: FailureSignature.Fingerprint(t.TestCaseId, t.Identifier, t.ErrorMessage ?? t.Reason))).ToList();
        var fps = failed.Select(f => f.Fp).Distinct().ToList();
        var matches = await db.Defects.Where(d => d.Fingerprint != null && fps.Contains(d.Fingerprint) && d.Status != DefectStatus.Duplicate && d.Status != DefectStatus.Rejected)
            .OrderByDescending(d => d.Number).ToListAsync(ct);
        foreach (var (t, fp) in failed)
        {
            var defect = matches.FirstOrDefault(d => d.Fingerprint == fp && DefectWorkflow.Unresolved.Contains(d.Status)) ?? matches.FirstOrDefault(d => d.Fingerprint == fp);
            if (defect is null) continue;
            if (defect.Status is DefectStatus.Verified or DefectStatus.Closed)
            {
                AddEvent(defect.Id, "Status", defect.Status, DefectStatus.Reopened, $"The same failure recurred in {runNumber}.", SystemUser);
                defect.Status = DefectStatus.Reopened;
                recurred.Add(defect);
                await AttachAsync(defect, t, null, ct);
            }
            else if (defect.Status != DefectStatus.ReadyForRetest) await AttachAsync(defect, t, $"Failed again in {runNumber}.", ct);
        }

        // Retests: defects awaiting retest whose linked test case ran in this run (started after the fix was handed over).
        var executedCases = tests.Where(t => t.TestCaseId is not null && t.Outcome is "Passed" or "Failed").GroupBy(t => t.TestCaseId!.Value)
            .ToDictionary(g => g.Key, g => g.Any(t => t.Outcome == "Failed") ? g.First(t => t.Outcome == "Failed") : g.First());
        var caseIds = executedCases.Keys.ToList();
        var awaiting = await db.Defects.Where(d => d.Status == DefectStatus.ReadyForRetest && d.TestCaseId != null && caseIds.Contains(d.TestCaseId.Value)).ToListAsync(ct);
        foreach (var d in awaiting)
        {
            var handedOver = await db.DefectEvents.Where(e => e.DefectId == d.Id && e.ToStatus == DefectStatus.ReadyForRetest).MaxAsync(e => (DateTimeOffset?)e.Timestamp, ct);
            if (handedOver is { } h && run.CreatedAt < h) continue;
            var result = executedCases[d.TestCaseId!.Value];
            if (result.Outcome == "Passed")
            {
                d.VerificationResult = $"Automated retest passed in {runNumber}{(run.BuildVersion is { } b ? $" on build {b}" : "")}.";
                AddEvent(d.Id, "Status", d.Status, DefectStatus.Verified, d.VerificationResult, SystemUser);
                d.Status = DefectStatus.Verified;
            }
            else
            {
                AddEvent(d.Id, "Status", d.Status, DefectStatus.Reopened, $"Retest failed in {runNumber}: {Truncate(result.ErrorMessage ?? result.Reason, 500)}", SystemUser);
                d.Status = DefectStatus.Reopened;
                await AttachAsync(d, result, null, ct);
            }
        }
        await db.SaveChangesAsync(ct);

        foreach (var d in recurred)
            await notifications.PublishAsync(new NotificationMessage(NotificationEvents.DefectRecurred, NotificationSeverity.Warning,
                $"{Format(d.Number)} reopened: the failure recurred in {runNumber}", d.Title, $"/defects/{d.Id}", d.ProductId,
                new Dictionary<string, string> { ["defect"] = Format(d.Number), ["run"] = runNumber },
                d.AssignedTo is { } a && await users.FindByUserNameAsync(a, ct) is { } u ? [u.Id] : null), ct);
        if (matches.Count > 0 || awaiting.Count > 0) await notifier.NotifyAsync("defectsChanged", new { runId }, ct);
    }

    // ---- helpers ----

    private async Task<Defect> LoadAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Defects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Defect", id);
        if (d.ProductId is { } pid) await access.EnsureCanEditAsync(pid, ct);
        return d;
    }

    private void AddEvent(Guid defectId, string kind, DefectStatus? from, DefectStatus? to, string? text, string? userName = null)
        => db.DefectEvents.Add(new DefectEvent { DefectId = defectId, Timestamp = clock.GetUtcNow(), UserName = userName ?? user.UserName, Kind = kind, FromStatus = from, ToStatus = to, Text = text });

    private async Task<Guid> ScopeIdAsync(CancellationToken ct)
        => await db.SequenceScopes.Where(s => s.Key == ScopeKey).Select(s => s.Id).FirstAsync(ct);

    private async Task<string> RunNumberAsync(Guid runId, CancellationToken ct)
        => ExecutionService.Format(await db.ExecutionRuns.Where(r => r.Id == runId).Select(r => r.Number).FirstAsync(ct));

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..(max - 1)] + "…";
}
