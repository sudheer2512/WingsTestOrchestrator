using System.Net;
using System.Text.Json;
using Wings.Agent.Contracts;
using Wings.Application.Execution;
using Common = Wings.Application.Common;
using Wings.Application.Features;
using Wings.Application.Orchestration;
using Wings.Application.Testing;
using Wings.Domain.Execution;
using Wings.Domain.Orchestration;
using Wings.Domain.Testing;
using Wings.Tests.Infrastructure;
using Xunit;

namespace Wings.Tests;

public sealed class FastScheduleFactory : WingsApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings => [new("Schedules:PollSeconds", "1")];
}

public sealed class OrchestrationTests(FastScheduleFactory factory) : IClassFixture<FastScheduleFactory>
{
    /// <summary>Publishes the sample portal package and creates one automated test case per discovered test.</summary>
    private async Task<(PackageVersionDetailDto Version, Dictionary<string, Guid> CaseByTitle)> PortalWithCasesAsync()
    {
        var version = await ExecutionFixture.PublishedPortalPackageAsync(factory);
        var dev = await factory.ClientFor("dev");
        var cases = new Dictionary<string, Guid>();
        foreach (var t in version.Tests)
        {
            var mapped = await (await dev.PutJson($"/api/v1/test-packages/versions/{version.Summary.Id}/tests/{t.Id}/mapping", new MapTestRequest(null, t.FeatureId, CreateTestCase: true))).Read<DiscoveredTestDto>();
            cases[t.DisplayName] = mapped.TestCaseId!.Value;
        }
        return (version, cases);
    }

    /// <summary>Cancels runs left active by other tests in this class (they share one database), so agents here only see this test's work.</summary>
    private async Task ResetQueueAsync()
    {
        var admin = await factory.ClientFor("admin");
        foreach (var status in new[] { RunStatus.Queued, RunStatus.Running })
            foreach (var r in (await (await admin.GetAsync($"/api/v1/runs?status={status}&pageSize=500")).Read<Common.PagedResult<RunSummaryDto>>()).Items)
                await admin.PostAsync($"/api/v1/runs/{r.Id}/cancel", null);
    }

    private static async Task SetCaseAsync(HttpClient c, Guid id, Func<TestCaseFields, TestCaseFields> change)
    {
        var tc = await (await c.GetAsync($"/api/v1/test-cases/{id}")).Read<TestCaseDetailDto>();
        (await c.PutJson($"/api/v1/test-cases/{id}", new SaveTestCaseRequest(change(tc.Fields), tc.Version))).EnsureSuccessStatusCode();
    }

    private static async Task<HttpClient> AgentInPoolAsync(WingsApiFactory f, string pool)
    {
        var admin = await f.ClientFor("admin");
        var created = await (await admin.PostJson("/api/v1/agents", new AgentUpsert($"b-{Guid.NewGuid():N}"[..18], AgentKinds.Browser, pool, null))).Read<AgentKeyResult>();
        var agent = f.CreateClient();
        agent.DefaultRequestHeaders.Add(AgentHeaders.Key, created.Key);
        (await agent.PostJson("/api/v1/agent/register", new AgentRegistration { MachineName = "h", Kind = AgentKinds.Browser, Slots = 4, Frameworks = ["PLAYWRIGHT_TS"] })).EnsureSuccessStatusCode();
        return agent;
    }

    private static async Task CompleteAsync(HttpClient agent, JobAssignment job, string outcome = TestOutcomes.Passed)
    {
        (await agent.PostAsync($"/api/v1/agent/jobs/{job.JobId}/start", null)).EnsureSuccessStatusCode();
        (await agent.PostJson($"/api/v1/agent/jobs/{job.JobId}/results", new ResultBatch(job.TestIdentifiers.Select(i => new TestResultReport { Identifier = i, Outcome = outcome }).ToList()))).EnsureSuccessStatusCode();
        (await agent.PostJson($"/api/v1/agent/jobs/{job.JobId}/complete", new JobCompletion(JobCompletions.Completed, null, 0))).EnsureSuccessStatusCode();
    }

    private static EnvironmentUpsert Env(string code, params EnvironmentVariableUpsert[] vars) => new()
    {
        Code = code, Name = $"Env {code}", Kind = EnvironmentKind.QA, BaseUrl = $"https://{code.ToLowerInvariant()}.wings.test/portal/", ApplicationBuildVersion = "2026.2.17",
        Variables = vars,
    };

    [Fact(DisplayName = "Environments: secrets are encrypted, never returned, compared blind, and delivered decrypted only to the agent")]
    public async Task Environments_And_Secrets()
    {
        var admin = await factory.ClientFor("admin");
        var env = await (await admin.PostJson("/api/v1/environments", Env("QA1", new("DB_PASSWORD", "s3cret-pw!", true, null), new("TENANT", "Wings Motors", false, null)) with { AgentPools = ["qa"] }))
            .Read<EnvironmentDto>();
        var secret = env.Variables.Single(v => v.Name == "DB_PASSWORD");
        Assert.Null(secret.Value);
        Assert.True(secret.HasValue);
        Assert.Equal("Wings Motors", env.Variables.Single(v => v.Name == "TENANT").Value);

        // Saving without a value keeps the secret; cloning carries it over.
        var kept = await (await admin.PutJson($"/api/v1/environments/{env.Id}", Env("QA1", new("DB_PASSWORD", null, true, "rotated quarterly"), new("TENANT", "Wings Motors", false, null)) with { AgentPools = ["qa"], Version = env.Version }))
            .Read<EnvironmentDto>();
        Assert.True(kept.Variables.Single(v => v.Name == "DB_PASSWORD").HasValue);
        var clone = await (await admin.PostJson($"/api/v1/environments/{env.Id}/clone", new EnvironmentsControllerClone("QA2", "Env QA2"))).Read<EnvironmentDto>();
        var diff = await (await admin.GetAsync($"/api/v1/environments/compare?left={env.Id}&right={clone.Id}")).Read<List<EnvironmentComparisonRow>>();
        var pwRow = diff.Single(r => r.Item == "$DB_PASSWORD");
        Assert.False(pwRow.Differs);
        Assert.DoesNotContain("s3cret", pwRow.Left);
        Assert.DoesNotContain(diff, r => (r.Left ?? "").Contains("s3cret") || (r.Right ?? "").Contains("s3cret"));

        await ResetQueueAsync();
        var (version, _) = await PortalWithCasesAsync();
        var tester = await factory.ClientFor("tester");
        var run = await (await tester.PostJson("/api/v1/runs", new StartRunRequest { PackageVersionId = version.Summary.Id, EnvironmentId = env.Id, Variables = new Dictionary<string, string> { ["EXTRA"] = "1" } }))
            .Read<RunSummaryDto>(HttpStatusCode.Created);
        Assert.Equal("Env QA1", run.EnvironmentName);
        Assert.Equal("2026.2.17", run.BuildVersion);

        Assert.Empty(await ExecutionFixture.ClaimAsync(await AgentInPoolAsync(factory, "other"))); // pool not allowed
        var agent = await AgentInPoolAsync(factory, "qa");
        var job = Assert.Single(await ExecutionFixture.ClaimAsync(agent));
        Assert.Equal("s3cret-pw!", job.Variables["DB_PASSWORD"]);
        Assert.Equal("https://qa1.wings.test/portal/", job.Variables["WINGS_BASE_URL"]);
        Assert.Equal("1", job.Variables["EXTRA"]);
        Assert.Contains("DB_PASSWORD", job.SecretNames);
        using var context = JsonDocument.Parse(job.ContextJson);
        Assert.Equal("QA1", context.RootElement.GetProperty("environment").GetProperty("code").GetString());

        var detail = await ExecutionFixture.RunAsync(tester, run.Id);
        Assert.DoesNotContain("s3cret", JsonSerializer.Serialize(detail));

        // Maintenance blocks new runs (unless explicitly ignored).
        var current = await (await admin.GetAsync($"/api/v1/environments/{clone.Id}")).Read<EnvironmentDto>();
        (await admin.PutJson($"/api/v1/environments/{clone.Id}", Env("QA2") with { InMaintenance = true, Version = current.Version })).EnsureSuccessStatusCode();
        await (await tester.PostJson("/api/v1/runs", new StartRunRequest { PackageVersionId = version.Summary.Id, EnvironmentId = clone.Id })).Problem(HttpStatusCode.Conflict);
        (await tester.PostJson("/api/v1/runs", new StartRunRequest { PackageVersionId = version.Summary.Id, EnvironmentId = clone.Id, IgnoreEnvironmentHealth = true })).EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Production guard: destructive tests are not run unless authorised; production needs a separate permission")]
    public async Task ProductionGuard()
    {
        var (version, cases) = await PortalWithCasesAsync();
        var admin = await factory.ClientFor("admin");
        var happy = cases["portal booking happy path"];
        await SetCaseAsync(admin, happy, f => f with { IsDestructive = true });
        var prod = await (await admin.PostJson("/api/v1/environments", Env("PROD1") with { Kind = EnvironmentKind.Production, IsProduction = true })).Read<EnvironmentDto>();

        var tester = await factory.ClientFor("tester");
        await (await tester.PostJson("/api/v1/runs", new StartRunRequest { PackageVersionId = version.Summary.Id, EnvironmentId = prod.Id })).Problem(HttpStatusCode.Forbidden);

        var run = await (await admin.PostJson("/api/v1/runs", new StartRunRequest { PackageVersionId = version.Summary.Id, EnvironmentId = prod.Id })).Read<RunSummaryDto>(HttpStatusCode.Created);
        var detail = await ExecutionFixture.RunAsync(admin, run.Id);
        var blocked = detail.Tests.Single(t => t.TestCaseId == happy);
        Assert.Equal("NotRun", blocked.Outcome);
        Assert.Contains("destructive", blocked.Reason);
        Assert.Equal(2, detail.Jobs.Single().TestCount);

        var current = await (await admin.GetAsync($"/api/v1/environments/{prod.Id}")).Read<EnvironmentDto>();
        (await admin.PutJson($"/api/v1/environments/{prod.Id}", Env("PROD1") with { Kind = EnvironmentKind.Production, IsProduction = true, AllowedDestructiveTestCaseIds = [happy], Version = current.Version }))
            .EnsureSuccessStatusCode();
        var allowed = await (await admin.PostJson("/api/v1/runs", new StartRunRequest { PackageVersionId = version.Summary.Id, EnvironmentId = prod.Id })).Read<RunSummaryDto>(HttpStatusCode.Created);
        Assert.Equal(3, (await ExecutionFixture.RunAsync(admin, allowed.Id)).Jobs.Single().TestCount);
    }

    [Fact(DisplayName = "Dependency waves: dependents wait for prerequisites and are blocked when a prerequisite fails")]
    public async Task DependencyOrdering()
    {
        var (version, cases) = await PortalWithCasesAsync();
        var admin = await factory.ClientFor("admin");
        var first = cases["rejects an invalid mobile number"];
        var second = cases["portal booking happy path"];
        (await admin.PostJson($"/api/v1/test-cases/{second}/dependencies", new TestCaseDependencyUpsert(first))).EnsureSuccessStatusCode();

        await ResetQueueAsync();
        var run = await (await admin.PostJson("/api/v1/runs", new StartRunRequest { TestCaseIds = [first, second] })).Read<RunSummaryDto>(HttpStatusCode.Created);
        var detail = await ExecutionFixture.RunAsync(admin, run.Id);
        Assert.Equal([0, 1], detail.Jobs.Select(j => j.Wave).Order());
        Assert.Equal(1, detail.Jobs.Single(j => j.Wave == 1).WaitingOn);

        var agent = await AgentInPoolAsync(factory, "any");
        var wave0 = Assert.Single(await ExecutionFixture.ClaimAsync(agent));
        Assert.Empty(await ExecutionFixture.ClaimAsync(agent)); // wave 1 waits
        await CompleteAsync(agent, wave0, TestOutcomes.Failed);

        Assert.Empty(await ExecutionFixture.ClaimAsync(agent)); // nothing to dispatch: the dependent is blocked instead
        var done = await ExecutionFixture.RunAsync(admin, run.Id);
        Assert.Equal(RunStatus.Completed, done.Run.Status);
        var dependent = done.Tests.Single(t => t.TestCaseId == second);
        Assert.Equal("Blocked", dependent.Outcome);
        Assert.Contains("rejects an invalid mobile number", dependent.Reason);

        // Cycles are rejected when declared.
        await (await admin.PostJson($"/api/v1/test-cases/{first}/dependencies", new TestCaseDependencyUpsert(second))).Problem(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Resource locks and test data reservations keep parallel runs from sharing resources or records")]
    public async Task Locks_And_TestData()
    {
        var (version, cases) = await PortalWithCasesAsync();
        var admin = await factory.ClientFor("admin");
        var set = await (await admin.PostJson("/api/v1/test-data", new DataSetUpsert("CUSTOMERS_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Customers", null, null, null, null, null))).Read<DataSetDto>();
        (await admin.PutJson($"/api/v1/test-data/{set.Id}/records", new DataRecordUpsert("C001", JsonDocument.Parse("""{"name":"Asha Rao","mobile":"9876500001"}""").RootElement))).EnsureSuccessStatusCode();

        var happy = cases["portal booking happy path"];
        await SetCaseAsync(admin, happy, f => f with { ResourceKeys = "db:QA-COMPANY-01", DataRequirements = set.Code });

        await ResetQueueAsync();
        var run1 = await (await admin.PostJson("/api/v1/runs", new StartRunRequest { TestCaseIds = [happy] })).Read<RunSummaryDto>(HttpStatusCode.Created);
        var run2 = await (await admin.PostJson("/api/v1/runs", new StartRunRequest { TestCaseIds = [happy] })).Read<RunSummaryDto>(HttpStatusCode.Created);
        var a1 = await AgentInPoolAsync(factory, "any");
        var a2 = await AgentInPoolAsync(factory, "any");

        var job1 = Assert.Single(await ExecutionFixture.ClaimAsync(a1, 1));
        using (var ctx = JsonDocument.Parse(job1.ContextJson))
            Assert.Equal("Asha Rao", ctx.RootElement.GetProperty("data").GetProperty(set.Code).GetProperty("records")[0].GetProperty("values").GetProperty("name").GetString());
        var locks = await (await admin.GetAsync("/api/v1/runs/locks")).Read<List<ResourceLockDto>>();
        Assert.Contains(locks, l => l.Key == "db:qa-company-01" && l.JobId == job1.JobId);
        var reservations = await (await admin.GetAsync("/api/v1/test-data/reservations")).Read<List<ReservationDto>>();
        Assert.Contains(reservations, r => r.JobId == job1.JobId && r.RecordKey == "C001");

        Assert.Empty(await ExecutionFixture.ClaimAsync(a2)); // same lock and the only record are held by run 1
        await CompleteAsync(a1, job1);
        var job2 = Assert.Single(await ExecutionFixture.ClaimAsync(a2));
        Assert.Equal(run2.Id, job2.RunId);
        Assert.NotEqual(run1.Id, job2.RunId);

        // Requiring more records than exist is caught when the run starts.
        await SetCaseAsync(admin, happy, f => f with { DataRequirements = $"{set.Code}:5" });
        var tooMany = await (await admin.PostJson("/api/v1/runs", new StartRunRequest { TestCaseIds = [happy] })).Problem(HttpStatusCode.BadRequest);
        Assert.Contains("1 active record", tooMany);
    }

    [Fact(DisplayName = "Plans run suites with manual tests pending for testers; completion is approved by someone else; campaigns fan out")]
    public async Task Plans_Campaigns_ManualResults()
    {
        var (_, cases) = await PortalWithCasesAsync();
        var admin = await factory.ClientFor("admin");
        var qa = await factory.ClientFor("qa");
        var manual = await (await admin.PostJson("/api/v1/test-cases", new SaveTestCaseRequest(new TestCaseFields { Name = "Brochure looks right", TestTypeCode = "FUNCTIONAL" })))
            .Read<TestCaseDetailDto>(HttpStatusCode.Created);
        var suite = await (await admin.PostJson("/api/v1/test-suites", new SuiteUpsert("Portal acceptance", null, null, null, null))).Read<SuiteDetailDto>();
        (await admin.PostJson($"/api/v1/test-suites/{suite.Suite.Id}/items", new SuiteItemsRequest([cases["portal booking happy path"], manual.Id]))).EnsureSuccessStatusCode();
        var envA = await (await admin.PostJson("/api/v1/environments", Env("CA" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()))).Read<EnvironmentDto>();
        var envB = await (await admin.PostJson("/api/v1/environments", Env("CB" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()))).Read<EnvironmentDto>();

        var plan = await (await qa.PostJson("/api/v1/plans", new PlanUpsert { Name = "Release 2026.2 acceptance", SuiteIds = [suite.Suite.Id], EnvironmentId = envA.Id, Risk = RiskLevel.High }))
            .Read<PlanDetailDto>();
        Assert.StartsWith("PLAN-", plan.Summary.NumberDisplay);
        Assert.Equal(2, plan.Summary.TestCaseCount);

        await ResetQueueAsync();
        var run = await (await qa.PostJson($"/api/v1/plans/{plan.Summary.Id}/execute", new ExecutePlanRequest())).Read<RunSummaryDto>();
        Assert.Equal(plan.Summary.Id, run.PlanId);
        Assert.True(run.IncludeManual);
        var agent = await AgentInPoolAsync(factory, "any");
        await CompleteAsync(agent, Assert.Single(await ExecutionFixture.ClaimAsync(agent)));
        var detail = await ExecutionFixture.RunAsync(qa, run.Id);
        Assert.Equal(RunStatus.Running, detail.Run.Status); // the manual test is still pending
        var manualRow = detail.Tests.Single(t => t.IsManual);
        await (await qa.PostJson($"/api/v1/runs/{run.Id}/tests/{manualRow.Id}/manual", new ManualResultRequest("Failed", null))).Problem(HttpStatusCode.BadRequest);
        var recorded = await (await qa.PostJson($"/api/v1/runs/{run.Id}/tests/{manualRow.Id}/manual", new ManualResultRequest("Passed", "Checked on 1080p"))).Read<TestExecutionDto>();
        Assert.Equal("qa", recorded.ExecutedBy);
        Assert.Equal(RunStatus.Completed, (await ExecutionFixture.RunAsync(qa, run.Id)).Run.Status);

        var withCoverage = await (await qa.GetAsync($"/api/v1/plans/{plan.Summary.Id}")).Read<PlanDetailDto>();
        Assert.Equal(2, withCoverage.CoverageTotals["Passed"]);
        Assert.Equal(PlanStatus.Active, withCoverage.Summary.Status);
        (await qa.PostJson($"/api/v1/plans/{plan.Summary.Id}/status", new PlansControllerStatus(PlanStatus.Completed, null))).EnsureSuccessStatusCode();
        await (await qa.PostJson($"/api/v1/plans/{plan.Summary.Id}/status", new PlansControllerStatus(PlanStatus.Approved, "ok"))).Problem(HttpStatusCode.Forbidden); // last editor
        var approved = await (await admin.PostJson($"/api/v1/plans/{plan.Summary.Id}/status", new PlansControllerStatus(PlanStatus.Approved, "Exit criteria met"))).Read<PlanDetailDto>();
        Assert.Equal("admin", approved.ApprovedBy);

        var campaign = await (await qa.PostJson("/api/v1/campaigns", new CampaignUpsert("Release 2026.2", null, "2026.2.18", [plan.Summary.Id], [envA.Id, envB.Id]))).Read<CampaignDto>();
        var started = await (await qa.PostAsync($"/api/v1/campaigns/{campaign.Id}/execute", null)).Read<List<RunSummaryDto>>();
        Assert.Equal(2, started.Count);
        Assert.All(started, r => Assert.Equal("2026.2.18", r.BuildVersion));
        var refreshed = await (await qa.GetAsync($"/api/v1/campaigns/{campaign.Id}")).Read<CampaignDto>();
        Assert.Equal(CampaignStatus.Running, refreshed.Status);
        Assert.All(refreshed.Cells, c => Assert.NotNull(c.LatestRun));
    }

    [Fact(DisplayName = "Schedules: cron next runs are computed, due schedules start runs, webhooks need their token")]
    public async Task Schedules_And_Triggers()
    {
        var (version, _) = await PortalWithCasesAsync();
        var admin = await factory.ClientFor("admin");
        var daily = await (await admin.PostJson("/api/v1/schedules", new ScheduleUpsert
        {
            Name = "Nightly portal", Trigger = TriggerType.Weekly, Time = "02:30", DaysOfWeek = [1, 2, 3, 4, 5], TimeZoneId = "Asia/Kolkata",
            TargetType = ScheduleTarget.Package, TargetId = version.PackageId,
        })).Read<ScheduleDto>();
        Assert.Equal("30 2 * * 1,2,3,4,5", daily.CronExpression);
        Assert.Equal(5, daily.Upcoming.Count);
        Assert.All(daily.Upcoming, t => Assert.Equal(new TimeSpan(2, 30, 0), TimeZoneInfo.ConvertTime(t, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).TimeOfDay));
        await (await admin.PostJson("/api/v1/schedules", new ScheduleUpsert { Name = "Bad", Trigger = TriggerType.Cron, CronExpression = "every day", TargetType = ScheduleTarget.Package, TargetId = version.PackageId }))
            .Problem(HttpStatusCode.BadRequest);

        var once = await (await admin.PostJson("/api/v1/schedules", new ScheduleUpsert
        {
            Name = "One-off", Trigger = TriggerType.Once, RunOnceAt = DateTimeOffset.UtcNow.AddSeconds(1), TargetType = ScheduleTarget.Package, TargetId = version.PackageId,
        })).Read<ScheduleDto>();
        ScheduleDto after;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        do { await Task.Delay(500); after = (await (await admin.GetAsync("/api/v1/schedules")).Read<List<ScheduleDto>>()).Single(s => s.Id == once.Id); }
        while (after.LastRunId is null && sw.Elapsed < TimeSpan.FromSeconds(20));
        Assert.NotNull(after.LastRunId);
        Assert.False(after.IsEnabled); // one-off schedules disable themselves
        var scheduled = await ExecutionFixture.RunAsync(admin, after.LastRunId!.Value);
        Assert.Equal("schedule:One-off", scheduled.Run.TriggeredBy);

        var hook = await (await admin.PostJson("/api/v1/schedules", new ScheduleUpsert { Name = "After deploy", Trigger = TriggerType.Webhook, TargetType = ScheduleTarget.Package, TargetId = version.PackageId }))
            .Read<ScheduleDto>();
        var token = await (await admin.PostAsync($"/api/v1/schedules/{hook.Id}/webhook-token", null)).Read<WebhookTokenResult>();
        var ci = factory.CreateClient();
        await (await ci.PostJson($"/api/v1/triggers/schedules/{hook.Id}", new TriggerRequest("2026.2.19"))).Problem(HttpStatusCode.Forbidden);
        ci.DefaultRequestHeaders.Add("X-Wings-Trigger-Token", token.Token);
        var triggered = await (await ci.PostJson($"/api/v1/triggers/schedules/{hook.Id}", new TriggerRequest("2026.2.19"))).Read<JsonElement>();
        var runId = triggered.GetProperty("runs")[0].GetProperty("id").GetGuid();
        var hooked = await ExecutionFixture.RunAsync(admin, runId);
        Assert.Equal("webhook:After deploy", hooked.Run.TriggeredBy);
        Assert.Equal("2026.2.19", hooked.Run.BuildVersion);
    }
}

// Request bodies of controller-nested records, mirrored here for readability.
internal sealed record EnvironmentsControllerClone(string Code, string Name);
internal sealed record PlansControllerStatus(PlanStatus Status, string? Comment);
