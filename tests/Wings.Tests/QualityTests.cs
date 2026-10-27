using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wings.Agent.Contracts;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Application.Features;
using Wings.Application.Quality;
using Wings.Application.Testing;
using Wings.Domain.Features;
using Wings.Domain.Orchestration;
using Wings.Domain.Quality;
using Wings.Domain.Testing;
using Wings.Infrastructure.Persistence;
using Wings.Tests.Infrastructure;
using Xunit;

namespace Wings.Tests;

/// <summary>Captures outbound HTTP (issue trackers, Teams, webhooks) and answers like the real services would.</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    public sealed record Captured(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);
    public ConcurrentQueue<Captured> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Enqueue(new Captured(request.Method, request.RequestUri!, headers, body));
        var json = request.RequestUri!.Host switch
        {
            "api.github.test" => """{"number":42,"html_url":"https://github.test/wings/auto/issues/42"}""",
            _ => "{}",
        };
        return new HttpResponseMessage(request.RequestUri.Host == "api.github.test" ? HttpStatusCode.Created : HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

public sealed class QualityFactory : WingsApiFactory
{
    public RecordingHandler Outbound { get; } = new();
    public string MailDirectory { get; } = Path.Combine(Path.GetTempPath(), "wings-mail", Guid.NewGuid().ToString("N"));

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings =>
    [
        new("Notifications:MonitorSeconds", "1"),
        new("Notifications:Smtp:PickupDirectory", MailDirectory),
        new("Notifications:PublicUrl", "https://wto.test"),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddHttpClient(IssueTrackerService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Outbound).SetHandlerLifetime(Timeout.InfiniteTimeSpan));
    }

    public async Task DbAsync(Func<WingsDbContext, Task> work)
    {
        using var scope = Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<WingsDbContext>());
    }
}

public sealed class QualityTests(QualityFactory factory) : IClassFixture<QualityFactory>
{
    // ---- helpers ----

    private static async Task Eventually(Func<Task<bool>> condition, string what, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            if (await condition()) return;
            await Task.Delay(200);
        }
        Assert.Fail($"Timed out waiting for: {what}");
    }

    private sealed record Setup(ProductDto Product, ProductVersionDto Release, FeatureDetailDto FeatureA, FeatureDetailDto FeatureB);

    private async Task<Setup> ProductAsync(HttpClient admin, bool withRelease = true)
    {
        var code = "Q" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var product = await (await admin.PostJson("/api/v1/products", new ProductUpsert(code, $"Quality {code}", null))).Read<ProductDto>();
        var release = await (await admin.PostJson($"/api/v1/products/{product.Id}/releases", new ProductVersionUpsert("2026.3", null, false, null))).Read<ProductVersionDto>();
        var module = await (await admin.PostJson("/api/v1/features", new CreateFeatureRequest(product.Id, null, new FeatureFields { Name = "Accounts", FeatureTypeCode = FeatureTypes.Module })))
            .Read<FeatureDetailDto>(HttpStatusCode.Created);
        var a = await (await admin.PostJson("/api/v1/features", new CreateFeatureRequest(product.Id, module.Id, new FeatureFields { Name = "Post invoice", IntroducedInVersionId = withRelease ? release.Id : null })))
            .Read<FeatureDetailDto>(HttpStatusCode.Created);
        var b = await (await admin.PostJson("/api/v1/features", new CreateFeatureRequest(product.Id, module.Id, new FeatureFields { Name = "GST calculation" }))).Read<FeatureDetailDto>(HttpStatusCode.Created);
        return new Setup(product, release, a, b);
    }

    private static async Task<TestCaseDetailDto> CaseAsync(HttpClient c, string name, Guid featureId, string priority = "P2", params string[] tags)
        => await (await c.PostJson("/api/v1/test-cases", new SaveTestCaseRequest(new TestCaseFields
        {
            Name = name, FeatureIds = [featureId], PrimaryFeatureId = featureId, Priority = priority, Tags = tags, ExpectedResults = "Invoice total is 950.00",
            Steps = [new TestStepDto(1, "Open the invoice form", "Form opens", null, null), new TestStepDto(2, "Apply a 5% discount to 1,000.00", "Total shows 950.00", null, null)],
        }))).Read<TestCaseDetailDto>(HttpStatusCode.Created);

    private static async Task<(Guid RunId, Dictionary<Guid, Guid> TestByCase)> ManualRunAsync(HttpClient c, params Guid[] testCaseIds)
    {
        var run = await (await c.PostJson("/api/v1/runs", new StartRunRequest { TestCaseIds = testCaseIds, IncludeManual = true })).Read<RunSummaryDto>(HttpStatusCode.Created);
        var detail = await ExecutionFixture.RunAsync(c, run.Id);
        return (run.Id, detail.Tests.ToDictionary(t => t.TestCaseId!.Value, t => t.Id));
    }

    private static async Task RecordAsync(HttpClient c, Guid runId, Guid testId, string outcome, string? comment = null)
        => (await c.PostJson($"/api/v1/runs/{runId}/tests/{testId}/manual", new ManualResultRequest(outcome, comment))).EnsureSuccessStatusCode();

    private static async Task<DefectDetailDto> DefectAsync(HttpClient c, Guid id) => await (await c.GetAsync($"/api/v1/defects/{id}")).Read<DefectDetailDto>();

    private async Task<HttpClient> CiClientAsync()
    {
        var admin = await factory.ClientFor("admin");
        var key = await (await admin.PostJson("/api/v1/integrations/keys", new { name = $"ci-{Guid.NewGuid():N}"[..12] })).Read<IntegrationKeyResult>();
        var ci = factory.CreateClient();
        ci.DefaultRequestHeaders.Add("X-Wings-Integration-Key", key.Secret);
        return ci;
    }

    // ---- defects ----

    [Fact(DisplayName = "Scenario 9: a failure raises a linked defect; repeats attach to it instead of duplicating; retest verifies; recurrence reopens")]
    public async Task Defects_FromFailures_NoDuplicates_RetestAndRecurrence()
    {
        var admin = await factory.ClientFor("admin");
        var tester = await factory.ClientFor("tester");
        var setup = await ProductAsync(admin);
        var tc = await CaseAsync(admin, "Invoice discount", setup.FeatureA.Id, "P1");

        var (run1, tests1) = await ManualRunAsync(tester, tc.Id);
        await RecordAsync(tester, run1, tests1[tc.Id], "Failed", "Total shows 1,000.00 instead of 950.00 for order 10045");

        var suggestions = await (await tester.GetAsync($"/api/v1/defects/suggestions?runId={run1}")).Read<List<FailureSuggestionDto>>();
        var s = Assert.Single(suggestions);
        Assert.Null(s.ExistingDefectId);
        Assert.Equal(DefectSeverity.Critical, s.SuggestedSeverity); // P1 test case
        Assert.Contains("Invoice discount", s.SuggestedTitle);

        await (await factory.CreateClient().PostJson("/api/v1/defects/from-failure", new CreateFromFailureRequest(s.TestExecutionId))).Problem(HttpStatusCode.Unauthorized);
        await (await (await factory.ClientFor("viewer")).PostJson("/api/v1/defects/from-failure", new CreateFromFailureRequest(s.TestExecutionId))).Problem(HttpStatusCode.Forbidden);
        var created = await (await tester.PostJson("/api/v1/defects/from-failure", new CreateFromFailureRequest(s.TestExecutionId))).Read<CreateFromFailureResult>();
        Assert.False(created.LinkedToExisting);
        var d = created.Defect;
        Assert.StartsWith("DEF-", d.Summary.NumberDisplay);
        Assert.Equal(setup.Product.Id, d.Summary.ProductId);
        Assert.Equal(setup.FeatureA.Id, d.Summary.FeatureId);
        Assert.Equal(tc.Id, d.Summary.TestCaseId);
        Assert.Equal(run1, d.FailingRunId);
        Assert.Contains("Apply a 5% discount", d.ReproductionSteps);
        Assert.Contains("950.00", d.ExpectedResult);
        Assert.Contains("1,000.00", d.ActualResult);
        Assert.Equal(DefectStatus.Open, d.Summary.Status);

        // The same failure again (different order number): attached automatically when the run finishes, not a new defect.
        var (run2, tests2) = await ManualRunAsync(tester, tc.Id);
        await RecordAsync(tester, run2, tests2[tc.Id], "Failed", "Total shows 1,000.00 instead of 950.00 for order 20977");
        await Eventually(async () => (await DefectAsync(tester, d.Summary.Id)).Summary.OccurrenceCount == 2, "recurrence attached");
        var again = await (await tester.GetAsync($"/api/v1/defects/suggestions?runId={run2}")).Read<List<FailureSuggestionDto>>();
        Assert.Equal(d.Summary.Id, Assert.Single(again).ExistingDefectId);
        var linked = await (await tester.PostJson("/api/v1/defects/from-failure", new CreateFromFailureRequest(again[0].TestExecutionId))).Read<CreateFromFailureResult>();
        Assert.True(linked.LinkedToExisting);
        Assert.Equal(d.Summary.Id, linked.Defect.Summary.Id);
        Assert.Equal(2, linked.Defect.Summary.OccurrenceCount);
        Assert.Equal(1, (await (await tester.GetAsync($"/api/v1/defects?productId={setup.Product.Id}")).Read<PagedResult<DefectSummaryDto>>()).Total);

        // Workflow: invalid jumps are refused; assignment notifies the assignee in-app.
        await (await tester.PostJson($"/api/v1/defects/{d.Summary.Id}/transition", new DefectTransition(DefectStatus.Verified))).Problem(HttpStatusCode.Conflict);
        await (await tester.PostJson($"/api/v1/defects/{d.Summary.Id}/transition", new DefectTransition(DefectStatus.Assigned, AssignedTo: "nobody"))).Problem(HttpStatusCode.BadRequest);
        (await tester.PostJson($"/api/v1/defects/{d.Summary.Id}/transition", new DefectTransition(DefectStatus.Assigned, AssignedTo: "dev"))).EnsureSuccessStatusCode();
        var dev = await factory.ClientFor("dev");
        var inbox = await (await dev.GetAsync("/api/v1/notifications/inbox")).Read<InboxDto>();
        Assert.Contains(inbox.Items, i => i.Type == NotificationEvents.DefectAssigned && i.Title.Contains(d.Summary.NumberDisplay));
        (await dev.PostJson($"/api/v1/defects/{d.Summary.Id}/transition", new DefectTransition(DefectStatus.InProgress))).EnsureSuccessStatusCode();
        (await dev.PostJson($"/api/v1/defects/{d.Summary.Id}/transition", new DefectTransition(DefectStatus.Fixed, RootCause: "Discount applied after tax rounding", FixVersionId: setup.Release.Id))).EnsureSuccessStatusCode();

        // Ready for retest with an automatic retest run; the passing retest verifies the fix.
        var ready = await (await dev.PostJson($"/api/v1/defects/{d.Summary.Id}/transition", new DefectTransition(DefectStatus.ReadyForRetest, Retest: true))).Read<DefectDetailDto>();
        var retest = Assert.Single(ready.Retests);
        Assert.Contains(d.Summary.NumberDisplay, retest.Name);
        var retestTests = (await ExecutionFixture.RunAsync(tester, retest.Id)).Tests;
        await RecordAsync(tester, retest.Id, retestTests.Single().Id, "Passed");
        await Eventually(async () => (await DefectAsync(tester, d.Summary.Id)).Summary.Status == DefectStatus.Verified, "retest verified the defect");
        var verified = await DefectAsync(tester, d.Summary.Id);
        Assert.Contains(retest.NumberDisplay, verified.VerificationResult);
        Assert.Contains(verified.History, h => h.ToStatus == DefectStatus.Verified && h.UserName == "system");
        (await tester.PostJson($"/api/v1/defects/{d.Summary.Id}/transition", new DefectTransition(DefectStatus.Closed))).EnsureSuccessStatusCode();

        // The same failure after closure reopens it.
        var (run3, tests3) = await ManualRunAsync(tester, tc.Id);
        await RecordAsync(tester, run3, tests3[tc.Id], "Failed", "Total shows 1,000.00 instead of 950.00 for order 31002");
        await Eventually(async () => (await DefectAsync(tester, d.Summary.Id)).Summary.Status == DefectStatus.Reopened, "recurrence reopened the defect");
        Assert.Contains((await (await dev.GetAsync("/api/v1/notifications/inbox")).Read<InboxDto>()).Items, i => i.Type == NotificationEvents.DefectRecurred);

        var clusters = await (await tester.GetAsync($"/api/v1/defects/clusters?productId={setup.Product.Id}")).Read<List<FailureClusterDto>>();
        var cluster = Assert.Single(clusters);
        Assert.Equal(3, cluster.Failures);
        Assert.Equal([d.Summary.NumberDisplay], cluster.DefectNumbers);
        var recurring = await (await tester.GetAsync($"/api/v1/defects/recurring?productId={setup.Product.Id}")).Read<List<RecurringFailureDto>>();
        Assert.Equal(3, Assert.Single(recurring).Failures);
    }

    [Fact(DisplayName = "Defects are pushed to an external tracker through its adapter")]
    public async Task Defects_PushToGitHub()
    {
        var admin = await factory.ClientFor("admin");
        var setup = await ProductAsync(admin);
        var d = await (await admin.PostJson("/api/v1/defects", new DefectUpsert { Title = "Ledger export drops the last row", FeatureId = setup.FeatureB.Id, Severity = DefectSeverity.Major, Priority = "P2" }))
            .Read<DefectDetailDto>(HttpStatusCode.Created);
        var tracker = await (await admin.PostJson("/api/v1/integrations/trackers", new TrackerUpsert("GitHub (test)", TrackerKinds.GitHub, "wings/auto", "https://api.github.test", null, "ghp_secret", null)))
            .Read<TrackerDto>();
        Assert.True(tracker.HasToken);
        var pushed = await (await admin.PostJson($"/api/v1/defects/{d.Summary.Id}/push", new { connectionId = tracker.Id })).Read<DefectDetailDto>();
        Assert.Equal("wings/auto#42", pushed.Summary.ExternalKey);
        Assert.Equal("https://github.test/wings/auto/issues/42", pushed.Summary.ExternalUrl);
        var request = factory.Outbound.Requests.Last(r => r.Uri.Host == "api.github.test");
        Assert.Equal("/repos/wings/auto/issues", request.Uri.AbsolutePath);
        Assert.Equal("Bearer ghp_secret", request.Headers["Authorization"]);
        Assert.Contains("Ledger export drops the last row", request.Body);
        Assert.Contains("https://wto.test/defects/", request.Body);
        await (await admin.PostJson($"/api/v1/defects/{d.Summary.Id}/push", new { connectionId = tracker.Id })).Problem(HttpStatusCode.Conflict); // already linked
    }

    // ---- releases and gates ----

    [Fact(DisplayName = "Scenario 8: release scope, gates, exception with justification, validation run and signed-off report")]
    public async Task Release_GatesExceptionsAndSignOff()
    {
        var admin = await factory.ClientFor("admin");
        var qa = await factory.ClientFor("qa");
        var tester = await factory.ClientFor("tester");
        var setup = await ProductAsync(admin);
        var post = await CaseAsync(admin, "Post invoice to ledger", setup.FeatureA.Id, "P1", "accounting");
        var gst = await CaseAsync(admin, "GST on discounted invoice", setup.FeatureB.Id);
        var smoke = await (await admin.PostJson("/api/v1/test-suites", new SuiteUpsert("Smoke", null, setup.Product.Id, "smoke", null))).Read<SuiteDetailDto>();
        (await admin.PostJson($"/api/v1/test-suites/{smoke.Suite.Id}/items", new SuiteItemsRequest([post.Id]))).EnsureSuccessStatusCode();

        var releaseId = setup.Release.Id;
        var ws = await (await qa.PostJson($"/api/v1/releases/{releaseId}/changes", new ReleaseChangeUpsert(setup.FeatureB.Id, FeatureChangeKind.Modified, RiskLevel.High, "Rounding change"))).Read<ReleaseWorkspaceDto>();
        Assert.Equal(1, ws.Metrics.FeaturesAdded);   // introduced in this release
        Assert.Equal(1, ws.Metrics.FeaturesModified); // declared
        Assert.Equal(2, ws.Metrics.TestsRequired);
        Assert.Contains("No quality gates are configured for this product.", ws.Risks);

        async Task Gate(string name, GateType type, string? parameter, bool blocking = true)
            => (await qa.PostJson("/api/v1/quality-gates", new GateUpsert(setup.Product.Id, null, name, type, parameter, blocking))).EnsureSuccessStatusCode();
        await Gate("Smoke passed", GateType.SuitePassed, smoke.Suite.Id.ToString());
        await Gate("No critical defects", GateType.NoOpenDefects, "Critical");
        await Gate("High-risk changes tested", GateType.ChangedFeaturesTested, "High");
        await Gate("Accounting validation", GateType.TaggedTestsPassed, "Accounting");
        await Gate("Automation 50%", GateType.MinAutomationCoverage, "50", blocking: false);
        await (await qa.PostJson("/api/v1/quality-gates", new GateUpsert(setup.Product.Id, null, "Bad", GateType.MinPassRate, "150"))).Problem(HttpStatusCode.BadRequest);
        await (await tester.PostJson("/api/v1/quality-gates", new GateUpsert(setup.Product.Id, null, "x", GateType.NoOpenDefects, null))).Problem(HttpStatusCode.Forbidden);

        ws = await (await qa.GetAsync($"/api/v1/releases/{releaseId}")).Read<ReleaseWorkspaceDto>();
        Assert.Equal(Readiness.Blocked, ws.Release.Readiness);
        Assert.Equal(GateStatuses.Passed, ws.Gates.Single(g => g.Type == GateType.NoOpenDefects).Status);
        Assert.Equal(GateStatuses.Warning, ws.Gates.Single(g => g.Type == GateType.MinAutomationCoverage).Status);
        Assert.Contains("Denominator", ws.Gates.Single(g => g.Type == GateType.SuitePassed).Definition);
        await (await qa.PostJson($"/api/v1/releases/{releaseId}/signoff", new SignOffRequest(ReleaseDecision.Approved, "Ship it"))).Problem(HttpStatusCode.Conflict);

        // Execute the release scope: every required test, manual ones pending for testers.
        var run = await (await qa.PostJson($"/api/v1/releases/{releaseId}/execute", new ExecuteReleaseRequest())).Read<RunSummaryDto>();
        var tests = (await ExecutionFixture.RunAsync(tester, run.Id)).Tests.ToDictionary(t => t.TestCaseId!.Value, t => t.Id);
        Assert.Equal(2, tests.Count);
        await RecordAsync(tester, run.Id, tests[post.Id], "Passed");
        await RecordAsync(tester, run.Id, tests[gst.Id], "Failed", "GST computed on the undiscounted price");

        // The finished run re-evaluates the gates and reports the failing one.
        await Eventually(async () => (await (await admin.GetAsync("/api/v1/notifications/log")).Read<List<EventLogDto>>())
            .Any(e => e.Type == NotificationEvents.GateFailed && e.Link == $"/releases/{releaseId}"), "gate failure notification");
        ws = await (await qa.GetAsync($"/api/v1/releases/{releaseId}")).Read<ReleaseWorkspaceDto>();
        Assert.Equal(ReleaseStatus.Testing, ws.Release.Status);
        Assert.Contains(ws.Runs, r => r.Id == run.Id);
        Assert.Equal(GateStatuses.Passed, ws.Gates.Single(g => g.Type == GateType.SuitePassed).Status);
        Assert.Equal(GateStatuses.Passed, ws.Gates.Single(g => g.Type == GateType.TaggedTestsPassed).Status);
        var changed = ws.Gates.Single(g => g.Type == GateType.ChangedFeaturesTested);
        Assert.Equal(GateStatuses.Failed, changed.Status);
        Assert.Equal((0, 1), (changed.Numerator, changed.Denominator));
        Assert.Equal(2, ws.Metrics.TestsCompleted);
        Assert.Equal(50, ws.Metrics.PassRate);
        Assert.Equal("Failed", ws.Features.Single(f => f.FeatureId == setup.FeatureB.Id).Status);
        Assert.Single(ws.Environments);

        // An exception needs releases.approve and a real justification; it is audited.
        await (await tester.PostJson($"/api/v1/releases/{releaseId}/waivers", new { gateId = changed.GateId, justification = "Accepted risk for hotfix release." })).Problem(HttpStatusCode.Forbidden);
        await (await qa.PostJson($"/api/v1/releases/{releaseId}/waivers", new { gateId = changed.GateId, justification = "ok" })).Problem(HttpStatusCode.BadRequest);
        ws = await (await qa.PostJson($"/api/v1/releases/{releaseId}/waivers", new { gateId = changed.GateId, justification = "GST rounding defect accepted by finance for 2026.3; fixed in 2026.3.1." }))
            .Read<ReleaseWorkspaceDto>();
        Assert.Equal(GateStatuses.Waived, ws.Gates.Single(g => g.GateId == changed.GateId).Status);
        Assert.Equal(Readiness.Warning, ws.Release.Readiness);

        // CI asks whether the candidate may be promoted.
        var ci = await CiClientAsync();
        var check = await (await ci.GetAsync($"/api/v1/ci/gates?product={setup.Product.Code}&release=2026.3&enforce=true")).Read<GateCheckDto>();
        Assert.False(check.Blocked);
        Assert.Equal(Readiness.Warning, check.Readiness);
        await (await factory.CreateClient().GetAsync($"/api/v1/ci/gates?product={setup.Product.Code}&release=2026.3")).Problem(HttpStatusCode.Unauthorized);
        await (await qa.GetAsync($"/api/v1/ci/gates?product={setup.Product.Code}&release=2026.3")).Problem(HttpStatusCode.Unauthorized); // a user token is not an integration key

        var signed = await (await qa.PostJson($"/api/v1/releases/{releaseId}/signoff", new SignOffRequest(ReleaseDecision.Approved, "Approved with the GST exception."))).Read<ReleaseWorkspaceDto>();
        Assert.Equal(ReleaseStatus.SignedOff, signed.Release.Status);
        var approval = Assert.Single(signed.Approvals);
        Assert.Equal("qa", approval.ApprovedBy);
        var pdf = await qa.GetByteArrayAsync($"/api/v1/releases/{releaseId}/approvals/{approval.Id}/report");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Equal(approval.ReportSha256, Convert.ToHexStringLower(SHA256.HashData(pdf)));
        var audit = await (await admin.GetAsync($"/api/v1/audit?entityType=GateWaiver")).Content.ReadAsStringAsync();
        Assert.Contains("ExceptionApproved", audit);
    }

    // ---- source and builds ----

    [Fact(DisplayName = "Builds map changed files and commit references to features, include dependents, and run the affected automated tests")]
    public async Task Builds_ChangeImpact_TriggersAffectedTests()
    {
        var admin = await factory.ClientFor("admin");
        var dev = await factory.ClientFor("dev");
        var version = await ExecutionFixture.PublishedPortalPackageAsync(factory);
        var automatedCase = (await (await dev.PutJson($"/api/v1/test-packages/versions/{version.Summary.Id}/tests/{version.Tests[0].Id}/mapping",
            new MapTestRequest(null, version.Tests[0].FeatureId, CreateTestCase: true))).Read<DiscoveredTestDto>()).TestCaseId!.Value;
        var bookingId = version.Tests[0].FeatureId!.Value;
        var booking = await (await admin.GetAsync($"/api/v1/features/{bookingId}")).Read<FeatureDetailDto>();

        var mapping = await (await admin.PostJson($"/api/v1/features/{bookingId}/source-mappings", new SourceMappingUpsert(SourceMappingKind.File, "portal/booking/**", null, null))).Read<SourceMappingDto>();
        (await admin.PostAsync($"/api/v1/features/{bookingId}/source-mappings/{mapping.Id}/approve", null)).EnsureSuccessStatusCode();
        var email = await (await admin.PostJson("/api/v1/features", new CreateFeatureRequest(booking.ProductId, booking.ParentId, new FeatureFields { Name = "Booking confirmation email" })))
            .Read<FeatureDetailDto>(HttpStatusCode.Created);
        (await admin.PostJson($"/api/v1/features/{email.Id}/dependencies", new DependencyUpsert(bookingId, DependencyType.Prerequisite, null))).EnsureSuccessStatusCode();
        var repo = await (await admin.PostJson("/api/v1/integrations/repositories", new RepositoryUpsert($"WingsAuto-{Guid.NewGuid():N}"[..20], VcsType.Tfvc, "$/WingsAuto/Main", booking.ProductId, null)))
            .Read<RepositoryDto>();

        // What-if analysis from the impact screen (Git paths), nothing recorded.
        var whatIf = await (await dev.PostJson("/api/v1/integrations/impact", new BuildReport { Product = "AUTO", BuildNumber = "local", ChangedFiles = ["portal/booking/new/index.html", "docs/readme.md"] }))
            .Read<BuildImpactDto>();
        Assert.Null(whatIf.Build);
        Assert.Contains(whatIf.Features, f => f.FeatureId == bookingId && f.Reason == ImpactReason.SourceMapping);
        Assert.Contains(whatIf.Features, f => f.FeatureId == email.Id && f.Reason == ImpactReason.Dependency);
        Assert.Equal(["docs/readme.md"], whatIf.UnmappedFiles);
        Assert.Contains(whatIf.Tests, t => t.TestCaseId == automatedCase && t.IsAutomated);

        // CI reports a TFVC build; impacted automated tests start immediately.
        var ci = await CiClientAsync();
        var build = await (await ci.PostJson("/api/v1/ci/builds", new BuildReport
        {
            Product = "AUTO", BuildNumber = "2026.3.0.117", Repository = repo.Name, Branch = "Main", Trigger = "affected",
            ChangedFiles = ["$/WingsAuto/Main/portal/booking/new/index.html"],
            Commits = [new CommitReport("C1187", "dev", $"Fix festival discount rounding (feature {booking.SequenceNumber})", null, ["$/WingsAuto/Main/portal/booking/new/index.html"])],
        })).Read<BuildImpactDto>();
        Assert.NotNull(build.Build);
        Assert.Equal(["portal/booking/new/index.html"], build.ChangedFiles); // TFVC server path made repository-relative
        Assert.Contains(build.Features, f => f.FeatureId == bookingId && f.Reason == ImpactReason.SourceMapping);
        Assert.Contains(build.Features, f => f.FeatureId == email.Id && f.Reason == ImpactReason.Dependency);
        Assert.NotNull(build.TriggeredRun);
        var run = await ExecutionFixture.RunAsync(dev, build.TriggeredRun!.Id);
        Assert.Equal("2026.3.0.117", run.Run.BuildVersion);
        Assert.Contains(run.Tests, t => t.TestCaseId == automatedCase);
        Assert.StartsWith("build:2026.3.0.117", run.Run.TriggeredBy);

        var changes = await (await dev.GetAsync($"/api/v1/integrations/features/{bookingId}/changes")).Read<List<FeatureChangeDto>>();
        Assert.Contains(changes, c => c.CommitId == "C1187" && c.BuildNumber == "2026.3.0.117");
        var listed = await (await dev.GetAsync($"/api/v1/integrations/builds?productId={booking.ProductId}")).Read<List<BuildSummaryDto>>();
        Assert.Contains(listed, b => b.Id == build.Build.Id && b.RepositoryName == repo.Name && b.TriggeredRunNumber == build.TriggeredRun.NumberDisplay);
        await (await ci.PostJson("/api/v1/ci/builds", new BuildReport { Product = "NOPE", BuildNumber = "1", ChangedFiles = ["a"] })).Problem(HttpStatusCode.BadRequest);
        await (await admin.PostAsync($"/api/v1/runs/{build.TriggeredRun.Id}/cancel", null)).Content.ReadAsStringAsync();
    }

    // ---- notifications ----

    [Fact(DisplayName = "Notifications reach subscribers in-app and channels (signed webhook, Teams card, email), and escalate when unacknowledged")]
    public async Task Notifications_Channels_Subscriptions_Escalation()
    {
        var admin = await factory.ClientFor("admin");
        var tester = await factory.ClientFor("tester");
        var setup = await ProductAsync(admin, withRelease: false);
        var tc = await CaseAsync(admin, "Credit note", setup.FeatureB.Id);

        var webhook = await (await admin.PostJson("/api/v1/notifications/channels", new ChannelUpsert("Ops webhook", ChannelType.Webhook, null, "https://hooks.example.test/wings", "whsec-123"))).Read<ChannelDto>();
        Assert.True(webhook.HasUrl);
        Assert.Equal("https://hooks.example.test/…", webhook.UrlHint);
        var teams = await (await admin.PostJson("/api/v1/notifications/channels", new ChannelUpsert("QA Teams", ChannelType.Teams, null, "https://teams.example.test/webhook/abc", null))).Read<ChannelDto>();
        var mail = await (await admin.PostJson("/api/v1/notifications/channels", new ChannelUpsert("QA leads", ChannelType.Email, "qa-leads@wings.test", null, null))).Read<ChannelDto>();
        await (await admin.PostJson("/api/v1/notifications/channels", new ChannelUpsert("Bad", ChannelType.Webhook, null, "http://example.test/x", null))).Problem(HttpStatusCode.BadRequest);
        await (await tester.PostJson("/api/v1/notifications/channels", new ChannelUpsert("Mine", ChannelType.Email, "me@wings.test", null, null))).Problem(HttpStatusCode.Forbidden);

        (await admin.PostJson("/api/v1/notifications/rules", new RuleUpsert("Run results to ops", [NotificationEvents.RunCompleted], setup.Product.Id, "Info", null, false, webhook.Id))).EnsureSuccessStatusCode();
        (await admin.PostJson("/api/v1/notifications/rules", new RuleUpsert("Run results to leads", [NotificationEvents.RunCompleted], setup.Product.Id, "Warning", null, false, mail.Id))).EnsureSuccessStatusCode();
        // A user subscribes themself; they cannot subscribe channels.
        (await tester.PostJson("/api/v1/notifications/rules", new RuleUpsert("My runs", ["run.*"], setup.Product.Id, "Info", null, false, null))).EnsureSuccessStatusCode();
        await (await tester.PostJson("/api/v1/notifications/rules", new RuleUpsert("Hijack", ["*"], null, "Info", null, false, webhook.Id))).Problem(HttpStatusCode.Forbidden);
        (await admin.PostJson("/api/v1/notifications/escalations", new EscalationUpsert("Unacknowledged failures", [NotificationEvents.RunCompleted], 5, teams.Id))).EnsureSuccessStatusCode();

        var (runId, tests) = await ManualRunAsync(tester, tc.Id);
        await RecordAsync(tester, runId, tests[tc.Id], "Failed", "Credit note total is negative");

        EventLogDto? ev = null;
        await Eventually(async () =>
        {
            ev = (await (await admin.GetAsync("/api/v1/notifications/log")).Read<List<EventLogDto>>()).FirstOrDefault(e => e.Type == NotificationEvents.RunCompleted && e.Link == $"/runs/{runId}");
            return ev is not null && ev.Deliveries.Count == 2 && ev.Deliveries.All(d => d.Status == DeliveryStatus.Sent);
        }, "webhook and email deliveries sent");
        Assert.Equal(1, ev!.Recipients);
        Assert.Equal("Warning", ev.Severity);

        var hook = factory.Outbound.Requests.Last(r => r.Uri.Host == "hooks.example.test" && r.Body.Contains(runId.ToString()));
        Assert.Equal("run.completed", hook.Headers["X-Wings-Event"]);
        Assert.Equal("sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec-123"), Encoding.UTF8.GetBytes(hook.Body))), hook.Headers["X-Wings-Signature"]);
        Assert.Contains($"https://wto.test/runs/{runId}", hook.Body);
        await Eventually(() => Task.FromResult(Directory.Exists(factory.MailDirectory) && Directory.GetFiles(factory.MailDirectory, "*.eml")
            .Any(f => File.ReadAllText(f).Contains("qa-leads@wings.test"))), "email written to the pickup folder");

        var inbox = await (await tester.GetAsync("/api/v1/notifications/inbox?unreadOnly=true")).Read<InboxDto>();
        var item = Assert.Single(inbox.Items, i => i.EventId == ev.Id);
        Assert.True(inbox.Unread >= 1);
        (await tester.PostJson("/api/v1/notifications/inbox/read", new { eventIds = new[] { item.EventId } })).EnsureSuccessStatusCode();
        Assert.DoesNotContain((await (await tester.GetAsync("/api/v1/notifications/inbox?unreadOnly=true")).Read<InboxDto>()).Items, i => i.EventId == ev.Id);

        // Nobody acknowledged it: after the policy's delay it goes to the escalation channel (Teams).
        await factory.DbAsync(db => db.NotificationEvents.Where(e => e.Id == ev.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedAt, DateTimeOffset.UtcNow.AddMinutes(-6))));
        await Eventually(() => Task.FromResult(factory.Outbound.Requests.Any(r => r.Uri.Host == "teams.example.test" && r.Body.Contains("ESCALATED"))), "escalation to Teams");
        var card = JsonDocument.Parse(factory.Outbound.Requests.Last(r => r.Uri.Host == "teams.example.test").Body).RootElement;
        Assert.Equal("application/vnd.microsoft.card.adaptive", card.GetProperty("attachments")[0].GetProperty("contentType").GetString());
        // Acknowledged events are not escalated again.
        (await tester.PostAsync($"/api/v1/notifications/{ev.Id}/acknowledge", null)).EnsureSuccessStatusCode();

        // Agent offline is detected by the monitor once heartbeats stop.
        var agentClient = await ExecutionFixture.AgentAsync(factory, AgentKinds.Browser, ["PLAYWRIGHT_TS"]);
        var agent = (await (await admin.GetAsync("/api/v1/agents")).Read<List<AgentDto>>()).OrderByDescending(a => a.RegisteredAt).First();
        await factory.DbAsync(db => db.ExecutionAgents.Where(a => a.Id == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.LastHeartbeatAt, DateTimeOffset.UtcNow.AddMinutes(-10))));
        await Eventually(async () => (await (await admin.GetAsync("/api/v1/notifications/log")).Read<List<EventLogDto>>())
            .Any(e => e.Type == NotificationEvents.AgentOffline && e.Title.Contains(agent.Name)), "agent offline notification");
        agentClient.Dispose();
    }

    // ---- dashboards and reports ----

    [Fact(DisplayName = "Dashboards and reports use defined denominators, respect permissions and export to CSV, Excel and PDF; scheduled reports are emailed")]
    public async Task Dashboards_Reports_Exports_Schedules()
    {
        var admin = await factory.ClientFor("admin");
        var tester = await factory.ClientFor("tester");
        var setup = await ProductAsync(admin);
        var tc = await CaseAsync(admin, "Trial balance", setup.FeatureA.Id);
        var (runId, tests) = await ManualRunAsync(tester, tc.Id);
        await RecordAsync(tester, runId, tests[tc.Id], "Passed");

        var global = await (await tester.GetAsync("/api/v1/dashboards/global")).Read<GlobalDashboardDto>();
        Assert.True(global.Products >= 2);
        Assert.Equal(global.Features, global.FeaturesWithTests + global.FeaturesWithoutTests);
        Assert.Contains("featureCoverage", global.Definitions.Keys);
        Assert.Equal(30, global.Trend.Count);
        var row = global.ProductRows.Single(p => p.ProductId == setup.Product.Id);
        Assert.Equal((2, 1, 1), (row.Features, row.FeaturesWithTests, row.LatestPassed));
        Assert.Equal("2026.3", row.NextRelease);

        var product = await (await tester.GetAsync($"/api/v1/dashboards/products/{setup.Product.Id}")).Read<ProductDashboardDto>();
        var module = Assert.Single(product.Modules);
        Assert.Equal((2, 1, 1, 1), (module.Features, module.FeaturesWithTests, module.TestCases, module.Passed));
        Assert.Contains(product.RiskAreas, r => r.FeatureId == setup.FeatureB.Id && r.Reason.Contains("no test cases"));
        Assert.Equal(12, product.AutomationTrend.Count);
        var execution = await (await tester.GetAsync("/api/v1/dashboards/execution")).Read<ExecutionDashboardDto>();
        Assert.Equal(24, execution.Throughput.Count);
        await (await (await factory.ClientFor("viewer")).GetAsync("/api/v1/dashboards/global")).Problem(HttpStatusCode.Forbidden);

        // Role-based visibility: tester productivity needs audit.view.
        var catalog = await (await tester.GetAsync("/api/v1/reports")).Read<List<ReportDefinitionDto>>();
        Assert.DoesNotContain(catalog, r => r.Key == "tester-productivity");
        Assert.Equal(17, (await (await admin.GetAsync("/api/v1/reports")).Read<List<ReportDefinitionDto>>()).Count);
        await (await tester.PostJson("/api/v1/reports/tester-productivity/run", new { parameters = new ReportParameters() })).Problem(HttpStatusCode.Forbidden);

        var inventory = await (await tester.PostJson("/api/v1/reports/feature-inventory/run", new { parameters = new ReportParameters { ProductId = setup.Product.Id }, columns = new[] { "seq", "name", "tests", "latest" } }))
            .Read<JsonElement>();
        Assert.Equal(["seq", "name", "tests", "latest"], inventory.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()));
        var rows = inventory.GetProperty("rows").EnumerateArray().Select(r => r.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Null ? null : c.GetString()).ToList()).ToList();
        Assert.Contains(rows, r => r[1] == "Post invoice" && r[2] == "1" && r[3] == "Passed");
        var coverage = await (await tester.PostJson("/api/v1/reports/automation-coverage/run", new { parameters = new ReportParameters { ProductId = setup.Product.Id } })).Read<JsonElement>();
        Assert.Contains(coverage.GetProperty("summary").EnumerateArray(), s => s.GetProperty("label").GetString() == "Code coverage" && s.GetProperty("value").GetString()!.Contains("not measured"));
        await (await tester.PostJson("/api/v1/reports/feature-inventory/run", new { parameters = new ReportParameters() })).Problem(HttpStatusCode.BadRequest);

        foreach (var (format, type, magic) in new[] { ("csv", "text/csv", ""), ("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PK"), ("pdf", "application/pdf", "%PDF") })
        {
            var file = await tester.PostJson($"/api/v1/reports/execution-summary/export?format={format}", new { parameters = new ReportParameters { Days = 7 } });
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.Equal(type, file.Content.Headers.ContentType?.MediaType);
            var bytes = await file.Content.ReadAsByteArrayAsync();
            Assert.StartsWith(magic, Encoding.ASCII.GetString(bytes, 0, Math.Min(4, bytes.Length)));
            if (format == "csv") Assert.Contains(SequenceFormat.Format((await ExecutionFixture.RunAsync(tester, runId)).Run.Number, 5, "RUN-"), Encoding.UTF8.GetString(bytes));
        }

        // A scheduled report is emailed with its export attached.
        var mail = await (await admin.PostJson("/api/v1/notifications/channels", new ChannelUpsert("Report readers", ChannelType.Email, "reports@wings.test", null, null))).Read<ChannelDto>();
        await (await tester.PostJson("/api/v1/reports/saved", new SavedReportUpsert("Weekly", "execution-summary", new ReportParameters(), null, false, "0 7 * * 1", null))).Problem(HttpStatusCode.BadRequest);
        var saved = await (await tester.PostJson("/api/v1/reports/saved", new SavedReportUpsert("Weekly inventory", "feature-inventory", new ReportParameters { ProductId = setup.Product.Id },
            ["seq", "name"], true, "0 7 * * 1", mail.Id, "csv"))).Read<SavedReportDto>();
        Assert.NotNull(saved.NextRunAt);
        Assert.Contains(await (await admin.GetAsync("/api/v1/reports/saved")).Read<List<SavedReportDto>>(), s => s.Id == saved.Id && !s.IsMine); // shared
        await factory.DbAsync(db => db.SavedReports.Where(s => s.Id == saved.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.NextRunAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await Eventually(() => Task.FromResult(Directory.Exists(factory.MailDirectory) && Directory.GetFiles(factory.MailDirectory, "*.eml")
            .Any(f => File.ReadAllText(f) is var t && t.Contains("reports@wings.test") && t.Contains("feature-inventory-"))), "scheduled report emailed");
    }
}
