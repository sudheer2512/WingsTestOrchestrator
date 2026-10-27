using System.Net;
using System.Text;
using Wings.Application.Administration;
using Wings.Application.Common;
using Wings.Application.Features;
using Wings.Domain.Features;
using Wings.Tests.Infrastructure;
using Xunit;

namespace Wings.Tests;

public sealed class FeatureRepositoryTests(WingsApiFactory factory) : IClassFixture<WingsApiFactory>
{
    private async Task<(HttpClient Admin, ProductDto Product)> NewProductAsync(string? scopeKey = null)
    {
        var admin = await factory.ClientFor("admin");
        var code = "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var product = await (await admin.PostJson("/api/v1/products", new ProductUpsert(code, $"Test product {code}", null, SequenceScopeKey: scopeKey))).Read<ProductDto>();
        return (admin, product);
    }

    private static async Task<FeatureDetailDto> Create(HttpClient c, Guid productId, Guid? parentId, string name, string type = FeatureTypes.Feature)
        => await (await c.PostJson("/api/v1/features", new CreateFeatureRequest(productId, parentId, new FeatureFields { Name = name, FeatureTypeCode = type }))).Read<FeatureDetailDto>(HttpStatusCode.Created);

    [Fact(DisplayName = "Scenario 1: a new feature gets a GUID and the next sequence and appears in the tree")]
    public async Task CreateFeature_AssignsIdentityAndSequence()
    {
        var (admin, product) = await NewProductAsync();
        var module = await Create(admin, product.Id, null, "Vehicle Sales", FeatureTypes.Module);
        var enquiries = await Create(admin, product.Id, module.Id, "Enquiries", FeatureTypes.Submodule);
        var feature = await Create(admin, product.Id, enquiries.Id, "Create Enquiry");

        Assert.NotEqual(Guid.Empty, feature.Id);
        Assert.Equal(3, feature.SequenceNumber);
        Assert.Equal("00003", feature.SequenceDisplay);
        Assert.Equal(["Vehicle Sales", "Enquiries"], feature.Ancestors.Select(a => a.Name));
        Assert.Equal("admin", feature.CreatedBy);

        var children = await (await admin.GetAsync($"/api/v1/products/{product.Id}/tree?parentId={enquiries.Id}")).Read<List<FeatureTreeNode>>();
        var node = Assert.Single(children);
        Assert.Equal(feature.Id, node.Id);
        Assert.Equal("NONE", node.CoverageStatus);
        var root = await (await admin.GetAsync($"/api/v1/products/{product.Id}/tree")).Read<List<FeatureTreeNode>>();
        Assert.True(Assert.Single(root).HasChildren);
    }

    [Fact(DisplayName = "Sequences survive reorder and move, and deleted numbers are never reused")]
    public async Task Sequences_AreImmutable_AndNeverReused()
    {
        var (admin, product) = await NewProductAsync();
        var a = await Create(admin, product.Id, null, "A", FeatureTypes.Module);
        var b = await Create(admin, product.Id, null, "B", FeatureTypes.Module);
        var child = await Create(admin, product.Id, a.Id, "Child");

        (await admin.PostJson("/api/v1/features/reorder", new ReorderRequest(product.Id, null, [b.Id, a.Id]))).EnsureSuccessStatusCode();
        var root = await (await admin.GetAsync($"/api/v1/products/{product.Id}/tree")).Read<List<FeatureTreeNode>>();
        Assert.Equal(["B", "A"], root.Select(n => n.Name));
        Assert.Equal([2L, 1L], root.Select(n => n.SequenceNumber));

        var moved = await (await admin.PostJson($"/api/v1/features/{child.Id}/move", new MoveFeatureRequest(b.Id, null, null, child.Version))).Read<FeatureDetailDto>();
        Assert.Equal(child.SequenceNumber, moved.SequenceNumber);
        Assert.Equal(b.Id, moved.ParentId);

        (await admin.DeleteAsync($"/api/v1/features/{moved.Id}")).EnsureSuccessStatusCode();
        var next = await Create(admin, product.Id, null, "After delete");
        Assert.Equal(4, next.SequenceNumber);
    }

    [Fact(DisplayName = "Concurrent creation allocates unique, gap-free sequence numbers")]
    public async Task ConcurrentCreation_AllocatesUniqueNumbers()
    {
        var (admin, product) = await NewProductAsync();
        var results = await Task.WhenAll(Enumerable.Range(1, 20).Select(i => Create(admin, product.Id, null, $"Parallel {i}")));
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i), results.Select(r => r.SequenceNumber).Order());
    }

    [Fact(DisplayName = "Products sharing a numbering scope draw from one sequence; moves across them keep numbers")]
    public async Task SharedScope_AcrossProducts()
    {
        var (admin, p1) = await NewProductAsync();
        var (_, p2) = await NewProductAsync(p1.SequenceScopeKey);
        var f1 = await Create(admin, p1.Id, null, "In P1");
        var f2 = await Create(admin, p2.Id, null, "In P2");
        Assert.Equal(f1.SequenceNumber + 1, f2.SequenceNumber);

        var moved = await (await admin.PostJson($"/api/v1/features/{f1.Id}/move", new MoveFeatureRequest(null, p2.Id, 0, f1.Version))).Read<FeatureDetailDto>();
        Assert.Equal(p2.Id, moved.ProductId);
        Assert.Equal(f1.SequenceNumber, moved.SequenceNumber);

        var (_, p3) = await NewProductAsync();
        await (await admin.PostJson($"/api/v1/features/{f2.Id}/move", new MoveFeatureRequest(null, p3.Id, 0, f2.Version))).Problem(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Scenario 7: features created after a sequence number can be listed")]
    public async Task Search_BySequence()
    {
        var (admin, product) = await NewProductAsync();
        for (var i = 1; i <= 6; i++) await Create(admin, product.Id, null, $"F{i}");
        var page = await (await admin.GetAsync($"/api/v1/features/search?productId={product.Id}&sequenceFrom=4")).Read<PagedResult<FeatureSearchHit>>();
        Assert.Equal(3, page.Total);
        Assert.Equal(["F4", "F5", "F6"], page.Items.Select(h => h.Node.Name));

        var text = await (await admin.GetAsync($"/api/v1/features/search?productId={product.Id}&text=00005")).Read<PagedResult<FeatureSearchHit>>();
        Assert.Equal("F5", Assert.Single(text.Items).Node.Name);
    }

    [Fact(DisplayName = "Legacy imports keep their sequence numbers and the counter moves past them")]
    public async Task Import_PreservesLegacySequences()
    {
        var (admin, product) = await NewProductAsync();
        var csv = "Path,Name,Sequence,Type,CreatedAt,CreatedBy\n" +
                  "Vehicle Sales / Enquiries,Create Enquiry,00125,FEATURE,2019-04-01T10:00:00Z,legacy.user\n" +
                  "Vehicle Sales / Enquiries,Modify Enquiry,126,FEATURE,,\n";
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "features.csv" } };
        var result = await (await admin.PostAsync($"/api/v1/products/{product.Id}/features/import", form)).Read<FeatureImportResult>();
        Assert.Empty(result.Errors);
        Assert.Equal(4, result.Created); // two containers + two features

        var hits = await (await admin.GetAsync($"/api/v1/features/search?productId={product.Id}&text=Enquiry")).Read<PagedResult<FeatureSearchHit>>();
        var create = hits.Items.Single(h => h.Node.Name == "Create Enquiry");
        Assert.Equal(125, create.Node.SequenceNumber);
        Assert.Equal(["Vehicle Sales", "Enquiries"], create.Ancestors.SkipLast(1).Select(a => a.Name));
        var detail = await (await admin.GetAsync($"/api/v1/features/{create.Node.Id}")).Read<FeatureDetailDto>();
        Assert.True(detail.IsLegacySequence);
        Assert.Equal("legacy.user", detail.CreatedBy);
        Assert.Equal(2019, detail.CreatedAt.Year);

        // The two containers created by the import were allocated after the legacy maximum (127, 128).
        Assert.Equal([127L, 128L], create.Ancestors.SkipLast(1).Select(a => long.Parse(a.SequenceDisplay)));
        var next = await Create(admin, product.Id, null, "New after import");
        Assert.Equal(129, next.SequenceNumber);

        using var dupe = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("Path,Name,Sequence\n,Clash,125\n")), "file", "f.csv" } };
        await (await admin.PostAsync($"/api/v1/products/{product.Id}/features/import", dupe)).Problem(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Structural rules: no moving under a descendant, no dependency cycles, stale updates rejected")]
    public async Task Rules_AreEnforced()
    {
        var (admin, product) = await NewProductAsync();
        var parent = await Create(admin, product.Id, null, "Parent", FeatureTypes.Module);
        var child = await Create(admin, product.Id, parent.Id, "Child");
        await (await admin.PostJson($"/api/v1/features/{parent.Id}/move", new MoveFeatureRequest(child.Id, null, null, parent.Version))).Problem(HttpStatusCode.BadRequest);

        var x = await Create(admin, product.Id, null, "Create Customer");
        var y = await Create(admin, product.Id, null, "Create Sales Order");
        var z = await Create(admin, product.Id, null, "Generate Invoice");
        (await admin.PostJson($"/api/v1/features/{y.Id}/dependencies", new DependencyUpsert(x.Id, DependencyType.Prerequisite, null))).EnsureSuccessStatusCode();
        (await admin.PostJson($"/api/v1/features/{z.Id}/dependencies", new DependencyUpsert(y.Id, DependencyType.Prerequisite, null))).EnsureSuccessStatusCode();
        var cycle = await (await admin.PostJson($"/api/v1/features/{x.Id}/dependencies", new DependencyUpsert(z.Id, DependencyType.Prerequisite, null))).Problem(HttpStatusCode.BadRequest);
        Assert.Contains("circular", cycle);

        var fields = new FeatureFields { Name = "Renamed", FeatureTypeCode = FeatureTypes.Feature };
        var updated = await (await admin.PutJson($"/api/v1/features/{x.Id}", new UpdateFeatureRequest(fields, x.Version))).Read<FeatureDetailDto>();
        Assert.Equal("Renamed", updated.Fields.Name);
        await (await admin.PutJson($"/api/v1/features/{x.Id}", new UpdateFeatureRequest(fields with { Name = "Stale" }, x.Version))).Problem(HttpStatusCode.Conflict);
    }

    [Fact(DisplayName = "Scenario 2 and audit: detail shows requirements, dependencies and full change history")]
    public async Task Detail_And_AuditHistory()
    {
        var (admin, product) = await NewProductAsync();
        var f = await Create(admin, product.Id, null, "Generate Invoice");
        (await admin.PostJson($"/api/v1/features/{f.Id}/requirements", new RequirementUpsert("REQ-1", "Apply GST", null))).EnsureSuccessStatusCode();
        var fields = f.Fields with { BusinessRules = "- Tax per HSN", StatusCode = FeatureStatuses.Active, ApplicationType = ApplicationType.WinForms };
        (await admin.PutJson($"/api/v1/features/{f.Id}", new UpdateFeatureRequest(fields, f.Version))).EnsureSuccessStatusCode();

        var detail = await (await admin.GetAsync($"/api/v1/features/{f.Id}")).Read<FeatureDetailDto>();
        Assert.Equal("- Tax per HSN", detail.Fields.BusinessRules);
        Assert.Equal("REQ-1", Assert.Single(detail.Requirements).Code);

        var audit = await (await admin.GetAsync($"/api/v1/features/{f.Id}/audit")).Read<PagedResult<AuditEntryDto>>();
        Assert.Contains(audit.Items, a => a.Action == "Created" && a.EntityType == "Feature");
        Assert.Contains(audit.Items, a => a.Action == "Created" && a.EntityType == "FeatureRequirement");
        var update = Assert.Single(audit.Items, a => a.Action == "Updated" && a.EntityType == "Feature");
        Assert.Contains("BusinessRules", update.ChangesJson);
    }

    [Fact(DisplayName = "Permissions: read-only users cannot change features; product access limits visibility")]
    public async Task Permissions_AndProductAccess()
    {
        var (admin, product) = await NewProductAsync();
        var (_, other) = await NewProductAsync();
        var viewer = await factory.ClientFor("viewer");
        await (await viewer.PostJson("/api/v1/features", new CreateFeatureRequest(product.Id, null, new FeatureFields { Name = "Nope" }))).Problem(HttpStatusCode.Forbidden);
        (await viewer.GetAsync($"/api/v1/products/{product.Id}/tree")).EnsureSuccessStatusCode();

        var users = await (await admin.GetAsync("/api/v1/admin/users")).Read<List<Wings.Api.Controllers.UserSummaryDto>>();
        var tester = users.Single(u => u.UserName == "tester");
        (await admin.PutJson($"/api/v1/admin/users/{tester.Id}/product-access", new[] { new Wings.Api.Controllers.ProductAccessDto(product.Id, false) })).EnsureSuccessStatusCode();

        var testerClient = await factory.ClientFor("tester");
        var visible = await (await testerClient.GetAsync("/api/v1/products")).Read<List<ProductDto>>();
        Assert.Equal(product.Id, Assert.Single(visible).Id);
        await (await testerClient.GetAsync($"/api/v1/products/{other.Id}/tree")).Problem(HttpStatusCode.NotFound);

        (await admin.PutJson($"/api/v1/admin/users/{tester.Id}/product-access", Array.Empty<object>())).EnsureSuccessStatusCode();
        await (await viewer.GetAsync("/api/v1/admin/users")).Problem(HttpStatusCode.Forbidden);
        await (await factory.CreateClient().GetAsync("/api/v1/products")).Problem(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Seeded sample data mirrors the Wings Auto hierarchy")]
    public async Task SampleData_IsSeeded()
    {
        var admin = await factory.ClientFor("admin");
        var products = await (await admin.GetAsync("/api/v1/products")).Read<List<ProductDto>>();
        var auto = products.Single(p => p.Code == "AUTO");
        var hits = await (await admin.GetAsync($"/api/v1/features/search?productId={auto.Id}&text=Create Enquiry")).Read<PagedResult<FeatureSearchHit>>();
        var create = hits.Items.Single(h => h.Node.Name == "Create Enquiry");
        Assert.Equal(3, create.Node.SequenceNumber);
        Assert.Equal(3, create.Node.TestCaseCount);
        var untested = await (await admin.GetAsync($"/api/v1/features/search?productId={auto.Id}&untested=true&pageSize=500")).Read<PagedResult<FeatureSearchHit>>();
        Assert.DoesNotContain(untested.Items, h => h.Node.Id == create.Node.Id);
    }
}
