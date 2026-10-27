using Microsoft.EntityFrameworkCore;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Domain.Features;
using Wings.Domain.Testing;

namespace Wings.Application.Features;

public interface IFeatureService
{
    Task<FeatureDetailDto> CreateAsync(CreateFeatureRequest request, CancellationToken ct = default);
    Task<FeatureDetailDto> UpdateAsync(Guid id, UpdateFeatureRequest request, CancellationToken ct = default);
    Task<FeatureDetailDto> MoveAsync(Guid id, MoveFeatureRequest request, CancellationToken ct = default);
    Task ReorderAsync(ReorderRequest request, CancellationToken ct = default);
    Task<FeatureDetailDto> DuplicateAsync(Guid id, DuplicateFeatureRequest request, CancellationToken ct = default);
    Task<FeatureDetailDto> DeprecateAsync(Guid id, Guid? replacedById, string? note, CancellationToken ct = default);
    Task<int> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<RequirementDto> AddRequirementAsync(Guid featureId, RequirementUpsert request, CancellationToken ct = default);
    Task<RequirementDto> UpdateRequirementAsync(Guid featureId, Guid requirementId, RequirementUpsert request, CancellationToken ct = default);
    Task DeleteRequirementAsync(Guid featureId, Guid requirementId, CancellationToken ct = default);

    Task<SourceMappingDto> AddSourceMappingAsync(Guid featureId, SourceMappingUpsert request, CancellationToken ct = default);
    Task ApproveSourceMappingAsync(Guid featureId, Guid mappingId, CancellationToken ct = default);
    Task DeleteSourceMappingAsync(Guid featureId, Guid mappingId, CancellationToken ct = default);

    Task<DependencyDto> AddDependencyAsync(Guid featureId, DependencyUpsert request, CancellationToken ct = default);
    Task DeleteDependencyAsync(Guid featureId, Guid dependencyId, CancellationToken ct = default);

    Task<AttachmentDto> AddAttachmentAsync(Guid featureId, string fileName, string contentType, Stream content, string? caption, CancellationToken ct = default);
    Task<AttachmentDto> AddLinkAsync(Guid featureId, string url, string? caption, CancellationToken ct = default);
    Task<(AttachmentDto Meta, Stream Content)> OpenAttachmentAsync(Guid featureId, Guid attachmentId, CancellationToken ct = default);
    Task DeleteAttachmentAsync(Guid featureId, Guid attachmentId, CancellationToken ct = default);

    Task<FeatureImportResult> ImportAsync(Guid productId, Stream content, string format, CancellationToken ct = default);
    Task<(byte[] Content, string ContentType, string FileName)> ExportAsync(Guid productId, string format, CancellationToken ct = default);
}

public sealed class FeatureService(
    IWingsDbContext db, ISequenceAllocator sequences, IProductAccess access, IFeatureQueryService queries,
    IFileStore files, IEnumerable<ITabularCodec> codecs) : IFeatureService
{
    public const long MaxAttachmentBytes = 100L * 1024 * 1024;

    public async Task<FeatureDetailDto> CreateAsync(CreateFeatureRequest request, CancellationToken ct = default)
    {
        var id = await db.InTransactionAsync(async () =>
        {
            var feature = await CreateCoreAsync(request, ct);
            await db.SaveChangesAsync(ct);
            return feature.Id;
        }, ct);
        return await queries.GetAsync(id, ct);
    }

    /// <summary>Allocates the sequence and builds the entity; caller saves inside its transaction.</summary>
    private async Task<Feature> CreateCoreAsync(CreateFeatureRequest request, CancellationToken ct, DateTimeOffset? createdAt = null, string? createdBy = null, string? release = null)
    {
        await ValidateFieldsAsync(request.Fields, null, ct);
        await access.EnsureCanEditAsync(request.ProductId, ct);
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
                      ?? throw new NotFoundException("Product", request.ProductId);

        Feature? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await db.Features.FirstOrDefaultAsync(f => f.Id == parentId, ct) ?? throw new NotFoundException("Parent feature", parentId);
            if (parent.ProductId != product.Id) throw new ValidationException("parentId", "The parent belongs to a different product.");
        }

        long sequence;
        if (request.LegacySequenceNumber is { } legacy)
        {
            if (await db.Features.IgnoreQueryFilters().AnyAsync(f => f.SequenceScopeId == product.SequenceScopeId && f.SequenceNumber == legacy, ct))
                throw new ValidationException("sequenceNumber", $"Sequence {legacy} is already used (or was used by a deleted feature) in this numbering scope.");
            await sequences.ReserveAsync(product.SequenceScopeId, legacy, ct);
            sequence = legacy;
        }
        else sequence = await sequences.NextAsync(product.SequenceScopeId, ct);

        var siblingsMax = await db.Features.Where(f => f.ProductId == product.Id && f.ParentId == request.ParentId).MaxAsync(f => (int?)f.DisplayOrder, ct) ?? 0;
        var feature = new Feature
        {
            ProductId = product.Id, ParentId = parent?.Id, SequenceScopeId = product.SequenceScopeId, SequenceNumber = sequence,
            IsLegacySequence = request.LegacySequenceNumber is not null, Path = FeaturePath.Build(parent?.Path, sequence),
            Depth = (parent?.Depth ?? -1) + 1, DisplayOrder = siblingsMax + 10,
        };
        if (createdAt is { } at) feature.CreatedAt = at;
        if (createdBy.Clean() is { } by) feature.CreatedBy = by;
        Apply(feature, request.Fields);
        feature.OriginatingRelease = release.Clean() ?? (request.Fields.IntroducedInVersionId is { } vid
            ? await db.ProductVersions.Where(v => v.Id == vid).Select(v => v.VersionLabel).FirstOrDefaultAsync(ct) : null);
        db.Features.Add(feature);
        return feature;
    }

    public async Task<FeatureDetailDto> UpdateAsync(Guid id, UpdateFeatureRequest request, CancellationToken ct = default)
    {
        var feature = await LoadForEditAsync(id, ct);
        await ValidateFieldsAsync(request.Fields, feature, ct);
        db.SetOriginalVersion(feature, request.Version);
        Apply(feature, request.Fields);
        await db.SaveChangesAsync(ct);
        return await queries.GetAsync(id, ct);
    }

    public async Task<FeatureDetailDto> MoveAsync(Guid id, MoveFeatureRequest request, CancellationToken ct = default)
    {
        await db.InTransactionAsync(async () =>
        {
            var feature = await LoadForEditAsync(id, ct);
            db.SetOriginalVersion(feature, request.Version);
            var targetProductId = request.NewProductId ?? feature.ProductId;
            await access.EnsureCanEditAsync(targetProductId, ct);

            Feature? newParent = null;
            if (request.NewParentId is { } npid)
            {
                newParent = await db.Features.FirstOrDefaultAsync(f => f.Id == npid, ct) ?? throw new NotFoundException("Target parent", npid);
                targetProductId = newParent.ProductId;
                if (newParent.SequenceScopeId == feature.SequenceScopeId && newParent.Path.StartsWith(feature.Path))
                    throw new ValidationException("newParentId", "A feature cannot be moved under itself or one of its descendants.");
            }
            var targetScope = await db.Products.Where(p => p.Id == targetProductId).Select(p => (Guid?)p.SequenceScopeId).FirstOrDefaultAsync(ct)
                              ?? throw new NotFoundException("Product", targetProductId);
            if (targetScope != feature.SequenceScopeId)
                throw new ValidationException("newProductId", "Features can only move between products that share a numbering scope, so their sequence numbers are preserved.");

            var oldPath = feature.Path;
            var newPath = FeaturePath.Build(newParent?.Path, feature.SequenceNumber);
            var depthDelta = ((newParent?.Depth ?? -1) + 1) - feature.Depth;
            var subtree = await db.Features.Where(f => f.SequenceScopeId == feature.SequenceScopeId && f.Path.StartsWith(oldPath) && f.Id != feature.Id).ToListAsync(ct);

            feature.ParentId = newParent?.Id;
            feature.ProductId = targetProductId;
            feature.Path = newPath;
            feature.Depth += depthDelta;
            foreach (var d in subtree)
            {
                d.Path = newPath + d.Path[oldPath.Length..];
                d.Depth += depthDelta;
                d.ProductId = targetProductId;
            }

            var siblings = await db.Features.Where(f => f.ProductId == targetProductId && f.ParentId == feature.ParentId && f.Id != feature.Id)
                .OrderBy(f => f.DisplayOrder).ThenBy(f => f.SequenceNumber).ToListAsync(ct);
            siblings.Insert(Math.Clamp(request.Position ?? siblings.Count, 0, siblings.Count), feature);
            for (var i = 0; i < siblings.Count; i++) siblings[i].DisplayOrder = (i + 1) * 10;

            db.PendingAuditAction = "Moved";
            db.PendingAuditSummary = $"Moved to {(newParent is null ? "product root" : newParent.Name)}";
            await db.SaveChangesAsync(ct);
        }, ct);
        return await queries.GetAsync(id, ct);
    }

    public async Task ReorderAsync(ReorderRequest request, CancellationToken ct = default)
    {
        await access.EnsureCanEditAsync(request.ProductId, ct);
        var siblings = await db.Features.Where(f => f.ProductId == request.ProductId && f.ParentId == request.ParentId).ToListAsync(ct);
        if (siblings.Count != request.OrderedIds.Count || siblings.Any(s => !request.OrderedIds.Contains(s.Id)))
            throw new ValidationException("orderedIds", "The list must contain every sibling exactly once. Reload the tree and try again.");
        var order = request.OrderedIds.Select((sid, i) => (sid, i)).ToDictionary(x => x.sid, x => (x.i + 1) * 10);
        foreach (var s in siblings) s.DisplayOrder = order[s.Id];
        db.PendingAuditAction = "Reordered";
        await db.SaveChangesAsync(ct);
    }

    public async Task<FeatureDetailDto> DuplicateAsync(Guid id, DuplicateFeatureRequest request, CancellationToken ct = default)
    {
        var source = await db.Features.AsNoTracking().Include(f => f.Requirements).FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("Feature", id);
        var fields = FeatureQueryService.ToFields(source) with
        {
            Name = request.NewName.Clean() ?? $"{source.Name} (copy)", StatusCode = FeatureStatuses.Planned, IsDeprecated = false, ReplacedByFeatureId = null,
        };
        var newId = await db.InTransactionAsync(async () =>
        {
            var copy = await CreateCoreAsync(new CreateFeatureRequest(source.ProductId, source.ParentId, fields), ct);
            copy.DisplayOrder = source.DisplayOrder + 5;
            if (request.IncludeRequirements)
                foreach (var r in source.Requirements)
                    db.FeatureRequirements.Add(new FeatureRequirement { FeatureId = copy.Id, Code = r.Code, Title = r.Title, Description = r.Description, Priority = r.Priority, ExternalLink = r.ExternalLink });
            if (request.IncludeTestMappings)
                foreach (var tcId in await db.TestCaseFeatureMappings.Where(m => m.FeatureId == id).Select(m => m.TestCaseId).ToListAsync(ct))
                    db.TestCaseFeatureMappings.Add(new TestCaseFeatureMapping { TestCaseId = tcId, FeatureId = copy.Id });
            db.PendingAuditSummary = $"Duplicated from {source.Name}";
            await db.SaveChangesAsync(ct);
            // Keep the copy directly after its source without disturbing other siblings' relative order.
            var siblings = await db.Features.Where(f => f.ProductId == source.ProductId && f.ParentId == source.ParentId)
                .OrderBy(f => f.DisplayOrder).ThenBy(f => f.SequenceNumber).ToListAsync(ct);
            for (var i = 0; i < siblings.Count; i++) siblings[i].DisplayOrder = (i + 1) * 10;
            await db.SaveChangesAsync(ct);
            return copy.Id;
        }, ct);
        return await queries.GetAsync(newId, ct);
    }

    public async Task<FeatureDetailDto> DeprecateAsync(Guid id, Guid? replacedById, string? note, CancellationToken ct = default)
    {
        var feature = await LoadForEditAsync(id, ct);
        if (replacedById is { } rid && (rid == id || !await db.Features.AnyAsync(f => f.Id == rid, ct)))
            throw new ValidationException("replacedById", "Choose an existing, different feature as the replacement.");
        feature.IsDeprecated = true;
        feature.StatusCode = FeatureStatuses.Deprecated;
        feature.ReplacedByFeatureId = replacedById;
        feature.DeprecationNote = note.Clean();
        db.PendingAuditAction = "Deprecated";
        await db.SaveChangesAsync(ct);
        return await queries.GetAsync(id, ct);
    }

    public async Task<int> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var feature = await LoadForEditAsync(id, ct);
        var subtree = await db.Features.Where(f => f.SequenceScopeId == feature.SequenceScopeId && f.Path.StartsWith(feature.Path)).ToListAsync(ct);
        // Soft delete: rows (and their sequence numbers) remain, so numbers are never reused and history stays intact.
        foreach (var f in subtree) f.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        return subtree.Count;
    }

    // ---- Requirements -------------------------------------------------------------------------------------------

    public async Task<RequirementDto> AddRequirementAsync(Guid featureId, RequirementUpsert request, CancellationToken ct = default)
    {
        await LoadForEditAsync(featureId, ct);
        ValidateRequirement(request);
        var code = request.Code.Trim();
        if (await db.FeatureRequirements.AnyAsync(r => r.FeatureId == featureId && r.Code == code, ct))
            throw new ValidationException("code", $"Requirement '{code}' already exists on this feature.");
        var r = new FeatureRequirement { FeatureId = featureId, Code = code, Title = request.Title.Trim(), Description = request.Description.Clean(), Priority = request.Priority, ExternalLink = request.ExternalLink.Clean() };
        db.FeatureRequirements.Add(r);
        await db.SaveChangesAsync(ct);
        return new RequirementDto(r.Id, r.Code, r.Title, r.Description, r.Priority, r.ExternalLink, r.Version);
    }

    public async Task<RequirementDto> UpdateRequirementAsync(Guid featureId, Guid requirementId, RequirementUpsert request, CancellationToken ct = default)
    {
        await LoadForEditAsync(featureId, ct);
        ValidateRequirement(request);
        var r = await db.FeatureRequirements.FirstOrDefaultAsync(x => x.Id == requirementId && x.FeatureId == featureId, ct) ?? throw new NotFoundException("Requirement", requirementId);
        if (request.Version is { } v) db.SetOriginalVersion(r, v);
        r.Code = request.Code.Trim(); r.Title = request.Title.Trim(); r.Description = request.Description.Clean(); r.Priority = request.Priority; r.ExternalLink = request.ExternalLink.Clean();
        await db.SaveChangesAsync(ct);
        return new RequirementDto(r.Id, r.Code, r.Title, r.Description, r.Priority, r.ExternalLink, r.Version);
    }

    public async Task DeleteRequirementAsync(Guid featureId, Guid requirementId, CancellationToken ct = default)
    {
        await LoadForEditAsync(featureId, ct);
        var r = await db.FeatureRequirements.FirstOrDefaultAsync(x => x.Id == requirementId && x.FeatureId == featureId, ct) ?? throw new NotFoundException("Requirement", requirementId);
        db.FeatureRequirements.Remove(r);
        await db.SaveChangesAsync(ct);
    }

    // ---- Source mappings ----------------------------------------------------------------------------------------

    public async Task<SourceMappingDto> AddSourceMappingAsync(Guid featureId, SourceMappingUpsert request, CancellationToken ct = default)
    {
        await LoadForEditAsync(featureId, ct);
        new Validator().Required(request.Value, "value", 450).ThrowIfInvalid();
        var m = new FeatureSourceMapping { FeatureId = featureId, Kind = request.Kind, Value = request.Value.Trim(), Repository = request.Repository.Clean(), Notes = request.Notes.Clean() };
        db.FeatureSourceMappings.Add(m);
        await db.SaveChangesAsync(ct);
        return new SourceMappingDto(m.Id, m.Kind, m.Value, m.Repository, m.Notes, m.IsApproved, m.CreatedBy);
    }

    public async Task ApproveSourceMappingAsync(Guid featureId, Guid mappingId, CancellationToken ct = default)
    {
        await LoadForEditAsync(featureId, ct);
        var m = await db.FeatureSourceMappings.FirstOrDefaultAsync(x => x.Id == mappingId && x.FeatureId == featureId, ct) ?? throw new NotFoundException("Source mapping", mappingId);
        m.IsApproved = true;
        db.PendingAuditAction = "Approved";
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSourceMappingAsync(Guid featureId, Guid mappingId, CancellationToken ct = default)
    {
        await LoadForEditAsync(featureId, ct);
        var m = await db.FeatureSourceMappings.FirstOrDefaultAsync(x => x.Id == mappingId && x.FeatureId == featureId, ct) ?? throw new NotFoundException("Source mapping", mappingId);
        db.FeatureSourceMappings.Remove(m);
        await db.SaveChangesAsync(ct);
    }

    // ---- Dependencies -------------------------------------------------------------------------------------------

    public async Task<DependencyDto> AddDependencyAsync(Guid featureId, DependencyUpsert request, CancellationToken ct = default)
    {
        var feature = await LoadForEditAsync(featureId, ct);
        var target = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.DependsOnFeatureId, ct) ?? throw new NotFoundException("Feature", request.DependsOnFeatureId);
        if (await db.FeatureDependencies.AnyAsync(d => d.FeatureId == featureId && d.DependsOnFeatureId == target.Id, ct))
            throw new ValidationException("dependsOnFeatureId", "This dependency already exists.");
        var edges = await db.FeatureDependencies.Select(d => new { d.FeatureId, d.DependsOnFeatureId }).ToListAsync(ct);
        if (DependencyGraph.WouldCreateCycle(featureId, target.Id, edges.Select(e => (e.FeatureId, e.DependsOnFeatureId))))
            throw new ValidationException("dependsOnFeatureId", $"Adding this dependency would create a circular dependency between '{feature.Name}' and '{target.Name}'.");
        var d = new FeatureDependency { FeatureId = featureId, DependsOnFeatureId = target.Id, Type = request.Type, Notes = request.Notes.Clean() };
        db.FeatureDependencies.Add(d);
        await db.SaveChangesAsync(ct);
        var scope = await db.SequenceScopes.AsNoTracking().SingleAsync(s => s.Id == target.SequenceScopeId, ct);
        return new DependencyDto(d.Id, target.Id, target.Name, SequenceFormat.Format(target.SequenceNumber, scope.DisplayWidth, scope.Prefix), d.Type, d.Notes);
    }

    public async Task DeleteDependencyAsync(Guid featureId, Guid dependencyId, CancellationToken ct = default)
    {
        await LoadForEditAsync(featureId, ct);
        var d = await db.FeatureDependencies.FirstOrDefaultAsync(x => x.Id == dependencyId && x.FeatureId == featureId, ct) ?? throw new NotFoundException("Dependency", dependencyId);
        db.FeatureDependencies.Remove(d);
        await db.SaveChangesAsync(ct);
    }

    // ---- Attachments --------------------------------------------------------------------------------------------

    public async Task<AttachmentDto> AddAttachmentAsync(Guid featureId, string fileName, string contentType, Stream content, string? caption, CancellationToken ct = default)
    {
        await LoadForEditAsync(featureId, ct);
        var safeName = Path.GetFileName(fileName);
        new Validator().Required(safeName, "file", 255).ThrowIfInvalid();
        var blob = await files.PutAsync(content, ct);
        if (blob.SizeBytes > MaxAttachmentBytes) throw new ValidationException("file", $"Attachments are limited to {MaxAttachmentBytes / 1024 / 1024} MB.");
        var a = new FeatureAttachment { FeatureId = featureId, FileName = safeName, ContentType = contentType, SizeBytes = blob.SizeBytes, Sha256 = blob.Sha256, StorageKey = blob.Key, Caption = caption.Clean() };
        db.FeatureAttachments.Add(a);
        await db.SaveChangesAsync(ct);
        return new AttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, null, a.Caption, a.CreatedAt, a.CreatedBy);
    }

    public async Task<AttachmentDto> AddLinkAsync(Guid featureId, string url, string? caption, CancellationToken ct = default)
    {
        await LoadForEditAsync(featureId, ct);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ValidationException("url", "Enter an absolute http(s) URL.");
        var a = new FeatureAttachment { FeatureId = featureId, FileName = caption.Clean() ?? uri.Host, ContentType = "text/uri-list", ExternalUrl = uri.ToString(), Caption = caption.Clean() };
        db.FeatureAttachments.Add(a);
        await db.SaveChangesAsync(ct);
        return new AttachmentDto(a.Id, a.FileName, a.ContentType, 0, a.ExternalUrl, a.Caption, a.CreatedAt, a.CreatedBy);
    }

    public async Task<(AttachmentDto Meta, Stream Content)> OpenAttachmentAsync(Guid featureId, Guid attachmentId, CancellationToken ct = default)
    {
        var a = await db.FeatureAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId && x.FeatureId == featureId, ct) ?? throw new NotFoundException("Attachment", attachmentId);
        var productId = await db.Features.Where(f => f.Id == featureId).Select(f => f.ProductId).SingleAsync(ct);
        await access.EnsureCanViewAsync(productId, ct);
        if (a.StorageKey is null) throw new ValidationException("attachment", "This attachment is a link and has no stored content.");
        return (new AttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.ExternalUrl, a.Caption, a.CreatedAt, a.CreatedBy), await files.OpenReadAsync(a.StorageKey, ct));
    }

    public async Task DeleteAttachmentAsync(Guid featureId, Guid attachmentId, CancellationToken ct = default)
    {
        await LoadForEditAsync(featureId, ct);
        var a = await db.FeatureAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.FeatureId == featureId, ct) ?? throw new NotFoundException("Attachment", attachmentId);
        db.FeatureAttachments.Remove(a); // blob stays in the content-addressed store; retention jobs reclaim unreferenced blobs
        await db.SaveChangesAsync(ct);
    }

    // ---- Import / export ----------------------------------------------------------------------------------------

    private static readonly string[] ExportHeaders = ["Sequence", "Path", "Name", "Type", "Status", "ApplicationType", "Description", "Release", "CreatedAt", "CreatedBy", "Deprecated", "Id"];

    public async Task<(byte[] Content, string ContentType, string FileName)> ExportAsync(Guid productId, string format, CancellationToken ct = default)
    {
        await access.EnsureCanViewAsync(productId, ct);
        var codec = Codec(format);
        var product = await db.Products.AsNoTracking().Include(p => p.SequenceScope).FirstOrDefaultAsync(p => p.Id == productId, ct) ?? throw new NotFoundException("Product", productId);
        var features = await db.Features.AsNoTracking().Where(f => f.ProductId == productId).OrderBy(f => f.SequenceNumber).ToListAsync(ct);
        var bySeq = features.ToDictionary(f => f.SequenceNumber);
        var rows = features.Select(f => (IReadOnlyList<string?>)
        [
            SequenceFormat.Format(f.SequenceNumber, product.SequenceScope!.DisplayWidth, product.SequenceScope.Prefix),
            string.Join(" / ", FeaturePath.Parse(f.Path).SkipLast(1).Select(s => bySeq.TryGetValue(s, out var a) ? a.Name : s.ToString())),
            f.Name, f.FeatureTypeCode, f.StatusCode, f.ApplicationType.ToString(), f.Description, f.OriginatingRelease,
            f.CreatedAt.ToString("O"), f.CreatedBy, f.IsDeprecated ? "Yes" : "No", f.Id.ToString(),
        ]);
        return (codec.Write(ExportHeaders, rows, "Features"), codec.ContentType, $"{product.Code}-features.{codec.Format}");
    }

    /// <summary>
    /// Imports rows with columns Path ("Vehicle Sales / Enquiries"), Name, Sequence, Type, Status, ApplicationType, Description,
    /// Release, CreatedAt, CreatedBy. Legacy sequences are preserved; missing ancestors are created as containers. All-or-nothing.
    /// </summary>
    public async Task<FeatureImportResult> ImportAsync(Guid productId, Stream content, string format, CancellationToken ct = default)
    {
        await access.EnsureCanEditAsync(productId, ct);
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct) ?? throw new NotFoundException("Product", productId);
        var rows = Codec(format).Read(content);
        var errors = new List<string>();
        var parsed = new List<FeatureImportRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string? Get(string k) => r.TryGetValue(k, out var v) ? v.Clean() : null;
            var name = Get("Name");
            if (name is null) { errors.Add($"Row {i + 2}: Name is required."); continue; }
            long? seq = null;
            if (Get("Sequence") is { } s)
            {
                if (long.TryParse(s.Trim('[', ']'), out var n) && n > 0) seq = n;
                else { errors.Add($"Row {i + 2}: Sequence '{s}' is not a positive number."); continue; }
            }
            ApplicationType? app = Get("ApplicationType") is { } a ? Enum.TryParse<ApplicationType>(a, true, out var at) ? at : null : null;
            DateTimeOffset? createdAt = Get("CreatedAt") is { } c && DateTimeOffset.TryParse(c, out var dc) ? dc : null;
            parsed.Add(new FeatureImportRow(Get("Path") ?? "", name, seq, Get("Type")?.ToUpperInvariant(), Get("Status")?.ToUpperInvariant(), app, Get("Description"), createdAt, Get("CreatedBy"), Get("Release")));
        }
        var dupes = parsed.Where(p => p.SequenceNumber is not null).GroupBy(p => p.SequenceNumber).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        errors.AddRange(dupes.Select(d => $"Sequence {d} appears more than once in the file."));
        if (errors.Count > 0) return new FeatureImportResult(0, 0, errors);

        var created = 0;
        var skipped = 0;
        await db.InTransactionAsync(async () =>
        {
            // Reserve the highest legacy number first so containers created during import never collide with later rows.
            if (parsed.Max(p => p.SequenceNumber) is { } maxLegacy) await sequences.ReserveAsync(product.SequenceScopeId, maxLegacy, ct);

            var existing = await db.Features.Where(f => f.ProductId == productId).ToListAsync(ct);
            Feature? Find(Guid? parentId, string name) => existing.FirstOrDefault(f => f.ParentId == parentId && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

            foreach (var row in parsed.OrderBy(p => p.SequenceNumber ?? long.MaxValue))
            {
                Guid? parentId = null;
                var segments = row.Path.Split(['/', '>', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (var depth = 0; depth < segments.Length; depth++)
                {
                    var container = Find(parentId, segments[depth]);
                    if (container is null)
                    {
                        container = await CreateCoreAsync(new CreateFeatureRequest(productId, parentId, new FeatureFields
                        {
                            Name = segments[depth], FeatureTypeCode = depth == 0 ? FeatureTypes.Module : FeatureTypes.Submodule, StatusCode = FeatureStatuses.Active,
                        }), ct);
                        await db.SaveChangesAsync(ct);
                        existing.Add(container);
                        created++;
                    }
                    parentId = container.Id;
                }

                if (Find(parentId, row.Name) is not null) { skipped++; continue; }
                var feature = await CreateCoreAsync(new CreateFeatureRequest(productId, parentId, new FeatureFields
                {
                    Name = row.Name, FeatureTypeCode = row.FeatureTypeCode ?? FeatureTypes.Feature, StatusCode = row.StatusCode ?? FeatureStatuses.Active,
                    ApplicationType = row.ApplicationType ?? ApplicationType.None, Description = row.Description,
                }, row.SequenceNumber), ct, row.CreatedAt, row.CreatedBy, row.OriginatingRelease);
                db.PendingAuditAction = "Imported";
                await db.SaveChangesAsync(ct);
                existing.Add(feature);
                created++;
            }
        }, ct);
        return new FeatureImportResult(created, skipped, []);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private ITabularCodec Codec(string format) => codecs.FirstOrDefault(c => c.Format.Equals(format, StringComparison.OrdinalIgnoreCase))
                                                  ?? throw new ValidationException("format", "Supported formats are csv and xlsx.");

    private async Task<Feature> LoadForEditAsync(Guid id, CancellationToken ct)
    {
        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("Feature", id);
        await access.EnsureCanEditAsync(feature.ProductId, ct);
        return feature;
    }

    private async Task ValidateFieldsAsync(FeatureFields f, Feature? existing, CancellationToken ct)
    {
        var v = new Validator().Required(f.Name, "name", 200).MaxLength(f.FormOrRoute, "formOrRoute", 400).MaxLength(f.ClassName, "className", 400);
        if (!await db.FeatureTypes.AnyAsync(t => t.Code == f.FeatureTypeCode, ct)) v.Add("featureTypeCode", $"Unknown feature type '{f.FeatureTypeCode}'.");
        if (!await db.FeatureStatuses.AnyAsync(t => t.Code == f.StatusCode, ct)) v.Add("statusCode", $"Unknown status '{f.StatusCode}'.");
        if (f.ReplacedByFeatureId is { } rid && (rid == existing?.Id || !await db.Features.AnyAsync(x => x.Id == rid, ct)))
            v.Add("replacedByFeatureId", "Choose an existing, different feature as the replacement.");
        if (f.IntroducedInVersionId is { } vid && !await db.ProductVersions.AnyAsync(x => x.Id == vid, ct)) v.Add("introducedInVersionId", "Unknown release.");
        v.ThrowIfInvalid();
    }

    private static void ValidateRequirement(RequirementUpsert r) => new Validator().Required(r.Code, "code", 50).Required(r.Title, "title", 400).ThrowIfInvalid();

    private static void Apply(Feature f, FeatureFields x)
    {
        f.Name = x.Name.Trim(); f.FeatureTypeCode = x.FeatureTypeCode; f.StatusCode = x.StatusCode; f.ApplicationType = x.ApplicationType;
        f.Description = x.Description.Clean(); f.BusinessObjective = x.BusinessObjective.Clean(); f.FunctionalRequirements = x.FunctionalRequirements.Clean();
        f.CurrentVersion = x.CurrentVersion.Clean(); f.IntroducedInVersionId = x.IntroducedInVersionId;
        f.AssemblyName = x.AssemblyName.Clean(); f.Namespace = x.Namespace.Clean(); f.ClassName = x.ClassName.Clean(); f.FormOrRoute = x.FormOrRoute.Clean();
        f.RelatedServices = x.RelatedServices.Clean(); f.ApiEndpoints = x.ApiEndpoints.Clean(); f.DatabaseObjects = x.DatabaseObjects.Clean();
        f.SourceRepository = x.SourceRepository.Clean(); f.BuildPackage = x.BuildPackage.Clean(); f.EntryPoint = x.EntryPoint.Clean(); f.RequiredPermissions = x.RequiredPermissions.Clean();
        f.Preconditions = x.Preconditions.Clean(); f.InputParameters = x.InputParameters.Clean(); f.ExpectedOutcomes = x.ExpectedOutcomes.Clean();
        f.BusinessRules = x.BusinessRules.Clean(); f.ValidationRules = x.ValidationRules.Clean(); f.ErrorConditions = x.ErrorConditions.Clean();
        f.UserRoles = x.UserRoles.Clean(); f.KnownLimitations = x.KnownLimitations.Clean();
        f.IsDeprecated = x.IsDeprecated; f.ReplacedByFeatureId = x.ReplacedByFeatureId; f.DeprecationNote = x.DeprecationNote.Clean();
    }
}
