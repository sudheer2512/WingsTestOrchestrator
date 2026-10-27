using Microsoft.AspNetCore.Mvc;
using Wings.Api.Security;
using Wings.Application.Abstractions;
using Wings.Application.Administration;
using Wings.Application.Common;
using Wings.Application.Features;
using Wings.Application.Security;
using Wings.Application.Testing;

namespace Wings.Api.Controllers;

[ApiController, Route("api/v1/products")]
public sealed class ProductsController(IProductService products, IFeatureQueryService queries, IFeatureService features, IChangeNotifier notifier) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.FeaturesView)]
    public Task<IReadOnlyList<ProductDto>> List(CancellationToken ct) => products.ListAsync(ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.FeaturesView)]
    public Task<ProductDto> Get(Guid id, CancellationToken ct) => products.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.ProductsManage)]
    public async Task<ActionResult<ProductDto>> Create(ProductUpsert request, CancellationToken ct)
    {
        var p = await products.CreateAsync(request, ct);
        await notifier.NotifyAsync("productsChanged", new { productId = p.Id }, ct);
        return CreatedAtAction(nameof(Get), new { id = p.Id }, p);
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.ProductsManage)]
    public Task<ProductDto> Update(Guid id, ProductUpsert request, CancellationToken ct) => products.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.ProductsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await products.DeleteAsync(id, ct); return NoContent(); }

    [HttpGet("{id:guid}/releases"), HasPermission(Permissions.FeaturesView)]
    public Task<IReadOnlyList<ProductVersionDto>> Releases(Guid id, CancellationToken ct) => products.ListVersionsAsync(id, ct);

    [HttpPost("{id:guid}/releases"), HasPermission(Permissions.ProductsManage)]
    public Task<ProductVersionDto> CreateRelease(Guid id, ProductVersionUpsert request, CancellationToken ct) => products.CreateVersionAsync(id, request, ct);

    [HttpPut("{id:guid}/releases/{releaseId:guid}"), HasPermission(Permissions.ProductsManage)]
    public Task<ProductVersionDto> UpdateRelease(Guid id, Guid releaseId, ProductVersionUpsert request, CancellationToken ct) => products.UpdateVersionAsync(id, releaseId, request, ct);

    /// <summary>Lazy-loads one level of the feature tree. Omit parentId for the product's top level.</summary>
    [HttpGet("{id:guid}/tree"), HasPermission(Permissions.FeaturesView)]
    public Task<IReadOnlyList<FeatureTreeNode>> Tree(Guid id, [FromQuery] Guid? parentId, CancellationToken ct) => queries.GetChildrenAsync(id, parentId, ct);

    [HttpGet("{id:guid}/features/export"), HasPermission(Permissions.FeaturesView)]
    public async Task<IActionResult> Export(Guid id, [FromQuery] string format = "xlsx", CancellationToken ct = default)
    {
        var (content, type, name) = await features.ExportAsync(id, format, ct);
        return File(content, type, name);
    }

    [HttpPost("{id:guid}/features/import"), HasPermission(Permissions.FeaturesEdit), RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<FeatureImportResult> Import(Guid id, IFormFile file, CancellationToken ct)
    {
        var format = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
        await using var s = file.OpenReadStream();
        var result = await features.ImportAsync(id, s, format, ct);
        if (result.Created > 0) await notifier.NotifyAsync("featuresChanged", new { productId = id }, ct);
        return result;
    }
}

[ApiController, Route("api/v1/sequence-scopes"), HasPermission(Permissions.FeaturesView)]
public sealed class SequenceScopesController(IProductService products) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SequenceScopeDto>> List(CancellationToken ct) => products.ListScopesAsync(ct);
}

[ApiController, Route("api/v1/features")]
public sealed class FeaturesController(IFeatureQueryService queries, IFeatureService features, IAuditQueryService audit, ITestCaseService testCases, IChangeNotifier notifier) : ControllerBase
{
    [HttpGet("search"), HasPermission(Permissions.FeaturesView)]
    public Task<PagedResult<FeatureSearchHit>> Search([FromQuery] FeatureFilter filter, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
        => queries.SearchAsync(filter, new PageRequest(page, pageSize), ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.FeaturesView)]
    public Task<FeatureDetailDto> Get(Guid id, CancellationToken ct) => queries.GetAsync(id, ct);

    [HttpGet("{id:guid}/ancestors"), HasPermission(Permissions.FeaturesView)]
    public Task<IReadOnlyList<FeatureBreadcrumb>> Ancestors(Guid id, CancellationToken ct) => queries.GetAncestorsAsync(id, ct);

    [HttpPost, HasPermission(Permissions.FeaturesEdit)]
    public async Task<ActionResult<FeatureDetailDto>> Create(CreateFeatureRequest request, CancellationToken ct)
    {
        var f = await features.CreateAsync(request, ct);
        await Changed(f.ProductId, f.ParentId, ct);
        return CreatedAtAction(nameof(Get), new { id = f.Id }, f);
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<FeatureDetailDto> Update(Guid id, UpdateFeatureRequest request, CancellationToken ct)
    {
        var f = await features.UpdateAsync(id, request, ct);
        await Changed(f.ProductId, f.ParentId, ct);
        return f;
    }

    [HttpPost("{id:guid}/move"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<FeatureDetailDto> Move(Guid id, MoveFeatureRequest request, CancellationToken ct)
    {
        var f = await features.MoveAsync(id, request, ct);
        await Changed(f.ProductId, null, ct);
        return f;
    }

    [HttpPost("reorder"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<IActionResult> Reorder(ReorderRequest request, CancellationToken ct)
    {
        await features.ReorderAsync(request, ct);
        await Changed(request.ProductId, request.ParentId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/duplicate"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<FeatureDetailDto> Duplicate(Guid id, DuplicateFeatureRequest request, CancellationToken ct)
    {
        var f = await features.DuplicateAsync(id, request, ct);
        await Changed(f.ProductId, f.ParentId, ct);
        return f;
    }

    public sealed record DeprecateRequest(Guid? ReplacedById, string? Note);

    [HttpPost("{id:guid}/deprecate"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<FeatureDetailDto> Deprecate(Guid id, DeprecateRequest request, CancellationToken ct)
    {
        var f = await features.DeprecateAsync(id, request.ReplacedById, request.Note, ct);
        await Changed(f.ProductId, f.ParentId, ct);
        return f;
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<object> Delete(Guid id, CancellationToken ct)
    {
        var f = await queries.GetAsync(id, ct);
        var count = await features.DeleteAsync(id, ct);
        await Changed(f.ProductId, f.ParentId, ct);
        return new { deleted = count };
    }

    [HttpGet("{id:guid}/audit"), HasPermission(Permissions.FeaturesView)]
    public async Task<PagedResult<AuditEntryDto>> Audit(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        await queries.GetAncestorsAsync(id, ct); // access check
        return await audit.ForEntityAsync("Feature", id, new PageRequest(page, pageSize), ct);
    }

    [HttpGet("{id:guid}/automation"), HasPermission(Permissions.TestCasesView)]
    public async Task<IReadOnlyList<FeatureAutomationDto>> Automation(Guid id, [FromServices] IPackageService packages, CancellationToken ct)
    {
        await queries.GetAncestorsAsync(id, ct); // access check
        return await packages.ForFeatureAsync(id, ct);
    }

    [HttpGet("{id:guid}/test-cases"), HasPermission(Permissions.TestCasesView)]
    public Task<PagedResult<TestCaseSummaryDto>> TestCases(Guid id, [FromQuery] bool includeSubfeatures = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
        => testCases.SearchAsync(new TestCaseFilter { FeatureId = id, IncludeSubfeatures = includeSubfeatures }, new PageRequest(page, pageSize), ct);

    // ---- child records ----

    [HttpPost("{id:guid}/requirements"), HasPermission(Permissions.FeaturesEdit)]
    public Task<RequirementDto> AddRequirement(Guid id, RequirementUpsert request, CancellationToken ct) => features.AddRequirementAsync(id, request, ct);

    [HttpPut("{id:guid}/requirements/{requirementId:guid}"), HasPermission(Permissions.FeaturesEdit)]
    public Task<RequirementDto> UpdateRequirement(Guid id, Guid requirementId, RequirementUpsert request, CancellationToken ct) => features.UpdateRequirementAsync(id, requirementId, request, ct);

    [HttpDelete("{id:guid}/requirements/{requirementId:guid}"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<IActionResult> DeleteRequirement(Guid id, Guid requirementId, CancellationToken ct) { await features.DeleteRequirementAsync(id, requirementId, ct); return NoContent(); }

    [HttpPost("{id:guid}/source-mappings"), HasPermission(Permissions.FeaturesEdit)]
    public Task<SourceMappingDto> AddSourceMapping(Guid id, SourceMappingUpsert request, CancellationToken ct) => features.AddSourceMappingAsync(id, request, ct);

    [HttpPost("{id:guid}/source-mappings/{mappingId:guid}/approve"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<IActionResult> ApproveSourceMapping(Guid id, Guid mappingId, CancellationToken ct) { await features.ApproveSourceMappingAsync(id, mappingId, ct); return NoContent(); }

    [HttpDelete("{id:guid}/source-mappings/{mappingId:guid}"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<IActionResult> DeleteSourceMapping(Guid id, Guid mappingId, CancellationToken ct) { await features.DeleteSourceMappingAsync(id, mappingId, ct); return NoContent(); }

    [HttpPost("{id:guid}/dependencies"), HasPermission(Permissions.FeaturesEdit)]
    public Task<DependencyDto> AddDependency(Guid id, DependencyUpsert request, CancellationToken ct) => features.AddDependencyAsync(id, request, ct);

    [HttpDelete("{id:guid}/dependencies/{dependencyId:guid}"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<IActionResult> DeleteDependency(Guid id, Guid dependencyId, CancellationToken ct) { await features.DeleteDependencyAsync(id, dependencyId, ct); return NoContent(); }

    [HttpPost("{id:guid}/attachments"), HasPermission(Permissions.FeaturesEdit), RequestSizeLimit(FeatureService.MaxAttachmentBytes + 1024 * 1024)]
    public async Task<AttachmentDto> UploadAttachment(Guid id, IFormFile file, [FromForm] string? caption, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return await features.AddAttachmentAsync(id, file.FileName, file.ContentType, s, caption, ct);
    }

    public sealed record LinkRequest(string Url, string? Caption);

    [HttpPost("{id:guid}/links"), HasPermission(Permissions.FeaturesEdit)]
    public Task<AttachmentDto> AddLink(Guid id, LinkRequest request, CancellationToken ct) => features.AddLinkAsync(id, request.Url, request.Caption, ct);

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}"), HasPermission(Permissions.FeaturesView)]
    public async Task<IActionResult> DownloadAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var (meta, content) = await features.OpenAttachmentAsync(id, attachmentId, ct);
        // Served as a download (never rendered inline) so uploaded HTML/SVG cannot run script in the app's origin.
        return File(content, "application/octet-stream", meta.FileName);
    }

    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}"), HasPermission(Permissions.FeaturesEdit)]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken ct) { await features.DeleteAttachmentAsync(id, attachmentId, ct); return NoContent(); }

    private Task Changed(Guid productId, Guid? parentId, CancellationToken ct) => notifier.NotifyAsync("featuresChanged", new { productId, parentId }, ct);
}
