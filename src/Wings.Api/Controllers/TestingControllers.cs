using Microsoft.AspNetCore.Mvc;
using Wings.Api.Security;
using Wings.Application.Administration;
using Wings.Application.Common;
using Wings.Application.Features;
using Wings.Application.Security;
using Wings.Application.Testing;

namespace Wings.Api.Controllers;

[ApiController, Route("api/v1/test-cases")]
public sealed class TestCasesController(ITestCaseService testCases, IAuditQueryService audit) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<PagedResult<TestCaseSummaryDto>> Search([FromQuery] TestCaseFilter filter, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => testCases.SearchAsync(filter, new PageRequest(page, pageSize), ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TestCasesView)]
    public Task<TestCaseDetailDto> Get(Guid id, CancellationToken ct) => testCases.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.TestCasesEdit)]
    public async Task<ActionResult<TestCaseDetailDto>> Create(SaveTestCaseRequest request, CancellationToken ct)
    {
        var tc = await testCases.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = tc.Id }, tc);
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.TestCasesEdit)]
    public Task<TestCaseDetailDto> Update(Guid id, SaveTestCaseRequest request, CancellationToken ct) => testCases.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/clone"), HasPermission(Permissions.TestCasesEdit)]
    public Task<TestCaseDetailDto> Clone(Guid id, CloneRequest request, CancellationToken ct) => testCases.CloneAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.TestCasesEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await testCases.DeleteAsync(id, ct); return NoContent(); }

    [HttpPost("bulk"), HasPermission(Permissions.TestCasesEdit)]
    public Task<BulkResult> Bulk(BulkUpdateRequest request, CancellationToken ct) => testCases.BulkUpdateAsync(request, ct);

    [HttpGet("{id:guid}/versions"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<TestCaseVersionDto>> Versions(Guid id, CancellationToken ct) => testCases.VersionsAsync(id, ct);

    [HttpPost("{id:guid}/submit"), HasPermission(Permissions.TestCasesEdit)]
    public Task<TestCaseDetailDto> Submit(Guid id, CancellationToken ct) => testCases.SubmitForReviewAsync(id, ct);

    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.TestCasesApprove)]
    public Task<TestCaseDetailDto> Approve(Guid id, ReviewDecision decision, CancellationToken ct) => testCases.ApproveAsync(id, decision, ct);

    [HttpPost("{id:guid}/reject"), HasPermission(Permissions.TestCasesApprove)]
    public Task<TestCaseDetailDto> Reject(Guid id, ReviewDecision decision, CancellationToken ct) => testCases.RejectAsync(id, decision, ct);

    [HttpPost("{id:guid}/dependencies"), HasPermission(Permissions.TestCasesEdit)]
    public Task<TestCaseDependencyDto> AddDependency(Guid id, TestCaseDependencyUpsert request, CancellationToken ct) => testCases.AddDependencyAsync(id, request, ct);

    [HttpDelete("{id:guid}/dependencies/{dependencyId:guid}"), HasPermission(Permissions.TestCasesEdit)]
    public async Task<IActionResult> RemoveDependency(Guid id, Guid dependencyId, CancellationToken ct) { await testCases.RemoveDependencyAsync(id, dependencyId, ct); return NoContent(); }

    [HttpGet("{id:guid}/audit"), HasPermission(Permissions.TestCasesView)]
    public async Task<PagedResult<AuditEntryDto>> Audit(Guid id, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        await testCases.GetAsync(id, ct);
        return await audit.ForEntityAsync("TestCase", id, new PageRequest(page, 50), ct);
    }

    [HttpGet("export"), HasPermission(Permissions.TestCasesView)]
    public async Task<IActionResult> Export([FromQuery] TestCaseFilter filter, [FromQuery] string format = "xlsx", CancellationToken ct = default)
    {
        var (content, type, name) = await testCases.ExportAsync(filter, format, ct);
        return File(content, type, name);
    }

    [HttpPost("import"), HasPermission(Permissions.TestCasesEdit), RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ImportResult> Import(IFormFile file, [FromForm] Guid? folderId, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return await testCases.ImportAsync(s, Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant(), folderId, ct);
    }
}

[ApiController, Route("api/v1/test-folders")]
public sealed class TestFoldersController(ITestFolderService folders) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<FolderDto>> List(CancellationToken ct) => folders.ListAsync(ct);

    [HttpPost, HasPermission(Permissions.TestCasesEdit)]
    public Task<FolderDto> Create(FolderUpsert request, CancellationToken ct) => folders.CreateAsync(request, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.TestCasesEdit)]
    public Task<FolderDto> Update(Guid id, FolderUpsert request, CancellationToken ct) => folders.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.TestCasesEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await folders.DeleteAsync(id, ct); return NoContent(); }
}

[ApiController, Route("api/v1/test-suites")]
public sealed class TestSuitesController(ITestSuiteService suites) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<SuiteSummaryDto>> List([FromQuery] Guid? productId, [FromQuery] string? text, CancellationToken ct) => suites.ListAsync(productId, text, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TestCasesView)]
    public Task<SuiteDetailDto> Get(Guid id, CancellationToken ct) => suites.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.TestCasesEdit)]
    public Task<SuiteDetailDto> Create(SuiteUpsert request, CancellationToken ct) => suites.CreateAsync(request, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.TestCasesEdit)]
    public Task<SuiteDetailDto> Update(Guid id, SuiteUpsert request, CancellationToken ct) => suites.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.TestCasesEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await suites.DeleteAsync(id, ct); return NoContent(); }

    [HttpPost("{id:guid}/items"), HasPermission(Permissions.TestCasesEdit)]
    public Task<SuiteDetailDto> AddItems(Guid id, SuiteItemsRequest request, CancellationToken ct) => suites.AddItemsAsync(id, request, ct);

    [HttpDelete("{id:guid}/items/{itemId:guid}"), HasPermission(Permissions.TestCasesEdit)]
    public Task<SuiteDetailDto> RemoveItem(Guid id, Guid itemId, CancellationToken ct) => suites.RemoveItemAsync(id, itemId, ct);

    [HttpPut("{id:guid}/items/order"), HasPermission(Permissions.TestCasesEdit)]
    public Task<SuiteDetailDto> Reorder(Guid id, IReadOnlyList<Guid> orderedItemIds, CancellationToken ct) => suites.ReorderAsync(id, orderedItemIds, ct);
}

[ApiController, Route("api/v1/test-packages")]
public sealed class TestPackagesController(IPackageService packages, IAuditQueryService audit) : ControllerBase
{
    public sealed record PackageDetail(PackageSummaryDto Package, IReadOnlyList<PackageVersionSummaryDto> Versions);

    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<PackageSummaryDto>> List([FromQuery] Guid? productId, [FromQuery] string? text, CancellationToken ct) => packages.ListAsync(productId, text, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TestCasesView)]
    public async Task<PackageDetail> Get(Guid id, CancellationToken ct)
    {
        var (p, v) = await packages.GetAsync(id, ct);
        return new PackageDetail(p, v);
    }

    /// <summary>Uploads a new package (or a new version when packageId is supplied). Validation runs asynchronously.</summary>
    [HttpPost("upload"), HasPermission(Permissions.TestCodeUpload), DisableRequestSizeLimit]
    public async Task<ActionResult<PackageVersionSummaryDto>> Upload(IFormFile file, [FromForm] UploadPackageRequest request, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        var v = await packages.UploadAsync(request, file.FileName, s, ct);
        return Accepted($"/api/v1/test-packages/versions/{v.Id}", v);
    }

    [HttpGet("versions/{versionId:guid}"), HasPermission(Permissions.TestCasesView)]
    public Task<PackageVersionDetailDto> Version(Guid versionId, CancellationToken ct) => packages.GetVersionAsync(versionId, ct);

    [HttpGet("versions/{versionId:guid}/file"), HasPermission(Permissions.TestCasesView)]
    public Task<PackageFileContentDto> File(Guid versionId, [FromQuery] string path, CancellationToken ct) => packages.GetFileAsync(versionId, path, ct);

    [HttpGet("versions/{versionId:guid}/download"), HasPermission(Permissions.TestCasesView)]
    public async Task<IActionResult> Download(Guid versionId, CancellationToken ct)
    {
        var (content, name) = await packages.DownloadAsync(versionId, ct);
        return File(content, "application/octet-stream", name);
    }

    [HttpPost("{id:guid}/edits"), HasPermission(Permissions.TestCodeEdit)]
    public Task<PackageVersionSummaryDto> Edit(Guid id, EditPackageFilesRequest request, CancellationToken ct) => packages.EditFilesAsync(id, request, ct);

    [HttpPut("versions/{versionId:guid}/tests/{testId:guid}/mapping"), HasPermission(Permissions.TestCodeUpload)]
    public Task<DiscoveredTestDto> Map(Guid versionId, Guid testId, MapTestRequest request, CancellationToken ct) => packages.MapTestAsync(versionId, testId, request, ct);

    [HttpPost("versions/{versionId:guid}/approve"), HasPermission(Permissions.TestCodeApprove)]
    public Task<PackageVersionDetailDto> Approve(Guid versionId, ReviewDecision decision, CancellationToken ct) => packages.ApproveAsync(versionId, decision, ct);

    [HttpPost("versions/{versionId:guid}/reject"), HasPermission(Permissions.TestCodeApprove)]
    public Task<PackageVersionDetailDto> Reject(Guid versionId, ReviewDecision decision, CancellationToken ct) => packages.RejectAsync(versionId, decision, ct);

    [HttpPost("versions/{versionId:guid}/publish"), HasPermission(Permissions.TestCodePublish)]
    public Task<PackageVersionDetailDto> Publish(Guid versionId, CancellationToken ct) => packages.PublishAsync(versionId, ct);

    [HttpPost("versions/{versionId:guid}/deprecate"), HasPermission(Permissions.TestCodePublish)]
    public Task<PackageVersionDetailDto> Deprecate(Guid versionId, CancellationToken ct) => packages.DeprecateAsync(versionId, ct);

    [HttpPost("versions/{versionId:guid}/revalidate"), HasPermission(Permissions.TestCodeUpload)]
    public Task<PackageVersionDetailDto> Revalidate(Guid versionId, CancellationToken ct) => packages.RevalidateAsync(versionId, ct);

    [HttpGet("{id:guid}/audit"), HasPermission(Permissions.TestCasesView)]
    public Task<PagedResult<AuditEntryDto>> Audit(Guid id, [FromQuery] int page = 1, CancellationToken ct = default)
        => audit.ForEntityAsync("TestPackage", id, new PageRequest(page, 50), ct);
}
