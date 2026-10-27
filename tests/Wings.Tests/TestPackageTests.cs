using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Wings.Application.Common;
using Wings.Application.Features;
using Wings.Application.Testing;
using Wings.Domain.Features;
using Wings.Domain.Testing;
using Wings.Tests.Infrastructure;
using Xunit;

namespace Wings.Tests;

public sealed class TestPackageTests(WingsApiFactory factory) : IClassFixture<WingsApiFactory>
{
    internal static readonly string SamplesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/automation"));

    internal static byte[] Zip(IDictionary<string, string> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (path, content) in files)
            {
                using var w = new StreamWriter(zip.CreateEntry(path).Open());
                w.Write(content);
            }
        return ms.ToArray();
    }

    internal static byte[] ZipDirectory(string dir, string rootFolder)
        => Zip(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => $"{rootFolder}/{Path.GetRelativePath(dir, f).Replace('\\', '/')}", File.ReadAllText));

    internal static async Task<PackageVersionSummaryDto> Upload(HttpClient client, byte[] content, string fileName, string name, Guid? productId = null, Guid? packageId = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (packageId is { } pid) form.Add(new StringContent(pid.ToString()), "packageId");
        else form.Add(new StringContent(name), "name");
        if (productId is { } prod) form.Add(new StringContent(prod.ToString()), "productId");
        form.Add(new StringContent("abc123"), "sourceCommit");
        return await (await client.PostAsync("/api/v1/test-packages/upload", form)).Read<PackageVersionSummaryDto>(HttpStatusCode.Accepted);
    }

    internal static async Task<PackageVersionDetailDto> WaitForValidation(HttpClient client, Guid versionId)
    {
        for (var i = 0; i < 100; i++)
        {
            var v = await (await client.GetAsync($"/api/v1/test-packages/versions/{versionId}")).Read<PackageVersionDetailDto>();
            if (v.Summary.Status is not (PackageStatus.Uploaded or PackageStatus.Validating)) return v;
            await Task.Delay(200);
        }
        throw new TimeoutException("Validation did not finish.");
    }

    private async Task<(Guid ProductId, ProductDto Auto)> AutoProductAsync(HttpClient admin)
    {
        var products = await (await admin.GetAsync("/api/v1/products")).Read<List<ProductDto>>();
        var auto = products.Single(p => p.Code == "AUTO");
        return (auto.Id, auto);
    }

    [Fact(DisplayName = "Scenario 3: a C# FlaUI project is validated, tests discovered and mapped, approved by a second person, and published")]
    public async Task CSharpProject_FullLifecycle()
    {
        var dev = await factory.ClientFor("dev");
        var qa = await factory.ClientFor("qa");
        var admin = await factory.ClientFor("admin");
        var (autoId, _) = await AutoProductAsync(admin);

        var uploaded = await Upload(dev, ZipDirectory(Path.Combine(SamplesRoot, "WingsAuto.WinForms.UiTests"), "WingsAuto.WinForms.UiTests"), "winforms-tests.zip", "Wings Auto – Enquiry UI tests", autoId);
        Assert.Equal(PackageStatus.Uploaded, uploaded.Status);
        Assert.Equal(64, uploaded.Sha256.Length);

        var v = await WaitForValidation(dev, uploaded.Id);
        Assert.True(v.Summary.Status == PackageStatus.Validated, string.Join("\n", v.Report!.Stages.SelectMany(s => s.Findings).Select(f => $"{f.Severity}: {f.Message} {f.File}")));
        Assert.Equal("FLAUI", v.Summary.DetectedFramework);
        Assert.Equal(ApplicationType.WinForms, v.Summary.Platform);
        Assert.Contains(v.Files, f => f.Path == "CreateEnquiryTests.cs" && f.IsText); // top-level folder stripped
        Assert.Equal(StageStatus.Pending, v.Report!.Stages.Single(s => s.Name == "Compilation").Status);

        Assert.Equal(2, v.Tests.Count);
        var test = v.Tests.Single(t => t.Identifier == "WingsAuto.WinForms.UiTests.CreateEnquiryTests.CreateEnquiry_WithMandatoryFields_SavesAndShowsNumber");
        Assert.Equal("Create enquiry with valid mandatory fields", test.DisplayName);
        Assert.Equal("XUNIT", test.Framework);
        Assert.Equal("Create Enquiry", test.FeatureName); // [Trait("WingsFeature","00003")] auto-mapped
        Assert.True(test.Line > 0);

        var file = await (await dev.GetAsync($"/api/v1/test-packages/versions/{v.Summary.Id}/file?path=CreateEnquiryTests.cs")).Read<PackageFileContentDto>();
        Assert.Equal("csharp", file.Language);
        Assert.Contains("FlaUI", file.Content);

        var mapped = await (await dev.PutJson($"/api/v1/test-packages/versions/{v.Summary.Id}/tests/{test.Id}/mapping", new MapTestRequest(null, test.FeatureId, CreateTestCase: true)))
            .Read<DiscoveredTestDto>();
        Assert.NotNull(mapped.TestCaseId);

        await (await dev.PostJson($"/api/v1/test-packages/versions/{v.Summary.Id}/approve", new ReviewDecision(null))).Problem(HttpStatusCode.Forbidden);
        await (await admin.PostAsync($"/api/v1/test-packages/versions/{v.Summary.Id}/publish", null)).Problem(HttpStatusCode.Conflict); // not approved yet
        var approved = await (await qa.PostJson($"/api/v1/test-packages/versions/{v.Summary.Id}/approve", new ReviewDecision("Reviewed"))).Read<PackageVersionDetailDto>();
        Assert.Equal(PackageStatus.Approved, approved.Summary.Status);
        var published = await (await qa.PostAsync($"/api/v1/test-packages/versions/{v.Summary.Id}/publish", null)).Read<PackageVersionDetailDto>();
        Assert.Equal(PackageStatus.Published, published.Summary.Status);

        var tc = await (await dev.GetAsync($"/api/v1/test-cases/{mapped.TestCaseId}")).Read<TestCaseDetailDto>();
        Assert.Equal(AutomationStatus.Automated, tc.Fields.AutomationStatus);
        Assert.Contains(tc.Automation, a => a.Status == PackageStatus.Published);

        var automation = await (await dev.GetAsync($"/api/v1/features/{test.FeatureId}/automation")).Read<List<FeatureAutomationDto>>();
        Assert.Contains(automation, a => a.VersionId == v.Summary.Id && a.Test.Identifier == test.Identifier);

        var download = await dev.GetAsync($"/api/v1/test-packages/versions/{v.Summary.Id}/download");
        Assert.Equal(uploaded.SizeBytes, (await download.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact(DisplayName = "Editing creates a new immutable version, keeps mappings and leaves the original untouched")]
    public async Task Edit_CreatesNewVersion()
    {
        var dev = await factory.ClientFor("dev");
        var (autoId, _) = await AutoProductAsync(await factory.ClientFor("admin"));
        var v1 = await WaitForValidation(dev, (await Upload(dev, ZipDirectory(Path.Combine(SamplesRoot, "wings-portal-e2e"), "wings-portal-e2e"), "portal.zip", "Portal e2e", autoId)).Id);
        Assert.Equal(PackageStatus.Validated, v1.Summary.Status);
        Assert.Equal("PLAYWRIGHT_TS", v1.Summary.DetectedFramework);
        Assert.Equal(ApplicationType.Browser, v1.Summary.Platform);
        Assert.Equal(3, v1.Tests.Count);
        var happy = v1.Tests.Single(t => t.DisplayName == "portal booking happy path");
        Assert.Equal("tests/online-booking.spec.ts > Online booking @feature-00027 > portal booking happy path", happy.Identifier);
        Assert.Equal("Online Booking", happy.FeatureName);

        var original = await (await dev.GetAsync($"/api/v1/test-packages/versions/{v1.Summary.Id}/file?path=tests/online-booking.spec.ts")).Read<PackageFileContentDto>();
        var newContent = original.Content.Replace("test('rejects an invalid mobile number'", "test('rejects a short mobile number'");
        var pkg = v1.PackageId;
        var v2Summary = await (await dev.PostJson($"/api/v1/test-packages/{pkg}/edits",
            new EditPackageFilesRequest(v1.Summary.Id, [new FileEdit("tests/online-booking.spec.ts", newContent)], "Rename negative test"))).Read<PackageVersionSummaryDto>();
        Assert.Equal(2, v2Summary.VersionNumber);
        Assert.Equal(v1.Summary.Id, v2Summary.ParentVersionId);

        var v2 = await WaitForValidation(dev, v2Summary.Id);
        Assert.Equal(PackageStatus.Validated, v2.Summary.Status);
        Assert.Contains(v2.Tests, t => t.DisplayName == "rejects a short mobile number");
        Assert.Equal(happy.FeatureId, v2.Tests.Single(t => t.Identifier == happy.Identifier).FeatureId);

        var v1Again = await (await dev.GetAsync($"/api/v1/test-packages/versions/{v1.Summary.Id}/file?path=tests/online-booking.spec.ts")).Read<PackageFileContentDto>();
        Assert.Equal(original.Sha256, v1Again.Sha256);
        var versions = await (await dev.GetAsync($"/api/v1/test-packages/{pkg}")).Read<Wings.Api.Controllers.TestPackagesController.PackageDetail>();
        Assert.Equal([2, 1], versions.Versions.Select(x => x.VersionNumber));
    }

    [Theory(DisplayName = "Unsafe or broken uploads fail validation with a clear finding")]
    [InlineData("zip-slip", "../../evil.cs", "class X {}", "Unsafe path")]
    [InlineData("eicar", "readme.txt", "{EICAR}", "EICAR")]
    [InlineData("exe", "tools/runner.exe", "MZ", "not allowed")]
    [InlineData("nested", "inner.zip", "PK", "not allowed")]
    [InlineData("syntax", "Broken.cs", "namespace N { public class C { public void M( { } }", "CS1")]
    [InlineData("csproj", "Bad.csproj", "<Project><PropertyGroup></PropertyGroup", "Invalid project XML")]
    public async Task UnsafeUploads_FailValidation(string name, string path, string content, string expected)
    {
        content = content.Replace("{EICAR}", System.Text.Encoding.ASCII.GetString(Wings.Infrastructure.Packages.BuiltInContentScanner.Eicar));
        var dev = await factory.ClientFor("dev");
        var v = await WaitForValidation(dev, (await Upload(dev, Zip(new Dictionary<string, string> { [path] = content, ["ok/Tests.cs"] = "class T {}" }), $"{name}.zip", name)).Id);
        Assert.Equal(PackageStatus.ValidationFailed, v.Summary.Status);
        Assert.Contains(v.Report!.Stages.SelectMany(s => s.Findings), f => f.Severity == "error" && f.Message.Contains(expected));
        await (await (await factory.ClientFor("qa")).PostJson($"/api/v1/test-packages/versions/{v.Summary.Id}/approve", new ReviewDecision(null))).Problem(HttpStatusCode.Conflict);
    }

    [Fact(DisplayName = "Build output folders are skipped, review warnings raised, and disallowed file types rejected at upload")]
    public async Task Upload_HygieneRules()
    {
        var dev = await factory.ClientFor("dev");
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="NUnit" Version="4.*" /></ItemGroup>
              <Target Name="Evil" BeforeTargets="Build"><Exec Command="curl example.com" /></Target>
            </Project>
            """;
        var v = await WaitForValidation(dev, (await Upload(dev, Zip(new Dictionary<string, string>
        {
            ["Api.Tests.csproj"] = csproj,
            ["InvoiceApiTests.cs"] = "using NUnit.Framework; namespace Api; public class InvoiceApiTests { [Test, Category(\"WingsFeature:12\")] public void Totals() {} [TestCase(1)] public void Rounding(int x) {} }",
            ["bin/Debug/net10.0/Api.Tests.dll"] = "MZ binary",
            ["obj/project.assets.json"] = "{}",
        }), "api-tests.zip", "API tests")).Id);
        Assert.Equal(PackageStatus.Validated, v.Summary.Status);
        Assert.Equal("NUNIT", v.Summary.DetectedFramework);
        Assert.DoesNotContain(v.Files, f => f.Path.StartsWith("bin/") || f.Path.StartsWith("obj/"));
        var findings = v.Report!.Stages.SelectMany(s => s.Findings).ToList();
        Assert.Contains(findings, f => f.Message.Contains("Skipped 2 file(s)"));
        Assert.Contains(findings, f => f.Severity == "warning" && f.Message.Contains("<Exec>"));
        Assert.Contains(findings, f => f.Severity == "warning" && f.Message.Contains("Microsoft.NET.Test.Sdk"));
        Assert.Contains(findings, f => f.Severity == "warning" && f.Message.Contains("Floating package version"));
        Assert.Equal(2, v.Tests.Count);

        using var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "payload.exe" }, { new StringContent("x"), "name" } };
        await (await dev.PostAsync("/api/v1/test-packages/upload", form)).Problem(HttpStatusCode.BadRequest);
        var viewer = await factory.ClientFor("viewer");
        using var form2 = new MultipartFormDataContent { { new ByteArrayContent("x"u8.ToArray()), "file", "a.cs" }, { new StringContent("x"), "name" } };
        await (await viewer.PostAsync("/api/v1/test-packages/upload", form2)).Problem(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "Single-file uploads: pytest module and structured YAML definitions")]
    public async Task SingleFile_Python_And_Structured()
    {
        var dev = await factory.ClientFor("dev");
        var py = "import pytest\n\nclass TestLedger:\n    @pytest.mark.feature(\"00012\")\n    def test_balanced(self):\n        assert True\n\ndef test_top_level():\n    pass\n";
        var pv = await WaitForValidation(dev, (await Upload(dev, Encoding.UTF8.GetBytes(py), "test_ledger.py", "Ledger checks")).Id);
        Assert.Equal(PackageStatus.Validated, pv.Summary.Status);
        Assert.Equal(["test_ledger.py::TestLedger::test_balanced", "test_ledger.py::test_top_level"], pv.Tests.Select(t => t.Identifier));

        var yaml = "kind: WingsTestDefinition\ntests:\n  - id: INV-001\n    name: Invoice posts to ledger\n  - id: INV-002\n    name: Credit note reverses ledger\n";
        var yv = await WaitForValidation(dev, (await Upload(dev, Encoding.UTF8.GetBytes(yaml), "invoice-checks.yaml", "Structured checks")).Id);
        Assert.Equal(PackageStatus.Validated, yv.Summary.Status);
        Assert.Equal("STRUCTURED", yv.Summary.DetectedFramework);
        Assert.Equal(["INV-001", "INV-002"], yv.Tests.Select(t => t.Identifier).Order());

        var list = await (await dev.GetAsync("/api/v1/test-packages?text=Structured")).Read<List<PackageSummaryDto>>();
        Assert.Equal(2, Assert.Single(list).TestCount);
    }
}
