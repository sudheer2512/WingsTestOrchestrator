using System.Net;
using System.Text;
using Wings.Application.Common;
using Wings.Application.Features;
using Wings.Application.Testing;
using Wings.Domain.Features;
using Wings.Domain.Testing;
using Wings.Tests.Infrastructure;
using Xunit;

namespace Wings.Tests;

public sealed class TestRepositoryTests(WingsApiFactory factory) : IClassFixture<WingsApiFactory>
{
    private async Task<(Guid ProductId, FeatureDetailDto Feature)> FeatureAsync(HttpClient admin)
    {
        var code = "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var product = await (await admin.PostJson("/api/v1/products", new ProductUpsert(code, code, null))).Read<ProductDto>();
        var feature = await (await admin.PostJson("/api/v1/features", new CreateFeatureRequest(product.Id, null, new FeatureFields { Name = "Generate Invoice" })))
            .Read<FeatureDetailDto>(HttpStatusCode.Created);
        return (product.Id, feature);
    }

    private static TestCaseFields Fields(string name, Guid featureId) => new()
    {
        Name = name, TestTypeCode = "FUNCTIONAL", FeatureIds = [featureId], PrimaryFeatureId = featureId, Tags = ["Smoke", "invoice"],
        Steps = [new TestStepDto(1, "Open invoice form", "Form opens", null, null), new TestStepDto(2, "Save", "Invoice number shown", null, null)],
    };

    [Fact(DisplayName = "Test cases are numbered, versioned on every change, and re-enter review after edits")]
    public async Task Lifecycle_VersioningAndApproval()
    {
        var dev = await factory.ClientFor("dev");
        var qa = await factory.ClientFor("qa");
        var (_, feature) = await FeatureAsync(await factory.ClientFor("admin"));

        var tc = await (await dev.PostJson("/api/v1/test-cases", new SaveTestCaseRequest(Fields("Invoice totals", feature.Id)))).Read<TestCaseDetailDto>(HttpStatusCode.Created);
        Assert.StartsWith("TC-", tc.NumberDisplay);
        Assert.Equal(1, tc.Revision);
        Assert.Equal(ReviewStatus.Draft, tc.ReviewStatus);
        Assert.Equal(["invoice", "smoke"], tc.Fields.Tags);
        Assert.True(Assert.Single(tc.Features).IsPrimary);

        (await dev.PostAsync($"/api/v1/test-cases/{tc.Id}/submit", null)).EnsureSuccessStatusCode();
        await (await dev.PostJson($"/api/v1/test-cases/{tc.Id}/approve", new ReviewDecision(null))).Problem(HttpStatusCode.Forbidden); // lacks permission
        var approved = await (await qa.PostJson($"/api/v1/test-cases/{tc.Id}/approve", new ReviewDecision("Looks good"))).Read<TestCaseDetailDto>();
        Assert.Equal(ReviewStatus.Approved, approved.ReviewStatus);
        Assert.Equal("qa", approved.ApprovedBy);

        var edited = await (await dev.PutJson($"/api/v1/test-cases/{tc.Id}", new SaveTestCaseRequest(approved.Fields with { Name = "Invoice totals incl. GST" }, approved.Version, "Clarified name")))
            .Read<TestCaseDetailDto>();
        Assert.Equal(2, edited.Revision);
        Assert.Equal(ReviewStatus.Draft, edited.ReviewStatus);
        await (await dev.PutJson($"/api/v1/test-cases/{tc.Id}", new SaveTestCaseRequest(approved.Fields, approved.Version))).Problem(HttpStatusCode.Conflict);

        var versions = await (await dev.GetAsync($"/api/v1/test-cases/{tc.Id}/versions")).Read<List<TestCaseVersionDto>>();
        Assert.Equal([2, 1], versions.Select(v => v.Revision));
        Assert.Contains("Invoice totals incl. GST", versions[0].SnapshotJson);
        Assert.Equal("Clarified name", versions[0].ChangeSummary);

        // The approver of the latest revision must differ from its author.
        (await qa.PostAsync($"/api/v1/test-cases/{tc.Id}/submit", null)).EnsureSuccessStatusCode();
        var qaEdit = await (await qa.PutJson($"/api/v1/test-cases/{tc.Id}", new SaveTestCaseRequest(edited.Fields with { Objective = "x" }))).Read<TestCaseDetailDto>();
        (await qa.PostAsync($"/api/v1/test-cases/{tc.Id}/submit", null)).EnsureSuccessStatusCode();
        await (await qa.PostJson($"/api/v1/test-cases/{qaEdit.Id}/approve", new ReviewDecision(null))).Problem(HttpStatusCode.Forbidden);

        var featureDetail = await (await dev.GetAsync($"/api/v1/features/{feature.Id}")).Read<FeatureDetailDto>();
        Assert.Equal(1, featureDetail.Testing.TestCaseCount);
        Assert.Equal("MANUAL", featureDetail.Testing.CoverageStatus);
    }

    [Fact(DisplayName = "Clone, bulk update, search and spreadsheet round-trip")]
    public async Task Clone_Bulk_ImportExport()
    {
        var admin = await factory.ClientFor("admin");
        var (_, feature) = await FeatureAsync(admin);
        var original = await (await admin.PostJson("/api/v1/test-cases", new SaveTestCaseRequest(Fields("Original", feature.Id)))).Read<TestCaseDetailDto>(HttpStatusCode.Created);
        var clone = await (await admin.PostJson($"/api/v1/test-cases/{original.Id}/clone", new CloneRequest("Clone"))).Read<TestCaseDetailDto>();
        Assert.NotEqual(original.Number, clone.Number);
        Assert.Equal(2, clone.Fields.Steps.Count);

        var bulk = await (await admin.PostJson("/api/v1/test-cases/bulk", new BulkUpdateRequest([original.Id, clone.Id], Priority: "P1", AutomationStatus: AutomationStatus.Automated, AddTags: ["regression"])))
            .Read<BulkResult>();
        Assert.Equal(2, bulk.Updated);
        var page = await (await admin.GetAsync($"/api/v1/test-cases?featureId={feature.Id}&tag=regression")).Read<PagedResult<TestCaseSummaryDto>>();
        var lower = await (await admin.GetAsync($"/api/v1/test-cases?featureId={feature.Id}&text=oRiGiNaL")).Read<PagedResult<TestCaseSummaryDto>>();
        Assert.Contains(lower.Items, t => t.Name == "Original"); // search is case-insensitive on every provider
        Assert.Equal(2, page.Total);
        Assert.All(page.Items, t => Assert.Equal("P1", t.Priority));

        var detail = await (await admin.GetAsync($"/api/v1/features/{feature.Id}")).Read<FeatureDetailDto>();
        Assert.Equal("AUTOMATED", detail.Testing.CoverageStatus);

        var csv = await admin.GetByteArrayAsync($"/api/v1/test-cases/export?featureId={feature.Id}&format=csv");
        var text = Encoding.UTF8.GetString(csv);
        Assert.Contains("Original", text);
        Assert.Contains("1. Open invoice form => Form opens", text);

        var product = await (await admin.GetAsync($"/api/v1/products/{feature.ProductId}")).Read<ProductDto>();
        var import = $"Name,Type,Priority,Steps,Features,Tags\nImported case,NEGATIVE,P3,\"1. Do a => b\n2. Do c\",{product.Code}:{feature.SequenceNumber},imported\n";
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(import)), "file", "cases.csv" } };
        var result = await (await admin.PostAsync("/api/v1/test-cases/import", form)).Read<ImportResult>();
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Created);
        var imported = (await (await admin.GetAsync("/api/v1/test-cases?tag=imported")).Read<PagedResult<TestCaseSummaryDto>>()).Items.Single();
        var importedDetail = await (await admin.GetAsync($"/api/v1/test-cases/{imported.Id}")).Read<TestCaseDetailDto>();
        Assert.Equal(2, importedDetail.Fields.Steps.Count);
        Assert.Equal(feature.Id, Assert.Single(importedDetail.Features).FeatureId);

        var xlsx = await admin.GetAsync($"/api/v1/test-cases/export?featureId={feature.Id}&format=xlsx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "Suites, folders, shared steps and dependency cycles")]
    public async Task Suites_Folders_SharedSteps()
    {
        var admin = await factory.ClientFor("admin");
        var (productId, feature) = await FeatureAsync(admin);
        var folder = await (await admin.PostJson("/api/v1/test-folders", new FolderUpsert("Invoicing", null))).Read<FolderDto>();
        var shared = await (await admin.PostJson("/api/v1/test-cases", new SaveTestCaseRequest(Fields("Login as sales user", feature.Id) with { IsSharedSteps = true, FolderId = folder.Id })))
            .Read<TestCaseDetailDto>(HttpStatusCode.Created);
        var plain = await (await admin.PostJson("/api/v1/test-cases", new SaveTestCaseRequest(Fields("Plain", feature.Id)))).Read<TestCaseDetailDto>(HttpStatusCode.Created);

        var caller = await (await admin.PostJson("/api/v1/test-cases", new SaveTestCaseRequest(Fields("Uses shared steps", feature.Id) with
        {
            Steps = [new TestStepDto(1, "", null, null, shared.Id), new TestStepDto(2, "Create invoice", "Saved", null, null)],
        }))).Read<TestCaseDetailDto>(HttpStatusCode.Created);
        Assert.Equal("Login as sales user", caller.Fields.Steps[0].SharedStepsName);
        await (await admin.PostJson("/api/v1/test-cases", new SaveTestCaseRequest(Fields("Bad ref", feature.Id) with { Steps = [new TestStepDto(1, "", null, null, plain.Id)] })))
            .Problem(HttpStatusCode.BadRequest);
        await (await admin.DeleteAsync($"/api/v1/test-cases/{shared.Id}")).Problem(HttpStatusCode.Conflict);

        (await admin.PostJson($"/api/v1/test-cases/{caller.Id}/dependencies", new TestCaseDependencyUpsert(plain.Id))).EnsureSuccessStatusCode();
        await (await admin.PostJson($"/api/v1/test-cases/{plain.Id}/dependencies", new TestCaseDependencyUpsert(caller.Id))).Problem(HttpStatusCode.BadRequest);

        var suite = await (await admin.PostJson("/api/v1/test-suites", new SuiteUpsert("Invoicing regression", null, productId, "regression", null))).Read<SuiteDetailDto>();
        Assert.StartsWith("TS-", suite.Suite.NumberDisplay);
        var withItems = await (await admin.PostJson($"/api/v1/test-suites/{suite.Suite.Id}/items", new SuiteItemsRequest([plain.Id, caller.Id, plain.Id]))).Read<SuiteDetailDto>();
        Assert.Equal([plain.Id, caller.Id], withItems.Items.Select(i => i.TestCase.Id));
        var reordered = await (await admin.PutJson($"/api/v1/test-suites/{suite.Suite.Id}/items/order", withItems.Items.Select(i => i.ItemId).Reverse().ToArray())).Read<SuiteDetailDto>();
        Assert.Equal([caller.Id, plain.Id], reordered.Items.Select(i => i.TestCase.Id));

        var inSuite = await (await admin.GetAsync($"/api/v1/test-cases?suiteId={suite.Suite.Id}")).Read<PagedResult<TestCaseSummaryDto>>();
        Assert.Equal(2, inSuite.Total);
        var folders = await (await admin.GetAsync("/api/v1/test-folders")).Read<List<FolderDto>>();
        Assert.Equal(1, folders.Single(f => f.Id == folder.Id).TestCaseCount);
        await (await admin.DeleteAsync($"/api/v1/test-folders/{folder.Id}")).Problem(HttpStatusCode.Conflict);
    }
}
