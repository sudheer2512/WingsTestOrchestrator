using System.Text;
using Wings.Application.Abstractions;
using Wings.Application.Quality;
using Wings.Domain.Features;
using Wings.Domain.Quality;
using Wings.Infrastructure.Reports;
using Xunit;

namespace Wings.Tests;

public sealed class QualityUnitTests
{
    [Fact(DisplayName = "Failure signatures ignore volatile values but separate different errors and tests")]
    public void Signatures()
    {
        var a = FailureSignature.Fingerprint(null, "spec.ts > total", "Expected 950 but got 1,000 (order 10045) at 2026-10-27T08:15:02Z id 3f2b1c9e-1d2a-4b5c-8d7e-0a1b2c3d4e5f");
        var b = FailureSignature.Fingerprint(null, "spec.ts > total", "Expected 950 but got 1,000 (order 77) at 2026-10-28T19:01:59Z id 00000000-1111-2222-3333-444444444444");
        Assert.Equal(a, b);
        Assert.NotEqual(a, FailureSignature.Fingerprint(null, "spec.ts > other", "Expected 950 but got 1,000"));
        Assert.NotEqual(a, FailureSignature.Fingerprint(null, "spec.ts > total", "Element #save not found"));
        Assert.Equal(FailureSignature.ClusterKey("Timeout 30000ms exceeded"), FailureSignature.ClusterKey("Timeout 5000ms exceeded"));
        Assert.Equal("timeout <n>ms exceeded | at <path>:<n>", FailureSignature.Normalize("Timeout 30000ms exceeded\n   at /tmp/run-123/spec.ts:4"));
    }

    [Fact(DisplayName = "Defect workflow allows only the documented transitions")]
    public void Workflow()
    {
        Assert.Contains(DefectStatus.Assigned, DefectWorkflow.Allowed(DefectStatus.Open));
        Assert.DoesNotContain(DefectStatus.Closed, DefectWorkflow.Allowed(DefectStatus.Open));
        Assert.Equal([DefectStatus.Reopened], DefectWorkflow.Allowed(DefectStatus.Closed));
        Assert.Contains(DefectStatus.ReadyForRetest, DefectWorkflow.Allowed(DefectStatus.Fixed));
        Assert.DoesNotContain(DefectStatus.Deferred, DefectWorkflow.Unresolved);
    }

    [Theory(DisplayName = "Changed files match source mappings by glob, path suffix, assembly, class, form and database object")]
    [InlineData("src/Sales/Invoicing/InvoiceService.cs", SourceMappingKind.File, "src/Sales/**", true)]
    [InlineData("src/Sales/Invoicing/InvoiceService.cs", SourceMappingKind.File, "src/Sales/*.cs", false)]
    [InlineData("src/Sales/Invoicing/InvoiceService.cs", SourceMappingKind.File, "Invoicing/InvoiceService.cs", true)]
    [InlineData("src/Wings.Sales/Invoicing/InvoiceService.cs", SourceMappingKind.Assembly, "Wings.Sales", true)]
    [InlineData("src/Sales/Invoicing/InvoiceService.cs", SourceMappingKind.Class, "Wings.Sales.InvoiceService", true)]
    [InlineData("src/Sales/Invoicing/InvoiceServiceTests.cs", SourceMappingKind.Class, "InvoiceService", false)]
    [InlineData("src/Sales/Forms/frmInvoice.Designer.cs", SourceMappingKind.Form, "frmInvoice", true)]
    [InlineData("db/procs/dbo.usp_PostInvoice.sql", SourceMappingKind.StoredProcedure, "dbo.usp_PostInvoice", true)]
    [InlineData("db/tables/Invoice.sql", SourceMappingKind.DatabaseTable, "[dbo].[Invoice]", true)]
    [InlineData("db/tables/InvoiceLine.sql", SourceMappingKind.DatabaseTable, "Invoice", false)]
    public void SourceMatching(string file, SourceMappingKind kind, string value, bool expected) => Assert.Equal(expected, BuildService.Matches(file, kind, value));

    [Fact(DisplayName = "TFVC server paths and Git paths normalise to repository-relative paths; commits reference features")]
    public void SourceAdapters()
    {
        var tfvc = new TfvcSourceAdapter();
        Assert.Equal("src/Sales/Invoice.cs", tfvc.NormalizePath("$/WingsAuto/Main/src/Sales/Invoice.cs", new SourceRepository { Url = "$/WingsAuto/Main" }));
        Assert.Equal("src/Sales/Invoice.cs", tfvc.NormalizePath("$/WingsAuto/Dev/src/Sales/Invoice.cs", null));
        Assert.Equal("src/a.cs", new GitSourceAdapter().NormalizePath(".\\src\\a.cs", null));
        Assert.Equal([27L, 31L, 150L], BuildService.FeatureRefs("Fix rounding (feature 27), see [F31] and AUTO-150", "AUTO").Order().ToArray());
    }

    [Fact(DisplayName = "The PDF writer produces a well-formed, multi-page document with a valid cross-reference table")]
    public void Pdf()
    {
        var doc = new PdfDocumentModel { Title = "Release testing report: Wings Auto 2026.3 – €, “quotes” & (parens)", Subtitle = "Signed off", Footer = "Wings" };
        doc.Fields([("Readiness", "Ready → shipped ≥ 95%")]).Heading("Gates")
            .Table(["Gate", "Status", "Detail"], Enumerable.Range(1, 120).Select(i => (IReadOnlyList<string?>)[$"Gate {i}", "Passed", new string('x', 300)]));
        var bytes = new PdfRenderer().Render(doc);
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        var pages = int.Parse(System.Text.RegularExpressions.Regex.Match(text, @"/Count (\d+)").Groups[1].Value);
        Assert.True(pages > 3);
        // Every xref offset points at the start of its object.
        var xref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        var entries = text[xref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ")).ToList();
        for (var i = 0; i < entries.Count; i++) Assert.StartsWith($"{i + 1} 0 obj", text[int.Parse(entries[i][..10])..]);
    }

    [Fact(DisplayName = "Suggested defect titles use the assertion's expected/received lines, not locator noise")]
    public void Titles()
    {
        var playwright = "Error: expect(locator).toHaveText(expected) failed\n\nLocator:  getByTestId('payable-amount')\nExpected: \"₹11,87,500\"\nReceived: \"₹12,45,000\"\nTimeout:  5000ms";
        Assert.Equal("festival offer: Expected: \"₹11,87,500\", Received: \"₹12,45,000\"", DefectService.SuggestTitle("festival offer", playwright));
        Assert.Equal("Save invoice: Element #save not found", DefectService.SuggestTitle("Save invoice", "Element #save not found\n   at Page.Click()"));
        Assert.Equal("Save invoice fails", DefectService.SuggestTitle("Save invoice", null));
    }

    [Fact(DisplayName = "Notification templates fill placeholders")]
    public void Templates()
    {
        var ev = new NotificationEvent { Type = "run.completed", Severity = "Warning", Title = "RUN-00007 completed", Body = "2 failed", CreatedAt = new DateTimeOffset(2026, 10, 27, 9, 30, 0, TimeSpan.Zero), DataJson = """{"build":"2026.3.1"}""" };
        Assert.Equal("[Wings QA] Warning: RUN-00007 completed", NotificationTemplates.Render(NotificationTemplates.DefaultSubject, ev, null));
        Assert.Equal("build 2026.3.1 at 2026-10-27 09:30 UTC: https://x/runs/1 {{unknown}}", NotificationTemplates.Render("build {{data.build}} at {{time}}: {{link}} {{unknown}}", ev, "https://x/runs/1"));
        Assert.True(NotificationPublisher.Matches("defect.*, run.completed", "defect.assigned"));
        Assert.False(NotificationPublisher.Matches("run.completed", "run.criticalFailure"));
    }
}
