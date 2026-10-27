using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Features;
using Wings.Domain.Testing;

namespace Wings.Application.Features;

public interface IFeatureQueryService
{
    Task<IReadOnlyList<FeatureTreeNode>> GetChildrenAsync(Guid productId, Guid? parentId, CancellationToken ct = default);
    Task<PagedResult<FeatureSearchHit>> SearchAsync(FeatureFilter filter, PageRequest page, CancellationToken ct = default);
    Task<FeatureDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureBreadcrumb>> GetAncestorsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> FeatureTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> FeatureStatusesAsync(CancellationToken ct = default);
}

public sealed class FeatureQueryService(IWingsDbContext db, IProductAccess access, TimeProvider clock) : IFeatureQueryService
{
    public async Task<IReadOnlyList<FeatureTreeNode>> GetChildrenAsync(Guid productId, Guid? parentId, CancellationToken ct = default)
    {
        await access.EnsureCanViewAsync(productId, ct);
        var scope = await ScopeForProductAsync(productId, ct);
        var rows = await ProjectNodes(db.Features.Where(f => f.ProductId == productId && f.ParentId == parentId)
            .OrderBy(f => f.DisplayOrder).ThenBy(f => f.SequenceNumber)).ToListAsync(ct);
        return rows.Select(r => ToNode(r, scope)).ToList();
    }

    public async Task<PagedResult<FeatureSearchHit>> SearchAsync(FeatureFilter filter, PageRequest page, CancellationToken ct = default)
    {
        var visible = await access.VisibleProductIdsAsync(ct);
        var q = db.Features.Where(f => visible == null || visible.Contains(f.ProductId));

        if (filter.ProductId is { } pid) q = q.Where(f => f.ProductId == pid);
        if (filter.UnderFeatureId is { } under)
        {
            var root = await db.Features.AsNoTracking().Where(f => f.Id == under).Select(f => new { f.Path, f.SequenceScopeId }).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Feature", under);
            q = q.Where(f => f.SequenceScopeId == root.SequenceScopeId && f.Path.StartsWith(root.Path));
        }
        if (filter.Text.Clean() is { } text)
        {
            var seqText = text.TrimStart('[', '0').TrimEnd(']');
            if (long.TryParse(seqText, out var seq)) q = q.Where(f => f.SequenceNumber == seq || EF.Functions.Like(f.Name, Search.Like(text), Search.Escape));
            else q = q.Where(f => EF.Functions.Like(f.Name, Search.Like(text), Search.Escape) || (f.Description != null && EF.Functions.Like(f.Description, Search.Like(text), Search.Escape)) || (f.FormOrRoute != null && EF.Functions.Like(f.FormOrRoute, Search.Like(text), Search.Escape)) || (f.ClassName != null && EF.Functions.Like(f.ClassName, Search.Like(text), Search.Escape)));
        }
        if (filter.StatusCode.Clean() is { } status) q = q.Where(f => f.StatusCode == status);
        if (filter.FeatureTypeCode.Clean() is { } type) q = q.Where(f => f.FeatureTypeCode == type);
        if (filter.ApplicationType is { } app) q = q.Where(f => f.ApplicationType == app);
        if (filter.SequenceFrom is { } from) q = q.Where(f => f.SequenceNumber >= from);
        if (filter.SequenceTo is { } to) q = q.Where(f => f.SequenceNumber <= to);
        if (filter.CreatedAfter is { } after) q = q.Where(f => f.CreatedAt > after);
        if (filter.RecentDays is { } days) { var since = clock.GetUtcNow().AddDays(-days); q = q.Where(f => f.CreatedAt >= since); }
        if (filter.IntroducedInVersionId is { } rel) q = q.Where(f => f.IntroducedInVersionId == rel);
        if (!filter.IncludeDeprecated) q = q.Where(f => !f.IsDeprecated);
        if (filter.SinceLastRelease)
        {
            if (filter.ProductId is not { } p) throw new ValidationException("productId", "Select a product to filter by its last release.");
            var last = await db.ProductVersions.Where(v => v.ProductId == p && v.IsReleased && v.ReleaseDate != null).MaxAsync(v => v.ReleaseDate, ct);
            if (last is { } d)
            {
                var cutoff = new DateTimeOffset(d.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
                q = q.Where(f => f.CreatedAt > cutoff);
            }
        }
        if (filter.Untested) q = q.Where(f => !db.TestCases.Any(tc => tc.FeatureMappings.Any(m => m.FeatureId == f.Id)));
        if (filter.MissingAutomation)
            q = q.Where(f => !db.TestCases.Any(tc => tc.AutomationStatus == AutomationStatus.Automated && tc.FeatureMappings.Any(m => m.FeatureId == f.Id)));

        var total = await q.CountAsync(ct);
        var sorted = (filter.SortBy?.ToLowerInvariant(), filter.Descending) switch
        {
            ("name", false) => q.OrderBy(f => f.Name),
            ("name", true) => q.OrderByDescending(f => f.Name),
            ("created", false) => q.OrderBy(f => f.CreatedAt),
            ("created", true) => q.OrderByDescending(f => f.CreatedAt),
            ("tree", _) => q.OrderBy(f => f.ProductId).ThenBy(f => f.Path),
            (_, true) => q.OrderByDescending(f => f.SequenceNumber),
            _ => q.OrderBy(f => f.SequenceNumber),
        };
        var rows = await ProjectNodes(sorted.Skip(page.Skip).Take(page.SafePageSize)).ToListAsync(ct);

        var scopeIds = rows.Select(r => r.SequenceScopeId).Distinct().ToList();
        var scopes = await db.SequenceScopes.AsNoTracking().Where(s => scopeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var productNames = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var ancestors = await LoadAncestorsAsync(rows.Select(r => (r.SequenceScopeId, r.Path)).ToList(), scopes, ct);

        var hits = rows.Select(r => new FeatureSearchHit(ToNode(r, scopes[r.SequenceScopeId]), productNames.GetValueOrDefault(r.ProductId, ""),
            ancestors.GetValueOrDefault((r.SequenceScopeId, r.Path)) ?? [])).ToList();
        return new PagedResult<FeatureSearchHit>(hits, total, page.SafePage, page.SafePageSize);
    }

    public async Task<FeatureDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var f = await db.Features.AsNoTracking()
            .Include(x => x.Product).Include(x => x.IntroducedInVersion)
            .Include(x => x.Requirements).Include(x => x.SourceMappings).Include(x => x.Attachments)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Feature", id);
        await access.EnsureCanViewAsync(f.ProductId, ct);
        var scope = await db.SequenceScopes.AsNoTracking().SingleAsync(s => s.Id == f.SequenceScopeId, ct);

        var ancestors = (await LoadAncestorsAsync([(f.SequenceScopeId, f.Path)], new() { [scope.Id] = scope }, ct)).GetValueOrDefault((f.SequenceScopeId, f.Path)) ?? [];
        var deps = await db.FeatureDependencies.AsNoTracking().Where(d => d.FeatureId == id)
            .Select(d => new { d.Id, d.DependsOnFeatureId, d.DependsOnFeature!.Name, d.DependsOnFeature.SequenceNumber, d.Type, d.Notes }).ToListAsync(ct);
        var dependents = await db.FeatureDependencies.AsNoTracking().Where(d => d.DependsOnFeatureId == id)
            .Select(d => new { d.Id, d.FeatureId, d.Feature!.Name, d.Feature.SequenceNumber, d.Type, d.Notes }).ToListAsync(ct);

        var tests = await db.TestCases.AsNoTracking().Where(tc => tc.FeatureMappings.Any(m => m.FeatureId == id))
            .Select(tc => new { tc.AutomationStatus, tc.ReviewStatus }).ToListAsync(ct);
        var packaged = await db.TestPackageTests.CountAsync(t => t.FeatureId == id, ct);
        var automated = tests.Count(t => t.AutomationStatus == AutomationStatus.Automated);
        var tcIds = db.TestCaseFeatureMappings.Where(m => m.FeatureId == id).Select(m => m.TestCaseId);
        var mine = db.TestExecutions.Where(t => t.JobId != null && !t.IsSuperseded && (t.FeatureId == id || (t.TestCaseId != null && tcIds.Contains(t.TestCaseId.Value))));
        var last = await mine.Where(t => t.CompletedAt != null).OrderByDescending(t => t.CompletedAt).Select(t => new { t.RunId, t.CompletedAt }).FirstOrDefaultAsync(ct);
        string? lastResult = null;
        if (last is not null)
        {
            var outcomes = await mine.Where(t => t.RunId == last.RunId).GroupBy(t => t.Outcome).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            lastResult = string.Join(", ", outcomes.OrderByDescending(o => o.Count).Select(o => $"{o.Count} {o.Key}"));
        }
        var replacedBy = f.ReplacedByFeatureId is { } rid ? await db.Features.Where(x => x.Id == rid).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;

        return new FeatureDetailDto
        {
            Id = f.Id, ProductId = f.ProductId, ProductName = f.Product!.Name, ParentId = f.ParentId,
            ParentName = ancestors.Count > 1 ? ancestors[^2].Name : null,
            Ancestors = ancestors.Take(Math.Max(0, ancestors.Count - 1)).ToList(),
            SequenceNumber = f.SequenceNumber, SequenceDisplay = SequenceFormat.Format(f.SequenceNumber, scope.DisplayWidth, scope.Prefix),
            SequenceScopeKey = scope.Key, IsLegacySequence = f.IsLegacySequence, DisplayOrder = f.DisplayOrder, Version = f.Version,
            CreatedAt = f.CreatedAt, CreatedBy = f.CreatedBy, ModifiedAt = f.ModifiedAt, ModifiedBy = f.ModifiedBy,
            IntroducedInVersionLabel = f.IntroducedInVersion?.VersionLabel ?? f.OriginatingRelease,
            ReplacedByName = replacedBy,
            Fields = ToFields(f),
            Testing = new FeatureTestingSummary
            {
                TestCaseCount = tests.Count, AutomatedCount = automated, ManualCount = tests.Count - automated,
                ApprovedCount = tests.Count(t => t.ReviewStatus == ReviewStatus.Approved),
                DraftCount = tests.Count(t => t.ReviewStatus is ReviewStatus.Draft or ReviewStatus.InReview),
                PackagedTestCount = packaged, CoverageStatus = Coverage(tests.Count, automated),
                LastExecutedAt = last?.CompletedAt, LastResult = lastResult, LastRunId = last?.RunId,
            },
            Requirements = f.Requirements.OrderBy(r => r.Code).Select(r => new RequirementDto(r.Id, r.Code, r.Title, r.Description, r.Priority, r.ExternalLink, r.Version)).ToList(),
            SourceMappings = f.SourceMappings.OrderBy(m => m.Kind).ThenBy(m => m.Value).Select(m => new SourceMappingDto(m.Id, m.Kind, m.Value, m.Repository, m.Notes, m.IsApproved, m.CreatedBy)).ToList(),
            Attachments = f.Attachments.OrderByDescending(a => a.CreatedAt).Select(a => new AttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.ExternalUrl, a.Caption, a.CreatedAt, a.CreatedBy)).ToList(),
            Dependencies = deps.Select(d => new DependencyDto(d.Id, d.DependsOnFeatureId, d.Name, SequenceFormat.Format(d.SequenceNumber, scope.DisplayWidth, scope.Prefix), d.Type, d.Notes)).ToList(),
            Dependents = dependents.Select(d => new DependencyDto(d.Id, d.FeatureId, d.Name, SequenceFormat.Format(d.SequenceNumber, scope.DisplayWidth, scope.Prefix), d.Type, d.Notes)).ToList(),
        };
    }

    public async Task<IReadOnlyList<FeatureBreadcrumb>> GetAncestorsAsync(Guid id, CancellationToken ct = default)
    {
        var f = await db.Features.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.ProductId, x.SequenceScopeId, x.Path }).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Feature", id);
        await access.EnsureCanViewAsync(f.ProductId, ct);
        var scope = await db.SequenceScopes.AsNoTracking().SingleAsync(s => s.Id == f.SequenceScopeId, ct);
        return (await LoadAncestorsAsync([(f.SequenceScopeId, f.Path)], new() { [scope.Id] = scope }, ct)).GetValueOrDefault((f.SequenceScopeId, f.Path)) ?? [];
    }

    public async Task<IReadOnlyList<LookupItem>> FeatureTypesAsync(CancellationToken ct = default)
        => await db.FeatureTypes.OrderBy(t => t.SortOrder).Select(t => new LookupItem(t.Code, t.Name, t.SortOrder)).ToListAsync(ct);

    public async Task<IReadOnlyList<LookupItem>> FeatureStatusesAsync(CancellationToken ct = default)
        => await db.FeatureStatuses.OrderBy(t => t.SortOrder).Select(t => new LookupItem(t.Code, t.Name, t.SortOrder)).ToListAsync(ct);

    internal static string Coverage(int total, int automated) => total == 0 ? "NONE" : automated == 0 ? "MANUAL" : automated < total ? "PARTIAL" : "AUTOMATED";

    internal static FeatureFields ToFields(Feature f) => new()
    {
        Name = f.Name, FeatureTypeCode = f.FeatureTypeCode, StatusCode = f.StatusCode, ApplicationType = f.ApplicationType,
        Description = f.Description, BusinessObjective = f.BusinessObjective, FunctionalRequirements = f.FunctionalRequirements,
        CurrentVersion = f.CurrentVersion, IntroducedInVersionId = f.IntroducedInVersionId, AssemblyName = f.AssemblyName,
        Namespace = f.Namespace, ClassName = f.ClassName, FormOrRoute = f.FormOrRoute, RelatedServices = f.RelatedServices,
        ApiEndpoints = f.ApiEndpoints, DatabaseObjects = f.DatabaseObjects, SourceRepository = f.SourceRepository,
        BuildPackage = f.BuildPackage, EntryPoint = f.EntryPoint, RequiredPermissions = f.RequiredPermissions,
        Preconditions = f.Preconditions, InputParameters = f.InputParameters, ExpectedOutcomes = f.ExpectedOutcomes,
        BusinessRules = f.BusinessRules, ValidationRules = f.ValidationRules, ErrorConditions = f.ErrorConditions,
        UserRoles = f.UserRoles, KnownLimitations = f.KnownLimitations, IsDeprecated = f.IsDeprecated,
        ReplacedByFeatureId = f.ReplacedByFeatureId, DeprecationNote = f.DeprecationNote,
    };

    private sealed record NodeRow(Guid Id, Guid ProductId, Guid? ParentId, string Name, long SequenceNumber, Guid SequenceScopeId, string Path,
        string FeatureTypeCode, string StatusCode, ApplicationType ApplicationType, int DisplayOrder, bool HasChildren, bool IsDeprecated,
        int TestCaseCount, int AutomatedCount, DateTimeOffset CreatedAt);

    private IQueryable<NodeRow> ProjectNodes(IQueryable<Feature> q) => q.Select(f => new NodeRow(
        f.Id, f.ProductId, f.ParentId, f.Name, f.SequenceNumber, f.SequenceScopeId, f.Path, f.FeatureTypeCode, f.StatusCode, f.ApplicationType,
        f.DisplayOrder, db.Features.Any(c => c.ParentId == f.Id), f.IsDeprecated,
        db.TestCases.Count(tc => tc.FeatureMappings.Any(m => m.FeatureId == f.Id)),
        db.TestCases.Count(tc => tc.AutomationStatus == AutomationStatus.Automated && tc.FeatureMappings.Any(m => m.FeatureId == f.Id)),
        f.CreatedAt));

    private static FeatureTreeNode ToNode(NodeRow r, SequenceScope scope) => new(
        r.Id, r.ProductId, r.ParentId, r.Name, r.SequenceNumber, SequenceFormat.Format(r.SequenceNumber, scope.DisplayWidth, scope.Prefix),
        r.FeatureTypeCode, r.StatusCode, r.ApplicationType, r.DisplayOrder, r.HasChildren, r.IsDeprecated, r.TestCaseCount, r.AutomatedCount,
        Coverage(r.TestCaseCount, r.AutomatedCount), r.CreatedAt);

    private async Task<SequenceScope> ScopeForProductAsync(Guid productId, CancellationToken ct)
        => await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.SequenceScope!).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Product", productId);

    /// <summary>Resolves the full chain (root → node) for each path with a single query.</summary>
    private async Task<Dictionary<(Guid, string), List<FeatureBreadcrumb>>> LoadAncestorsAsync(
        List<(Guid ScopeId, string Path)> paths, Dictionary<Guid, SequenceScope> scopes, CancellationToken ct)
    {
        var parsed = paths.Distinct().Select(p => (p.ScopeId, p.Path, Seqs: FeaturePath.Parse(p.Path))).ToList();
        var result = new Dictionary<(Guid, string), List<FeatureBreadcrumb>>();
        foreach (var group in parsed.GroupBy(p => p.ScopeId))
        {
            var allSeqs = group.SelectMany(g => g.Seqs).Distinct().ToList();
            var names = await db.Features.AsNoTracking().Where(f => f.SequenceScopeId == group.Key && allSeqs.Contains(f.SequenceNumber))
                .Select(f => new { f.Id, f.Name, f.SequenceNumber }).ToDictionaryAsync(f => f.SequenceNumber, ct);
            var scope = scopes[group.Key];
            foreach (var (scopeId, path, seqs) in group)
                result[(scopeId, path)] = seqs.Where(names.ContainsKey)
                    .Select(s => new FeatureBreadcrumb(names[s].Id, names[s].Name, SequenceFormat.Format(s, scope.DisplayWidth, scope.Prefix))).ToList();
        }
        return result;
    }
}

public static class FeaturePath
{
    public static string Build(string? parentPath, long sequence) => $"{parentPath ?? "/"}{sequence}/";
    public static long[] Parse(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToArray();
}
