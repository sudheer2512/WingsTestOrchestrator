using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Features;
using Wings.Domain.Testing;

namespace Wings.Application.Testing;

public interface ITestCaseService
{
    Task<PagedResult<TestCaseSummaryDto>> SearchAsync(TestCaseFilter filter, PageRequest page, CancellationToken ct = default);
    Task<TestCaseDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<TestCaseDetailDto> CreateAsync(SaveTestCaseRequest request, CancellationToken ct = default);
    Task<TestCaseDetailDto> UpdateAsync(Guid id, SaveTestCaseRequest request, CancellationToken ct = default);
    Task<TestCaseDetailDto> CloneAsync(Guid id, CloneRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<BulkResult> BulkUpdateAsync(BulkUpdateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TestCaseVersionDto>> VersionsAsync(Guid id, CancellationToken ct = default);
    Task<TestCaseDetailDto> SubmitForReviewAsync(Guid id, CancellationToken ct = default);
    Task<TestCaseDetailDto> ApproveAsync(Guid id, ReviewDecision decision, CancellationToken ct = default);
    Task<TestCaseDetailDto> RejectAsync(Guid id, ReviewDecision decision, CancellationToken ct = default);
    Task<TestCaseDependencyDto> AddDependencyAsync(Guid id, TestCaseDependencyUpsert request, CancellationToken ct = default);
    Task RemoveDependencyAsync(Guid id, Guid dependencyId, CancellationToken ct = default);
    Task<(byte[] Content, string ContentType, string FileName)> ExportAsync(TestCaseFilter filter, string format, CancellationToken ct = default);
    Task<ImportResult> ImportAsync(Stream content, string format, Guid? folderId, CancellationToken ct = default);
}

public sealed class TestCaseService(IWingsDbContext db, ISequenceAllocator sequences, ICurrentUser user, IProductAccess access,
    TimeProvider clock, IEnumerable<ITabularCodec> codecs, Quality.INotificationPublisher notifications) : ITestCaseService
{
    public const string ScopeKey = "TESTCASE";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult<TestCaseSummaryDto>> SearchAsync(TestCaseFilter filter, PageRequest page, CancellationToken ct = default)
    {
        var q = await FilterAsync(filter, ct);
        var total = await q.CountAsync(ct);
        q = (filter.SortBy?.ToLowerInvariant(), filter.Descending) switch
        {
            ("name", false) => q.OrderBy(t => t.Name),
            ("name", true) => q.OrderByDescending(t => t.Name),
            ("priority", false) => q.OrderBy(t => t.Priority).ThenBy(t => t.Number),
            ("priority", true) => q.OrderByDescending(t => t.Priority).ThenBy(t => t.Number),
            ("modified", _) => q.OrderByDescending(t => t.ModifiedAt ?? t.CreatedAt),
            (_, true) => q.OrderByDescending(t => t.Number),
            _ => q.OrderBy(t => t.Number),
        };
        var scope = await ScopeAsync(ct);
        var items = WithNumbers(await Summaries(q.Skip(page.Skip).Take(page.SafePageSize), scope).ToListAsync(ct), scope);
        return new PagedResult<TestCaseSummaryDto>(items, total, page.SafePage, page.SafePageSize);
    }

    internal static List<TestCaseSummaryDto> WithNumbers(List<TestCaseSummaryDto> items, SequenceScope scope)
        => items.Select(i => i with { NumberDisplay = SequenceFormat.Format(i.Number, scope.DisplayWidth, scope.Prefix) }).ToList();

    internal IQueryable<TestCaseSummaryDto> Summaries(IQueryable<TestCase> q, SequenceScope scope) => q.Select(t => new TestCaseSummaryDto(
        t.Id, t.Number, "", t.Name, t.TestTypeCode, t.Platform, t.FrameworkCode, t.Priority, t.FailureSeverity,
        t.AutomationStatus, t.ReviewStatus, t.Revision, t.OwnerUserName, t.FeatureMappings.Count, t.Tags.Select(x => x.Tag).ToList(),
        t.FolderId, t.IsTemplate, t.IsSharedSteps, t.CreatedAt, t.ModifiedAt));

    public async Task<TestCaseDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.TestCases.AsNoTracking().Include(x => x.Steps).Include(x => x.Tags).Include(x => x.RequirementLinks)
                    .Include(x => x.FeatureMappings).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Test case", id);
        var scope = await ScopeAsync(ct);
        var featureIds = t.FeatureMappings.Select(m => m.FeatureId).ToList();
        var visible = await access.VisibleProductIdsAsync(ct);
        var features = await db.Features.AsNoTracking().Where(f => featureIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name, f.SequenceNumber, f.ProductId, ProductName = f.Product!.Name, f.Product.SequenceScope!.DisplayWidth, f.Product.SequenceScope.Prefix })
            .ToListAsync(ct);
        if (visible is not null && features.Count > 0 && features.All(f => !visible.Contains(f.ProductId))) throw new NotFoundException("Test case", id);

        var reqIds = t.RequirementLinks.Select(r => r.RequirementId).ToList();
        var reqs = await db.FeatureRequirements.AsNoTracking().Where(r => reqIds.Contains(r.Id))
            .Join(db.Features, r => r.FeatureId, f => f.Id, (r, f) => new LinkedRequirementDto(r.Id, r.Code, r.Title, f.Id, f.Name)).ToListAsync(ct);
        var deps = await db.TestCaseDependencies.AsNoTracking().Where(d => d.TestCaseId == id)
            .Select(d => new { d.Id, d.DependsOnTestCaseId, d.DependsOnTestCase!.Number, d.DependsOnTestCase.Name, d.Type, d.Notes })
            .ToListAsync(ct);
        var automation = await db.TestPackageTests.AsNoTracking().Where(pt => pt.TestCaseId == id)
            .Join(db.TestPackageVersions, pt => pt.VersionId, v => v.Id, (pt, v) => new { pt, v })
            .Select(x => new LinkedAutomationDto(x.v.PackageId, x.v.Package!.Name, x.v.VersionNumber, x.v.Status, x.pt.Identifier, x.pt.FilePath, x.pt.Line))
            .ToListAsync(ct);
        var suites = await db.TestSuiteItems.AsNoTracking().Where(i => i.TestCaseId == id)
            .Join(db.TestSuites, i => i.SuiteId, s => s.Id, (i, s) => new { s.Id, s.Number, s.Name }).ToListAsync(ct);
        var suiteScope = await db.SequenceScopes.AsNoTracking().FirstAsync(s => s.Key == TestSuiteService.ScopeKey, ct);
        var sharedIds = t.Steps.Where(s => s.SharedStepsTestCaseId != null).Select(s => s.SharedStepsTestCaseId!.Value).ToList();
        var sharedNames = await db.TestCases.AsNoTracking().Where(x => sharedIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return new TestCaseDetailDto
        {
            Id = t.Id, Number = t.Number, NumberDisplay = SequenceFormat.Format(t.Number, scope.DisplayWidth, scope.Prefix), Revision = t.Revision,
            ReviewStatus = t.ReviewStatus, ReviewComment = t.ReviewComment, ApprovedBy = t.ApprovedBy, ApprovedAt = t.ApprovedAt, Version = t.Version,
            CreatedAt = t.CreatedAt, CreatedBy = t.CreatedBy, ModifiedAt = t.ModifiedAt, ModifiedBy = t.ModifiedBy,
            Fields = ToFields(t, sharedNames),
            Features = features.Select(f => new LinkedFeatureDto(f.Id, f.Name, SequenceFormat.Format(f.SequenceNumber, f.DisplayWidth, f.Prefix), f.ProductName,
                t.FeatureMappings.Any(m => m.FeatureId == f.Id && m.IsPrimary))).OrderByDescending(f => f.IsPrimary).ThenBy(f => f.Name).ToList(),
            Requirements = reqs,
            Dependencies = deps.Select(d => new TestCaseDependencyDto(d.Id, d.DependsOnTestCaseId, SequenceFormat.Format(d.Number, scope.DisplayWidth, scope.Prefix), d.Name, d.Type, d.Notes)).ToList(),
            Automation = automation,
            Suites = suites.Select(s => new SuiteRefDto(s.Id, SequenceFormat.Format(s.Number, suiteScope.DisplayWidth, suiteScope.Prefix), s.Name)).ToList(),
        };
    }

    public async Task<TestCaseDetailDto> CreateAsync(SaveTestCaseRequest request, CancellationToken ct = default)
    {
        await ValidateAsync(request.Fields, null, ct);
        var id = await db.InTransactionAsync(async () =>
        {
            var scope = await ScopeAsync(ct);
            var tc = new TestCase { Number = await sequences.NextAsync(scope.Id, ct), Revision = 1, ReviewStatus = ReviewStatus.Draft };
            Apply(tc, request.Fields);
            db.TestCases.Add(tc);
            Snapshot(tc, request.ChangeSummary ?? "Created");
            await db.SaveChangesAsync(ct);
            return tc.Id;
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TestCaseDetailDto> UpdateAsync(Guid id, SaveTestCaseRequest request, CancellationToken ct = default)
    {
        var tc = await LoadAsync(id, ct);
        await ValidateAsync(request.Fields, tc, ct);
        if (request.Version is { } v) db.SetOriginalVersion(tc, v);
        ReplaceChildren(tc);
        Apply(tc, request.Fields);
        tc.Revision++;
        // Any content change on an approved test case requires a fresh approval.
        if (tc.ReviewStatus is ReviewStatus.Approved or ReviewStatus.InReview) { tc.ReviewStatus = ReviewStatus.Draft; tc.ApprovedAt = null; tc.ApprovedBy = null; }
        Snapshot(tc, request.ChangeSummary);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TestCaseDetailDto> CloneAsync(Guid id, CloneRequest request, CancellationToken ct = default)
    {
        var source = await GetAsync(id, ct);
        var fields = source.Fields with
        {
            Name = request.NewName.Clean() ?? $"{source.Fields.Name} (copy)",
            IsTemplate = request.AsTemplate,
            FeatureIds = request.KeepFeatureLinks ? source.Fields.FeatureIds : [],
            PrimaryFeatureId = request.KeepFeatureLinks ? source.Fields.PrimaryFeatureId : null,
            RequirementIds = request.KeepFeatureLinks ? source.Fields.RequirementIds : [],
        };
        return await CreateAsync(new SaveTestCaseRequest(fields, ChangeSummary: $"Cloned from {source.NumberDisplay} r{source.Revision}"), ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tc = await LoadAsync(id, ct);
        if (await db.TestSteps.AnyAsync(s => s.SharedStepsTestCaseId == id, ct))
            throw new ConflictException("Other test cases call these shared steps. Remove those references first.");
        tc.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<BulkResult> BulkUpdateAsync(BulkUpdateRequest request, CancellationToken ct = default)
    {
        if (request.Ids.Count is 0 or > 1000) throw new ValidationException("ids", "Select between 1 and 1000 test cases.");
        var errors = new List<string>();
        if (request.TestTypeCode is { } tt && !await db.TestTypes.AnyAsync(x => x.Code == tt, ct)) throw new ValidationException("testTypeCode", $"Unknown test type '{tt}'.");
        if (request.FolderId is { } fid && !await db.TestFolders.AnyAsync(x => x.Id == fid, ct)) throw new ValidationException("folderId", "Unknown folder.");
        var addFeatures = request.AddFeatureIds ?? [];
        if (addFeatures.Count > 0 && await db.Features.CountAsync(f => addFeatures.Contains(f.Id), ct) != addFeatures.Distinct().Count())
            throw new ValidationException("addFeatureIds", "One or more features do not exist.");

        var cases = await db.TestCases.Include(x => x.Tags).Include(x => x.FeatureMappings).Include(x => x.Steps).Include(x => x.RequirementLinks)
            .Where(x => request.Ids.Contains(x.Id)).AsSplitQuery().ToListAsync(ct);
        foreach (var missing in request.Ids.Except(cases.Select(c => c.Id))) errors.Add($"Test case {missing} not found.");
        foreach (var tc in cases)
        {
            if (request.Priority.Clean() is { } p) tc.Priority = p;
            if (request.FailureSeverity.Clean() is { } s) tc.FailureSeverity = s;
            if (request.OwnerUserName is not null) tc.OwnerUserName = request.OwnerUserName.Clean();
            if (request.AutomationStatus is { } a) tc.AutomationStatus = a;
            if (request.TestTypeCode is { } t) tc.TestTypeCode = t;
            if (request.ClearFolder) tc.FolderId = null; else if (request.FolderId is { } f) tc.FolderId = f;
            foreach (var tag in NormalizeTags(request.AddTags ?? []).Where(tag => tc.Tags.All(x => x.Tag != tag))) tc.Tags.Add(new TestCaseTag { TestCaseId = tc.Id, Tag = tag });
            foreach (var tag in NormalizeTags(request.RemoveTags ?? [])) tc.Tags.RemoveAll(x => x.Tag == tag);
            foreach (var fid2 in addFeatures.Where(fid2 => tc.FeatureMappings.All(m => m.FeatureId != fid2))) tc.FeatureMappings.Add(new TestCaseFeatureMapping { TestCaseId = tc.Id, FeatureId = fid2 });
            tc.Revision++;
            Snapshot(tc, "Bulk update");
        }
        db.PendingAuditAction = "BulkUpdated";
        await db.SaveChangesAsync(ct);
        return new BulkResult(cases.Count, errors);
    }

    public async Task<IReadOnlyList<TestCaseVersionDto>> VersionsAsync(Guid id, CancellationToken ct = default)
    {
        await GetAsync(id, ct); // existence + access
        return await db.TestCaseVersions.AsNoTracking().Where(v => v.TestCaseId == id).OrderByDescending(v => v.Revision)
            .Select(v => new TestCaseVersionDto(v.Revision, v.ChangeSummary, v.CreatedAt, v.CreatedBy, v.SnapshotJson)).ToListAsync(ct);
    }

    public async Task<TestCaseDetailDto> SubmitForReviewAsync(Guid id, CancellationToken ct = default)
    {
        var tc = await LoadAsync(id, ct);
        if (tc.ReviewStatus is not (ReviewStatus.Draft or ReviewStatus.Rejected)) throw new ConflictException($"A {tc.ReviewStatus} test case cannot be submitted for review.");
        tc.ReviewStatus = ReviewStatus.InReview;
        db.PendingAuditAction = "SubmittedForReview";
        await db.SaveChangesAsync(ct);
        var productId = await db.TestCaseFeatureMappings.Where(m => m.TestCaseId == id).Select(m => (Guid?)m.Feature!.ProductId).FirstOrDefaultAsync(ct);
        await notifications.PublishAsync(new Quality.NotificationMessage(Quality.NotificationEvents.ApprovalRequired, Quality.NotificationSeverity.Info,
            $"Review requested: {SequenceFormat.Format(tc.Number, 5, "TC-")} {tc.Name}", $"{user.UserName} submitted revision {tc.Revision} for review.", $"/test-cases/{id}", productId), ct);
        return await GetAsync(id, ct);
    }

    public async Task<TestCaseDetailDto> ApproveAsync(Guid id, ReviewDecision decision, CancellationToken ct = default)
    {
        var tc = await LoadAsync(id, ct);
        if (tc.ReviewStatus != ReviewStatus.InReview) throw new ConflictException("Only test cases in review can be approved.");
        var author = await db.TestCaseVersions.Where(v => v.TestCaseId == id && v.Revision == tc.Revision).Select(v => v.CreatedBy).FirstOrDefaultAsync(ct);
        if (string.Equals(author, user.UserName, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Test cases must be approved by someone other than their author.");
        tc.ReviewStatus = ReviewStatus.Approved;
        tc.ApprovedBy = user.UserName;
        tc.ApprovedAt = clock.GetUtcNow();
        tc.ReviewComment = decision.Comment.Clean();
        db.PendingAuditAction = "Approved";
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TestCaseDetailDto> RejectAsync(Guid id, ReviewDecision decision, CancellationToken ct = default)
    {
        var tc = await LoadAsync(id, ct);
        if (tc.ReviewStatus != ReviewStatus.InReview) throw new ConflictException("Only test cases in review can be rejected.");
        new Validator().Required(decision.Comment, "comment", 4000).ThrowIfInvalid();
        tc.ReviewStatus = ReviewStatus.Rejected;
        tc.ReviewComment = decision.Comment!.Trim();
        db.PendingAuditAction = "Rejected";
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TestCaseDependencyDto> AddDependencyAsync(Guid id, TestCaseDependencyUpsert request, CancellationToken ct = default)
    {
        var tc = await LoadAsync(id, ct);
        var target = await db.TestCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.DependsOnTestCaseId, ct) ?? throw new NotFoundException("Test case", request.DependsOnTestCaseId);
        if (await db.TestCaseDependencies.AnyAsync(d => d.TestCaseId == id && d.DependsOnTestCaseId == target.Id, ct))
            throw new ValidationException("dependsOnTestCaseId", "This dependency already exists.");
        var edges = await db.TestCaseDependencies.Select(d => new { d.TestCaseId, d.DependsOnTestCaseId }).ToListAsync(ct);
        if (DependencyGraph.WouldCreateCycle(id, target.Id, edges.Select(e => (e.TestCaseId, e.DependsOnTestCaseId))))
            throw new ValidationException("dependsOnTestCaseId", $"Adding this dependency would create a circular dependency between '{tc.Name}' and '{target.Name}'.");
        var d = new TestCaseDependency { TestCaseId = id, DependsOnTestCaseId = target.Id, Type = request.Type, Notes = request.Notes.Clean() };
        db.TestCaseDependencies.Add(d);
        await db.SaveChangesAsync(ct);
        var scope = await ScopeAsync(ct);
        return new TestCaseDependencyDto(d.Id, target.Id, SequenceFormat.Format(target.Number, scope.DisplayWidth, scope.Prefix), target.Name, d.Type, d.Notes);
    }

    public async Task RemoveDependencyAsync(Guid id, Guid dependencyId, CancellationToken ct = default)
    {
        await LoadAsync(id, ct);
        var d = await db.TestCaseDependencies.FirstOrDefaultAsync(x => x.Id == dependencyId && x.TestCaseId == id, ct) ?? throw new NotFoundException("Dependency", dependencyId);
        db.TestCaseDependencies.Remove(d);
        await db.SaveChangesAsync(ct);
    }

    // ---- Import / export ----------------------------------------------------------------------------------------

    private static readonly string[] Headers = ["Number", "Name", "Description", "Objective", "Type", "Platform", "Framework", "Priority", "Severity",
        "Preconditions", "TestData", "Steps", "ExpectedResults", "Cleanup", "EstimatedMinutes", "Automation", "Owner", "Tags", "Features", "Review", "Revision"];

    public async Task<(byte[] Content, string ContentType, string FileName)> ExportAsync(TestCaseFilter filter, string format, CancellationToken ct = default)
    {
        var codec = Codec(format);
        var scope = await ScopeAsync(ct);
        var q = await FilterAsync(filter, ct);
        var cases = await q.AsNoTracking().Include(t => t.Steps).Include(t => t.Tags).Include(t => t.FeatureMappings).AsSplitQuery()
            .OrderBy(t => t.Number).Take(50_000).ToListAsync(ct);
        var featureIds = cases.SelectMany(c => c.FeatureMappings.Select(m => m.FeatureId)).Distinct().ToList();
        var featureRefs = await db.Features.AsNoTracking().Where(f => featureIds.Contains(f.Id))
            .Select(f => new { f.Id, Ref = f.Product!.Code + ":" + f.SequenceNumber }).ToDictionaryAsync(f => f.Id, f => f.Ref, ct);
        var rows = cases.Select(t => (IReadOnlyList<string?>)
        [
            SequenceFormat.Format(t.Number, scope.DisplayWidth, scope.Prefix), t.Name, t.Description, t.Objective, t.TestTypeCode, t.Platform.ToString(), t.FrameworkCode,
            t.Priority, t.FailureSeverity, t.Preconditions, t.TestDataRequirements,
            string.Join("\n", t.Steps.OrderBy(s => s.StepNumber).Select(s => $"{s.StepNumber}. {s.Action}" + (s.ExpectedResult is null ? "" : $" => {s.ExpectedResult}"))),
            t.ExpectedResults, t.CleanupSteps, t.EstimatedDurationMinutes?.ToString(), t.AutomationStatus.ToString(), t.OwnerUserName,
            string.Join(";", t.Tags.Select(x => x.Tag)), string.Join(";", t.FeatureMappings.Select(m => featureRefs.GetValueOrDefault(m.FeatureId))),
            t.ReviewStatus.ToString(), t.Revision.ToString(),
        ]);
        return (codec.Write(Headers, rows, "TestCases"), codec.ContentType, $"test-cases.{codec.Format}");
    }

    /// <summary>Creates test cases from rows (same columns as export). Features are referenced as PRODUCTCODE:sequence. All-or-nothing.</summary>
    public async Task<ImportResult> ImportAsync(Stream content, string format, Guid? folderId, CancellationToken ct = default)
    {
        var rows = Codec(format).Read(content);
        var errors = new List<string>();
        var requests = new List<TestCaseFields>();
        var types = await db.TestTypes.Select(t => t.Code).ToListAsync(ct);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string? Get(string k) => r.TryGetValue(k, out var v) ? v.Clean() : null;
            var line = $"Row {i + 2}";
            if (Get("Name") is not { } name) { errors.Add($"{line}: Name is required."); continue; }
            var type = Get("Type")?.ToUpperInvariant() ?? TestTypes.Functional;
            if (!types.Contains(type)) { errors.Add($"{line}: unknown test type '{type}'."); continue; }
            var featureIds = new List<Guid>();
            foreach (var reference in (Get("Features") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = reference.Split(':');
                if (parts.Length != 2 || !long.TryParse(parts[1].Trim('[', ']'), out var seq)) { errors.Add($"{line}: feature reference '{reference}' must be PRODUCTCODE:sequence."); continue; }
                var code = parts[0].ToUpperInvariant();
                var fid = await db.Features.Where(f => f.Product!.Code == code && f.SequenceNumber == seq).Select(f => (Guid?)f.Id).FirstOrDefaultAsync(ct);
                if (fid is null) errors.Add($"{line}: feature '{reference}' not found."); else featureIds.Add(fid.Value);
            }
            requests.Add(new TestCaseFields
            {
                Name = name, Description = Get("Description"), Objective = Get("Objective"), TestTypeCode = type,
                Platform = Enum.TryParse<ApplicationType>(Get("Platform"), true, out var pl) ? pl : ApplicationType.None,
                FrameworkCode = Get("Framework")?.ToUpperInvariant(), Priority = Get("Priority") ?? "P2", FailureSeverity = Get("Severity")?.ToUpperInvariant() ?? "MAJOR",
                Preconditions = Get("Preconditions"), TestDataRequirements = Get("TestData"), ExpectedResults = Get("ExpectedResults"), CleanupSteps = Get("Cleanup"),
                EstimatedDurationMinutes = int.TryParse(Get("EstimatedMinutes"), out var m) ? m : null,
                AutomationStatus = Enum.TryParse<AutomationStatus>(Get("Automation"), true, out var a) ? a : AutomationStatus.Manual,
                OwnerUserName = Get("Owner"), FolderId = folderId, Steps = ParseSteps(Get("Steps")),
                Tags = (Get("Tags") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), FeatureIds = featureIds,
            });
        }
        if (errors.Count > 0) return new ImportResult(0, errors);
        foreach (var f in requests) await ValidateAsync(f, null, ct);
        await db.InTransactionAsync(async () =>
        {
            var scope = await ScopeAsync(ct);
            foreach (var f in requests)
            {
                var tc = new TestCase { Number = await sequences.NextAsync(scope.Id, ct) };
                Apply(tc, f);
                db.TestCases.Add(tc);
                Snapshot(tc, "Imported");
            }
            db.PendingAuditAction = "Imported";
            await db.SaveChangesAsync(ct);
        }, ct);
        return new ImportResult(requests.Count, []);
    }

    internal static IReadOnlyList<TestStepDto> ParseSteps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var steps = new List<TestStepDto>();
        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = System.Text.RegularExpressions.Regex.Replace(raw, @"^\d+[\.\)]\s*", "");
            var parts = line.Split("=>", 2, StringSplitOptions.TrimEntries);
            steps.Add(new TestStepDto(steps.Count + 1, parts[0], parts.Length > 1 ? parts[1] : null, null, null));
        }
        return steps;
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private async Task<IQueryable<TestCase>> FilterAsync(TestCaseFilter filter, CancellationToken ct)
    {
        var q = db.TestCases.AsQueryable();
        var visible = await access.VisibleProductIdsAsync(ct);
        if (visible is not null)
            q = q.Where(t => !t.FeatureMappings.Any() || t.FeatureMappings.Any(m => visible.Contains(m.Feature!.ProductId)));
        if (filter.Text.Clean() is { } text)
        {
            var digits = text.ToUpperInvariant().Replace("TC-", "").TrimStart('0');
            q = long.TryParse(digits, out var n)
                ? q.Where(t => t.Number == n || EF.Functions.Like(t.Name, Search.Like(text), Search.Escape))
                : q.Where(t => EF.Functions.Like(t.Name, Search.Like(text), Search.Escape) || (t.Description != null && EF.Functions.Like(t.Description, Search.Like(text), Search.Escape)) || t.Tags.Any(x => x.Tag == text.ToLower()));
        }
        if (filter.FeatureId is { } fid)
        {
            if (filter.IncludeSubfeatures)
            {
                var root = await db.Features.AsNoTracking().Where(f => f.Id == fid).Select(f => new { f.Path, f.SequenceScopeId }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Feature", fid);
                q = q.Where(t => t.FeatureMappings.Any(m => m.Feature!.SequenceScopeId == root.SequenceScopeId && m.Feature.Path.StartsWith(root.Path)));
            }
            else q = q.Where(t => t.FeatureMappings.Any(m => m.FeatureId == fid));
        }
        if (filter.FolderId is { } folder) q = q.Where(t => t.FolderId == folder);
        if (filter.TestTypeCode.Clean() is { } type) q = q.Where(t => t.TestTypeCode == type);
        if (filter.AutomationStatus is { } a) q = q.Where(t => t.AutomationStatus == a);
        if (filter.ReviewStatus is { } r) q = q.Where(t => t.ReviewStatus == r);
        if (filter.Priority.Clean() is { } p) q = q.Where(t => t.Priority == p);
        if (filter.Tag.Clean() is { } tag) { var tg = tag.ToLowerInvariant(); q = q.Where(t => t.Tags.Any(x => x.Tag == tg)); }
        if (filter.Platform is { } pl) q = q.Where(t => t.Platform == pl);
        if (filter.IsTemplate is { } it) q = q.Where(t => t.IsTemplate == it);
        if (filter.IsSharedSteps is { } ss) q = q.Where(t => t.IsSharedSteps == ss);
        if (filter.OwnerUserName.Clean() is { } owner) q = q.Where(t => t.OwnerUserName == owner);
        if (filter.SuiteId is { } suite) q = q.Where(t => db.TestSuiteItems.Any(i => i.SuiteId == suite && i.TestCaseId == t.Id));
        return q;
    }

    private async Task<TestCase> LoadAsync(Guid id, CancellationToken ct)
    {
        var tc = await db.TestCases.Include(x => x.Steps).Include(x => x.Tags).Include(x => x.FeatureMappings).Include(x => x.RequirementLinks)
                     .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Test case", id);
        var productIds = await db.Features.Where(f => tc.FeatureMappings.Select(m => m.FeatureId).Contains(f.Id)).Select(f => f.ProductId).Distinct().ToListAsync(ct);
        foreach (var pid in productIds) await access.EnsureCanEditAsync(pid, ct);
        return tc;
    }

    private async Task<SequenceScope> ScopeAsync(CancellationToken ct)
        => await db.SequenceScopes.AsNoTracking().FirstOrDefaultAsync(s => s.Key == ScopeKey, ct)
           ?? throw new InvalidOperationException("The TESTCASE sequence scope is missing; run the database seeder.");

    private async Task ValidateAsync(TestCaseFields f, TestCase? existing, CancellationToken ct)
    {
        var v = new Validator().Required(f.Name, "name", 300).MaxLength(f.Priority, "priority", 20).MaxLength(f.FailureSeverity, "failureSeverity", 20)
            .Must(f.EstimatedDurationMinutes is null or >= 0, "estimatedDurationMinutes", "Duration cannot be negative.")
            .Must(f.Steps.All(s => !string.IsNullOrWhiteSpace(s.Action) || s.SharedStepsTestCaseId is not null), "steps", "Every step needs an action or a shared-steps reference.")
            .Must(f.PrimaryFeatureId is null || f.FeatureIds.Contains(f.PrimaryFeatureId.Value), "primaryFeatureId", "The primary feature must be one of the linked features.");
        if (!await db.TestTypes.AnyAsync(t => t.Code == f.TestTypeCode, ct)) v.Add("testTypeCode", $"Unknown test type '{f.TestTypeCode}'.");
        if (f.FrameworkCode is { } fw && !await db.TestingFrameworks.AnyAsync(t => t.Code == fw, ct)) v.Add("frameworkCode", $"Unknown framework '{fw}'.");
        if (f.FolderId is { } folder && !await db.TestFolders.AnyAsync(x => x.Id == folder, ct)) v.Add("folderId", "Unknown folder.");
        var featureIds = f.FeatureIds.Distinct().ToList();
        var features = await db.Features.Where(x => featureIds.Contains(x.Id)).Select(x => new { x.Id, x.ProductId }).ToListAsync(ct);
        if (features.Count != featureIds.Count) v.Add("featureIds", "One or more linked features do not exist.");
        var reqIds = f.RequirementIds.Distinct().ToList();
        if (reqIds.Count > 0 && await db.FeatureRequirements.CountAsync(r => reqIds.Contains(r.Id) && featureIds.Contains(r.FeatureId), ct) != reqIds.Count)
            v.Add("requirementIds", "Requirements must belong to one of the linked features.");
        try { Orchestration.TestDataService.Parse(f.DataRequirements); } catch (ValidationException ex) { v.Add("dataRequirements", ex.Message); }
        v.MaxLength(f.ResourceKeys, "resourceKeys", 400);
        if (f.ParametersJson.Clean() is { } pj)
        {
            try { using var doc = JsonDocument.Parse(pj); if (doc.RootElement.ValueKind != JsonValueKind.Array) v.Add("parametersJson", "Parameters must be a JSON array of parameter sets."); }
            catch (JsonException) { v.Add("parametersJson", "Parameters are not valid JSON."); }
        }
        var shared = f.Steps.Where(s => s.SharedStepsTestCaseId is not null).Select(s => s.SharedStepsTestCaseId!.Value).Distinct().ToList();
        if (existing is not null && shared.Contains(existing.Id)) v.Add("steps", "A test case cannot call its own steps.");
        if (shared.Count > 0 && await db.TestCases.CountAsync(t => shared.Contains(t.Id) && t.IsSharedSteps, ct) != shared.Count)
            v.Add("steps", "Shared-step references must point to test cases marked as shared steps.");
        v.ThrowIfInvalid();
        foreach (var pid in features.Select(x => x.ProductId).Distinct()) await access.EnsureCanEditAsync(pid, ct);
    }

    private void ReplaceChildren(TestCase tc)
    {
        db.TestSteps.RemoveRange(tc.Steps);
        tc.Steps.Clear();
        db.TestCaseTags.RemoveRange(tc.Tags);
        tc.Tags.Clear();
        db.TestCaseFeatureMappings.RemoveRange(tc.FeatureMappings);
        tc.FeatureMappings.Clear();
        db.TestCaseRequirementLinks.RemoveRange(tc.RequirementLinks);
        tc.RequirementLinks.Clear();
    }

    private static void Apply(TestCase tc, TestCaseFields f)
    {
        tc.Name = f.Name.Trim(); tc.Description = f.Description.Clean(); tc.Objective = f.Objective.Clean(); tc.TestTypeCode = f.TestTypeCode;
        tc.Platform = f.Platform; tc.FrameworkCode = f.FrameworkCode.Clean(); tc.Priority = f.Priority; tc.FailureSeverity = f.FailureSeverity;
        tc.Preconditions = f.Preconditions.Clean(); tc.TestDataRequirements = f.TestDataRequirements.Clean(); tc.ExpectedResults = f.ExpectedResults.Clean();
        tc.CleanupSteps = f.CleanupSteps.Clean(); tc.EstimatedDurationMinutes = f.EstimatedDurationMinutes; tc.AutomationStatus = f.AutomationStatus;
        tc.OwnerUserName = f.OwnerUserName.Clean(); tc.FolderId = f.FolderId; tc.IsTemplate = f.IsTemplate; tc.IsSharedSteps = f.IsSharedSteps;
        tc.ParametersJson = f.ParametersJson.Clean();
        tc.IsDestructive = f.IsDestructive;
        tc.ResourceKeys = f.ResourceKeys.Clean() is { } rk ? string.Join(",", rk.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(k => k.ToLowerInvariant()).Distinct()) : null;
        tc.DataRequirements = f.DataRequirements.Clean() is { } dr ? string.Join(",", Orchestration.TestDataService.Parse(dr).Select(d => d.Count == 1 ? d.Code : $"{d.Code}:{d.Count}")) : null;
        var n = 1;
        foreach (var s in f.Steps)
            tc.Steps.Add(new TestStep { TestCaseId = tc.Id, StepNumber = n++, Action = s.Action?.Trim() ?? "", ExpectedResult = s.ExpectedResult.Clean(), TestData = s.TestData.Clean(), SharedStepsTestCaseId = s.SharedStepsTestCaseId });
        foreach (var tag in NormalizeTags(f.Tags)) tc.Tags.Add(new TestCaseTag { TestCaseId = tc.Id, Tag = tag });
        foreach (var fid in f.FeatureIds.Distinct()) tc.FeatureMappings.Add(new TestCaseFeatureMapping { TestCaseId = tc.Id, FeatureId = fid, IsPrimary = fid == f.PrimaryFeatureId });
        foreach (var rid in f.RequirementIds.Distinct()) tc.RequirementLinks.Add(new TestCaseRequirementLink { TestCaseId = tc.Id, RequirementId = rid });
    }

    private static IEnumerable<string> NormalizeTags(IEnumerable<string> tags)
        => tags.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length is > 0 and <= 60).Distinct();

    private static TestCaseFields ToFields(TestCase t, IReadOnlyDictionary<Guid, string>? sharedNames = null) => new()
    {
        Name = t.Name, Description = t.Description, Objective = t.Objective, TestTypeCode = t.TestTypeCode, Platform = t.Platform, FrameworkCode = t.FrameworkCode,
        Priority = t.Priority, FailureSeverity = t.FailureSeverity, Preconditions = t.Preconditions, TestDataRequirements = t.TestDataRequirements,
        ExpectedResults = t.ExpectedResults, CleanupSteps = t.CleanupSteps, EstimatedDurationMinutes = t.EstimatedDurationMinutes, AutomationStatus = t.AutomationStatus,
        OwnerUserName = t.OwnerUserName, FolderId = t.FolderId, IsTemplate = t.IsTemplate, IsSharedSteps = t.IsSharedSteps, ParametersJson = t.ParametersJson,
        IsDestructive = t.IsDestructive, ResourceKeys = t.ResourceKeys, DataRequirements = t.DataRequirements,
        Steps = t.Steps.OrderBy(s => s.StepNumber).Select(s => new TestStepDto(s.StepNumber, s.Action, s.ExpectedResult, s.TestData, s.SharedStepsTestCaseId,
            s.SharedStepsTestCaseId is { } sid ? sharedNames?.GetValueOrDefault(sid) : null)).ToList(),
        FeatureIds = t.FeatureMappings.Select(m => m.FeatureId).ToList(), PrimaryFeatureId = t.FeatureMappings.FirstOrDefault(m => m.IsPrimary)?.FeatureId,
        Tags = t.Tags.Select(x => x.Tag).OrderBy(x => x).ToList(), RequirementIds = t.RequirementLinks.Select(r => r.RequirementId).ToList(),
    };

    private void Snapshot(TestCase tc, string? summary)
    {
        db.TestCaseVersions.Add(new TestCaseVersion
        {
            TestCaseId = tc.Id, Revision = tc.Revision, SnapshotJson = JsonSerializer.Serialize(ToFields(tc), Json),
            ChangeSummary = summary.Clean(), CreatedAt = clock.GetUtcNow(), CreatedBy = user.UserName,
        });
    }

    private ITabularCodec Codec(string format) => codecs.FirstOrDefault(c => c.Format.Equals(format, StringComparison.OrdinalIgnoreCase))
                                                  ?? throw new ValidationException("format", "Supported formats are csv and xlsx.");
}
