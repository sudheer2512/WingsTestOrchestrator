using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Wings.Agent.Contracts;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Application.Features;
using Wings.Application.Testing;
using Wings.Domain.Execution;
using Wings.Domain.Testing;
using Wings.Tests.Infrastructure;
using Xunit;

namespace Wings.Tests;

/// <summary>Builds published packages and fake agents that speak the real HTTP agent protocol.</summary>
internal static class ExecutionFixture
{
    public static async Task<PackageVersionDetailDto> PublishedPortalPackageAsync(WingsApiFactory factory)
    {
        var dev = await factory.ClientFor("dev");
        var qa = await factory.ClientFor("qa");
        var products = await (await dev.GetAsync("/api/v1/products")).Read<List<ProductDto>>();
        var auto = products.Single(p => p.Code == "AUTO");
        var zip = TestPackageTests.ZipDirectory(Path.Combine(TestPackageTests.SamplesRoot, "wings-portal-e2e"), "wings-portal-e2e");
        var v = await TestPackageTests.WaitForValidation(dev, (await TestPackageTests.Upload(dev, zip, "portal.zip", $"Portal {Guid.NewGuid():N}", auto.Id)).Id);
        Assert.Equal(PackageStatus.Validated, v.Summary.Status);
        (await qa.PostJson($"/api/v1/test-packages/versions/{v.Summary.Id}/approve", new ReviewDecision(null))).EnsureSuccessStatusCode();
        return await (await qa.PostAsync($"/api/v1/test-packages/versions/{v.Summary.Id}/publish", null)).Read<PackageVersionDetailDto>();
    }

    public static async Task<HttpClient> AgentAsync(WingsApiFactory factory, string kind, string[] frameworks, bool interactive = false)
    {
        var admin = await factory.ClientFor("admin");
        var created = await (await admin.PostJson("/api/v1/agents", new AgentUpsert($"{kind}-{Guid.NewGuid():N}"[..20], kind, "test", null))).Read<AgentKeyResult>();
        var agent = factory.CreateClient();
        agent.DefaultRequestHeaders.Add(AgentHeaders.Key, created.Key);
        (await agent.PostJson("/api/v1/agent/register", new AgentRegistration
        {
            MachineName = "test-host", OperatingSystem = "test", AgentVersion = "1.0.0", Kind = kind, Slots = 2, Frameworks = frameworks,
            InteractiveSession = interactive, ProcessSessionId = interactive ? 1 : 0,
        })).EnsureSuccessStatusCode();
        return agent;
    }

    public static async Task<List<JobAssignment>> ClaimAsync(HttpClient agent, int slots = 2)
        => (await (await agent.PostJson("/api/v1/agent/jobs/claim", new ClaimRequest(slots))).Read<ClaimResponse>()).Jobs.ToList();

    public static async Task<RunDetailDto> RunAsync(HttpClient c, Guid runId) => await (await c.GetAsync($"/api/v1/runs/{runId}")).Read<RunDetailDto>();
}

public sealed class ExecutionTests(WingsApiFactory factory) : IClassFixture<WingsApiFactory>
{
    private async Task<Guid> OnlineBookingFeatureAsync(HttpClient c)
    {
        var products = await (await c.GetAsync("/api/v1/products")).Read<List<ProductDto>>();
        var auto = products.Single(p => p.Code == "AUTO");
        var hits = await (await c.GetAsync($"/api/v1/features/search?productId={auto.Id}&text=Online Booking")).Read<PagedResult<FeatureSearchHit>>();
        return hits.Items.Single(h => h.Node.Name == "Online Booking").Node.Id;
    }

    [Fact(DisplayName = "Scenario 5: a browser run is dispatched to a Playwright agent, which returns results, logs and evidence")]
    public async Task BrowserRun_EndToEnd()
    {
        var version = await ExecutionFixture.PublishedPortalPackageAsync(factory);
        var tester = await factory.ClientFor("tester");
        var featureId = await OnlineBookingFeatureAsync(tester);
        var desktopAgent = await ExecutionFixture.AgentAsync(factory, AgentKinds.Desktop, ["FLAUI", "XUNIT"], interactive: true);
        var agent = await ExecutionFixture.AgentAsync(factory, AgentKinds.Browser, ["PLAYWRIGHT_TS"]);

        var run = await (await tester.PostJson("/api/v1/runs", new StartRunRequest
        {
            FeatureId = featureId, Variables = new Dictionary<string, string> { ["WINGS_BASE_URL"] = "http://portal.test/" },
        })).Read<RunSummaryDto>(HttpStatusCode.Created);
        Assert.StartsWith("RUN-", run.NumberDisplay);
        Assert.Equal(RunStatus.Queued, run.Status);
        // 3 automated tests from the package + the seeded manual test case linked to the feature ("Portal booking happy path" is automation-planned only).
        var detail = await ExecutionFixture.RunAsync(tester, run.Id);
        Assert.Equal(3, detail.Tests.Count(t => t.JobId != null));
        Assert.Contains(detail.Tests, t => t.JobId == null && t.Outcome == "NotRun" && t.Reason!.Contains("Manual"));

        Assert.Empty(await ExecutionFixture.ClaimAsync(desktopAgent)); // wrong framework/platform
        var job = Assert.Single(await ExecutionFixture.ClaimAsync(agent));
        Assert.Equal("PLAYWRIGHT_TS", job.Framework);
        Assert.Equal(3, job.TestIdentifiers.Count);
        Assert.Equal("http://portal.test/", job.Variables["WINGS_BASE_URL"]);
        Assert.Empty(await ExecutionFixture.ClaimAsync(agent)); // nothing left

        var bytes = await agent.GetByteArrayAsync($"/api/v1/agent/packages/{job.PackageVersionId}");
        Assert.Equal(job.PackageSha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));

        (await agent.PostAsync($"/api/v1/agent/jobs/{job.JobId}/start", null)).EnsureSuccessStatusCode();
        (await agent.PostJson($"/api/v1/agent/jobs/{job.JobId}/logs", new LogBatch([new(DateTimeOffset.UtcNow, "info", "npx playwright test"), new(DateTimeOffset.UtcNow, "info", "3 tests")]))).EnsureSuccessStatusCode();
        var happy = job.TestIdentifiers.Single(i => i.EndsWith("portal booking happy path"));
        var festival = job.TestIdentifiers.Single(i => i.Contains("festival"));
        (await agent.PostJson($"/api/v1/agent/jobs/{job.JobId}/results", new ResultBatch([
            new TestResultReport { Identifier = happy, Outcome = TestOutcomes.Passed, DurationMs = 1200 },
            new TestResultReport { Identifier = festival, Outcome = TestOutcomes.Failed, DurationMs = 900, ErrorMessage = "Expected ₹11,87,500 but got ₹12,45,000", StackTrace = "at online-booking.spec.ts:30" },
            new TestResultReport { Identifier = "not part of this job", Outcome = TestOutcomes.Passed },
        ]))).EnsureSuccessStatusCode();

        using (var form = new MultipartFormDataContent())
        {
            var png = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
            png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(png, "file", "test-failed-1.png");
            form.Add(new StringContent(ArtifactKinds.Screenshot), "kind");
            form.Add(new StringContent(festival), "testIdentifier");
            (await agent.PostAsync($"/api/v1/agent/jobs/{job.JobId}/artifacts", form)).EnsureSuccessStatusCode();
        }

        var mid = await ExecutionFixture.RunAsync(tester, run.Id);
        Assert.Equal(RunStatus.Running, mid.Run.Status);
        Assert.Equal(JobStatus.Running, Assert.Single(mid.Jobs).Status);
        Assert.Equal(1, mid.Run.Passed);
        Assert.Equal(1, mid.Run.Failed);

        (await agent.PostJson($"/api/v1/agent/jobs/{job.JobId}/complete", new JobCompletion(JobCompletions.Completed, null, 1))).EnsureSuccessStatusCode();
        (await agent.PostJson($"/api/v1/agent/jobs/{job.JobId}/complete", new JobCompletion(JobCompletions.Completed, null, 1))).EnsureSuccessStatusCode(); // idempotent

        var done = await ExecutionFixture.RunAsync(tester, run.Id);
        Assert.Equal(RunStatus.Completed, done.Run.Status);
        Assert.Equal(1, done.Run.Passed);
        Assert.Equal(1, done.Run.Failed);
        Assert.Equal(2, done.Run.NotRun); // unreported automated test + manual test case
        var failed = done.Tests.Single(t => t.Identifier == festival);
        Assert.Contains("12,45,000", failed.ErrorMessage);
        var shot = Assert.Single(failed.Artifacts);
        var inline = await tester.GetAsync($"/api/v1/runs/{run.Id}/artifacts/{shot.Id}");
        Assert.Equal("image/png", inline.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("inline", inline.Content.Headers.ContentDisposition?.ToString() ?? inline.Headers.GetValues("Content-Disposition").First());

        var logs = await (await tester.GetAsync($"/api/v1/runs/jobs/{job.JobId}/logs")).Read<List<LogLineDto>>();
        Assert.Equal(["npx playwright test", "3 tests"], logs.Select(l => l.Message));

        var feature = await (await tester.GetAsync($"/api/v1/features/{featureId}")).Read<FeatureDetailDto>();
        Assert.Equal(run.Id, feature.Testing.LastRunId);
        Assert.Contains("1 Failed", feature.Testing.LastResult);
        var history = await (await tester.GetAsync($"/api/v1/runs/history?featureId={featureId}")).Read<List<ExecutionHistoryDto>>();
        Assert.Contains(history, h => h.Identifier == festival && h.Outcome == "Failed");

        // Scenario 9: re-run only the failures, on the same package version.
        var rerun = await (await tester.PostJson("/api/v1/runs", new StartRunRequest { RerunOfRunId = run.Id, OnlyFailed = true })).Read<RunSummaryDto>(HttpStatusCode.Created);
        var rerunJob = Assert.Single(await ExecutionFixture.ClaimAsync(agent));
        Assert.Equal(rerun.Id, rerunJob.RunId);
        Assert.Equal([festival], rerunJob.TestIdentifiers);
        Assert.Equal(job.PackageVersionId, rerunJob.PackageVersionId);
    }

    [Fact(DisplayName = "Desktop jobs go only to interactive desktop agents; pause, priority and cancellation work")]
    public async Task DesktopDispatch_Pause_Cancel()
    {
        var dev = await factory.ClientFor("dev");
        var qa = await factory.ClientFor("qa");
        var products = await (await dev.GetAsync("/api/v1/products")).Read<List<ProductDto>>();
        var auto = products.Single(p => p.Code == "AUTO");
        var zip = TestPackageTests.ZipDirectory(Path.Combine(TestPackageTests.SamplesRoot, "WingsAuto.WinForms.UiTests"), "WingsAuto.WinForms.UiTests");
        var v = await TestPackageTests.WaitForValidation(dev, (await TestPackageTests.Upload(dev, zip, "ui.zip", $"UI {Guid.NewGuid():N}", auto.Id)).Id);
        (await qa.PostJson($"/api/v1/test-packages/versions/{v.Summary.Id}/approve", new ReviewDecision(null))).EnsureSuccessStatusCode();

        var noSession = await ExecutionFixture.AgentAsync(factory, AgentKinds.Desktop, ["FLAUI"], interactive: false);
        var browser = await ExecutionFixture.AgentAsync(factory, AgentKinds.Browser, ["FLAUI", "PLAYWRIGHT_TS"]);
        var desktop = await ExecutionFixture.AgentAsync(factory, AgentKinds.Desktop, ["FLAUI"], interactive: true);

        // Approved (not yet published) versions can be run explicitly as a pre-publication check.
        var run = await (await dev.PostJson("/api/v1/runs", new StartRunRequest { PackageVersionId = v.Summary.Id, Priority = 70 })).Read<RunSummaryDto>(HttpStatusCode.Created);
        Assert.True((await ExecutionFixture.RunAsync(dev, run.Id)).Jobs.Single().RequiresDesktop);

        (await dev.PostAsync($"/api/v1/runs/{run.Id}/pause", null)).EnsureSuccessStatusCode();
        Assert.Empty(await ExecutionFixture.ClaimAsync(desktop));
        (await dev.PostAsync($"/api/v1/runs/{run.Id}/resume", null)).EnsureSuccessStatusCode();

        Assert.Empty(await ExecutionFixture.ClaimAsync(noSession)); // session 0 / non-interactive
        Assert.Empty(await ExecutionFixture.ClaimAsync(browser));   // not a desktop agent
        var job = Assert.Single(await ExecutionFixture.ClaimAsync(desktop, 5));
        Assert.Equal(2, job.TestIdentifiers.Count);

        (await desktop.PostAsync($"/api/v1/agent/jobs/{job.JobId}/start", null)).EnsureSuccessStatusCode();
        (await dev.PostAsync($"/api/v1/runs/{run.Id}/cancel", null)).EnsureSuccessStatusCode();
        var hb = await (await desktop.PostJson("/api/v1/agent/heartbeat", new HeartbeatRequest([job.JobId, Guid.NewGuid()], 0))).Read<HeartbeatResponse>();
        Assert.Contains(job.JobId, hb.CancelJobIds);
        Assert.Equal(2, hb.CancelJobIds.Count); // also tells it to drop a job it does not hold
        (await desktop.PostJson($"/api/v1/agent/jobs/{job.JobId}/complete", new JobCompletion(JobCompletions.Cancelled, "Killed on request", null))).EnsureSuccessStatusCode();

        var done = await ExecutionFixture.RunAsync(dev, run.Id);
        Assert.Equal(RunStatus.Cancelled, done.Run.Status);
        Assert.Equal(JobStatus.Cancelled, done.Jobs.Single().Status);
        Assert.All(done.Tests, t => Assert.Equal("NotRun", t.Outcome));
    }

    [Fact(DisplayName = "Security: agent keys, package access and run permissions are enforced")]
    public async Task Security()
    {
        var viewer = await factory.ClientFor("viewer");
        await (await viewer.PostJson("/api/v1/runs", new StartRunRequest { TestCaseIds = [Guid.NewGuid()] })).Problem(HttpStatusCode.Forbidden);

        var bogus = factory.CreateClient();
        bogus.DefaultRequestHeaders.Add(AgentHeaders.Key, "wta_not-a-real-key");
        await (await bogus.PostJson("/api/v1/agent/jobs/claim", new ClaimRequest(1))).Problem(HttpStatusCode.Unauthorized);
        await (await viewer.PostJson("/api/v1/agent/jobs/claim", new ClaimRequest(1))).Problem(HttpStatusCode.Unauthorized); // user tokens are not agent credentials

        var version = await ExecutionFixture.PublishedPortalPackageAsync(factory);
        var agent = await ExecutionFixture.AgentAsync(factory, AgentKinds.Browser, ["PLAYWRIGHT_TS"]);
        await (await agent.GetAsync($"/api/v1/agent/packages/{version.Summary.Id}")).Problem(HttpStatusCode.Forbidden);

        var admin = await factory.ClientFor("admin");
        var agents = await (await admin.GetAsync("/api/v1/agents")).Read<List<AgentDto>>();
        var mine = agents.Last(a => a.Kind == AgentKinds.Browser);
        var rotated = await (await admin.PostAsync($"/api/v1/agents/{mine.Id}/rotate-key", null)).Read<AgentKeyResult>();
        Assert.StartsWith("wta_", rotated.Key);
        await (await viewer.PostJson("/api/v1/agents", new AgentUpsert("x", AgentKinds.Browser, null, null))).Problem(HttpStatusCode.Forbidden);
    }
}

/// <summary>Very short leases so agent loss can be observed quickly.</summary>
public sealed class ShortLeaseFactory : WingsApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings =>
        [new("Execution:LeaseSeconds", "2"), new("Execution:MonitorIntervalSeconds", "1")];
}

public sealed class AgentLossTests(ShortLeaseFactory factory) : IClassFixture<ShortLeaseFactory>
{
    [Fact(DisplayName = "Scenario 10: a lost agent's job is failed as infrastructure, evidence kept, and unfinished tests retried elsewhere")]
    public async Task LostAgent_IsDetected_AndRetried()
    {
        var version = await ExecutionFixture.PublishedPortalPackageAsync(factory);
        var tester = await factory.ClientFor("tester");
        var run = await (await tester.PostJson("/api/v1/runs", new StartRunRequest { PackageVersionId = version.Summary.Id, MaxInfrastructureRetries = 1 })).Read<RunSummaryDto>(HttpStatusCode.Created);

        var lost = await ExecutionFixture.AgentAsync(factory, AgentKinds.Browser, ["PLAYWRIGHT_TS"]);
        var job = Assert.Single(await ExecutionFixture.ClaimAsync(lost));
        (await lost.PostAsync($"/api/v1/agent/jobs/{job.JobId}/start", null)).EnsureSuccessStatusCode();
        var first = job.TestIdentifiers[0];
        (await lost.PostJson($"/api/v1/agent/jobs/{job.JobId}/results", new ResultBatch([new TestResultReport { Identifier = first, Outcome = TestOutcomes.Passed }]))).EnsureSuccessStatusCode();

        // The agent goes silent: no heartbeats. The monitor must notice within a few seconds.
        RunDetailDto detail;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        do { await Task.Delay(500); detail = await ExecutionFixture.RunAsync(tester, run.Id); }
        while (detail.Jobs.Count < 2 && sw.Elapsed < TimeSpan.FromSeconds(20));

        var failedJob = detail.Jobs.Single(j => j.Id == job.JobId);
        Assert.Equal(JobStatus.InfrastructureError, failedJob.Status);
        Assert.Contains("lease expired", failedJob.Error);
        var retry = detail.Jobs.Single(j => j.Id != job.JobId);
        Assert.Equal(2, retry.Attempt);
        Assert.Equal(job.JobId, retry.RetryOfJobId);
        Assert.Equal(2, retry.TestCount); // the reported test is not re-run
        Assert.Equal("Passed", detail.Tests.Single(t => t.Identifier == first && !t.IsSuperseded).Outcome);

        // The silent agent comes back too late: the server refuses its updates.
        await (await lost.PostJson($"/api/v1/agent/jobs/{job.JobId}/results", new ResultBatch([new TestResultReport { Identifier = job.TestIdentifiers[1], Outcome = TestOutcomes.Passed }])))
            .Problem(HttpStatusCode.Conflict);

        var healthy = await ExecutionFixture.AgentAsync(factory, AgentKinds.Browser, ["PLAYWRIGHT_TS"]);
        var retryJob = Assert.Single(await ExecutionFixture.ClaimAsync(healthy));
        (await healthy.PostAsync($"/api/v1/agent/jobs/{retryJob.JobId}/start", null)).EnsureSuccessStatusCode();
        (await healthy.PostJson($"/api/v1/agent/jobs/{retryJob.JobId}/results", new ResultBatch(retryJob.TestIdentifiers.Select(i => new TestResultReport { Identifier = i, Outcome = TestOutcomes.Passed }).ToList())))
            .EnsureSuccessStatusCode();
        (await healthy.PostJson($"/api/v1/agent/jobs/{retryJob.JobId}/complete", new JobCompletion(JobCompletions.Completed, null, 0))).EnsureSuccessStatusCode();

        var done = await ExecutionFixture.RunAsync(tester, run.Id);
        Assert.Equal(RunStatus.Completed, done.Run.Status);
        Assert.Equal(3, done.Run.Passed);
        Assert.Equal(3, done.Run.TotalTests); // superseded attempts are not double-counted
        Assert.Equal(2, done.Tests.Count(t => t.IsSuperseded && t.Outcome == "InfrastructureError"));
    }
}
