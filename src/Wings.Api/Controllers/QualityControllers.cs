using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wings.Api.Security;
using Wings.Application.Abstractions;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Application.Quality;
using Wings.Application.Security;

namespace Wings.Api.Controllers;

[ApiController, Route("api/v1/defects")]
public sealed class DefectsController(IDefectService defects, IIssueTrackerService trackers) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<PagedResult<DefectSummaryDto>> List([FromQuery] DefectFilter filter, CancellationToken ct) => defects.ListAsync(filter, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TestCasesView)]
    public Task<DefectDetailDto> Get(Guid id, CancellationToken ct) => defects.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.DefectsManage)]
    public async Task<ActionResult<DefectDetailDto>> Create(DefectUpsert request, CancellationToken ct)
    {
        var d = await defects.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = d.Summary.Id }, d);
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.DefectsManage)]
    public Task<DefectDetailDto> Update(Guid id, DefectUpsert request, CancellationToken ct) => defects.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/transition"), HasPermission(Permissions.DefectsManage)]
    public Task<DefectDetailDto> Transition(Guid id, DefectTransition request, CancellationToken ct) => defects.TransitionAsync(id, request, ct);

    public sealed record CommentRequest(string Text);

    [HttpPost("{id:guid}/comments"), HasPermission(Permissions.DefectsManage)]
    public Task<DefectDetailDto> Comment(Guid id, CommentRequest request, CancellationToken ct) => defects.CommentAsync(id, request.Text, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.DefectsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await defects.DeleteAsync(id, ct); return NoContent(); }

    [HttpGet("suggestions"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<FailureSuggestionDto>> Suggestions([FromQuery] Guid? runId, [FromQuery] Guid? productId, [FromQuery] int days = 7, CancellationToken ct = default)
        => defects.SuggestionsAsync(runId, productId, days, ct);

    [HttpPost("from-failure"), HasPermission(Permissions.DefectsManage)]
    public Task<CreateFromFailureResult> FromFailure(CreateFromFailureRequest request, CancellationToken ct) => defects.CreateFromFailureAsync(request, ct);

    public sealed record LinkRequest(Guid TestExecutionId);

    [HttpPost("{id:guid}/occurrences"), HasPermission(Permissions.DefectsManage)]
    public Task<DefectDetailDto> Link(Guid id, LinkRequest request, CancellationToken ct) => defects.LinkFailureAsync(id, request.TestExecutionId, ct);

    [HttpGet("clusters"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<FailureClusterDto>> Clusters([FromQuery] Guid? productId, [FromQuery] int days = 30, CancellationToken ct = default) => defects.ClustersAsync(productId, days, ct);

    [HttpGet("recurring"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<RecurringFailureDto>> Recurring([FromQuery] Guid? productId, [FromQuery] int days = 30, [FromQuery] int minFailures = 2, CancellationToken ct = default)
        => defects.RecurringAsync(productId, days, minFailures, ct);

    public sealed record PushRequest(Guid ConnectionId);

    [HttpPost("{id:guid}/push"), HasPermission(Permissions.DefectsManage)]
    public Task<DefectDetailDto> Push(Guid id, PushRequest request, CancellationToken ct) => trackers.PushAsync(id, request.ConnectionId, ct);
}

[ApiController, Route("api/v1/releases")]
public sealed class ReleasesController(IReleaseService releases) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.FeaturesView)]
    public Task<IReadOnlyList<ReleaseSummaryDto>> List([FromQuery] Guid? productId, CancellationToken ct) => releases.ListAsync(productId, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.FeaturesView)]
    public Task<ReleaseWorkspaceDto> Get(Guid id, CancellationToken ct) => releases.GetAsync(id, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.ReleasesManage)]
    public Task<ReleaseWorkspaceDto> Update(Guid id, ReleaseUpdate request, CancellationToken ct) => releases.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/changes"), HasPermission(Permissions.ReleasesManage)]
    public Task<ReleaseWorkspaceDto> AddChange(Guid id, ReleaseChangeUpsert request, CancellationToken ct) => releases.AddChangeAsync(id, request, ct);

    [HttpDelete("{id:guid}/changes/{changeId:guid}"), HasPermission(Permissions.ReleasesManage)]
    public Task<ReleaseWorkspaceDto> RemoveChange(Guid id, Guid changeId, CancellationToken ct) => releases.RemoveChangeAsync(id, changeId, ct);

    [HttpGet("{id:guid}/gates"), HasPermission(Permissions.FeaturesView)]
    public Task<GateCheckDto> Check(Guid id, CancellationToken ct) => releases.CheckAsync(id, ct);

    public sealed record WaiveRequest(Guid GateId, string Justification);

    [HttpPost("{id:guid}/waivers"), HasPermission(Permissions.ReleasesApprove)]
    public Task<ReleaseWorkspaceDto> Waive(Guid id, WaiveRequest request, CancellationToken ct) => releases.WaiveAsync(id, request.GateId, request.Justification, ct);

    [HttpPost("{id:guid}/waivers/{waiverId:guid}/revoke"), HasPermission(Permissions.ReleasesApprove)]
    public Task<ReleaseWorkspaceDto> Revoke(Guid id, Guid waiverId, CancellationToken ct) => releases.RevokeWaiverAsync(id, waiverId, ct);

    [HttpPost("{id:guid}/execute"), HasPermission(Permissions.TestsExecute)]
    public Task<RunSummaryDto> Execute(Guid id, ExecuteReleaseRequest request, CancellationToken ct) => releases.ExecuteAsync(id, request, null, ct);

    [HttpPost("{id:guid}/request-signoff"), HasPermission(Permissions.ReleasesManage)]
    public async Task<IActionResult> RequestSignOff(Guid id, CancellationToken ct) { await releases.RequestSignOffAsync(id, ct); return NoContent(); }

    [HttpPost("{id:guid}/signoff"), HasPermission(Permissions.ReleasesApprove)]
    public Task<ReleaseWorkspaceDto> SignOff(Guid id, SignOffRequest request, CancellationToken ct) => releases.SignOffAsync(id, request, ct);

    [HttpGet("{id:guid}/report"), HasPermission(Permissions.FeaturesView)]
    public async Task<IActionResult> Report(Guid id, CancellationToken ct) => File(await releases.ReportAsync(id, ct), "application/pdf", $"release-readiness-{DateTime.UtcNow:yyyyMMdd-HHmm}.pdf");

    [HttpGet("{id:guid}/approvals/{approvalId:guid}/report"), HasPermission(Permissions.FeaturesView)]
    public async Task<IActionResult> ApprovalReport(Guid id, Guid approvalId, CancellationToken ct)
    {
        var (content, name) = await releases.ApprovalReportAsync(id, approvalId, ct);
        return File(content, "application/pdf", name);
    }
}

[ApiController, Route("api/v1/quality-gates")]
public sealed class QualityGatesController(IReleaseService releases) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.FeaturesView)]
    public Task<IReadOnlyList<GateDto>> List([FromQuery] Guid productId, CancellationToken ct) => releases.GatesAsync(productId, ct);

    [HttpPost, HasPermission(Permissions.ReleasesManage)]
    public Task<GateDto> Create(GateUpsert request, CancellationToken ct) => releases.SaveGateAsync(null, request, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.ReleasesManage)]
    public Task<GateDto> Update(Guid id, GateUpsert request, CancellationToken ct) => releases.SaveGateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.ReleasesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await releases.DeleteGateAsync(id, ct); return NoContent(); }
}

[ApiController, Route("api/v1/notifications")]
public sealed class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet("inbox")]
    public Task<InboxDto> Inbox([FromQuery] bool unreadOnly = false, [FromQuery] int take = 50, CancellationToken ct = default) => notifications.InboxAsync(unreadOnly, take, ct);

    public sealed record ReadRequest(IReadOnlyList<Guid>? EventIds);

    [HttpPost("inbox/read")]
    public async Task<IActionResult> Read(ReadRequest request, CancellationToken ct) { await notifications.MarkReadAsync(request.EventIds, ct); return NoContent(); }

    [HttpPost("{eventId:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid eventId, CancellationToken ct) { await notifications.AcknowledgeAsync(eventId, ct); return NoContent(); }

    [HttpGet("event-types")]
    public IEnumerable<object> EventTypes() => NotificationEvents.All.Select(e => new { e.Type, e.Description });

    /// <summary>Own subscriptions; ?all=true lists every rule for notification administrators.</summary>
    [HttpGet("rules")]
    public Task<IReadOnlyList<RuleDto>> Rules([FromQuery] bool all = false, CancellationToken ct = default) => notifications.RulesAsync(all, ct);

    [HttpPost("rules")]
    public Task<RuleDto> CreateRule(RuleUpsert request, CancellationToken ct) => notifications.SaveRuleAsync(null, request, ct);

    [HttpPut("rules/{id:guid}")]
    public Task<RuleDto> UpdateRule(Guid id, RuleUpsert request, CancellationToken ct) => notifications.SaveRuleAsync(id, request, ct);

    [HttpDelete("rules/{id:guid}")]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct) { await notifications.DeleteRuleAsync(id, ct); return NoContent(); }

    [HttpGet("channels")]
    public Task<IReadOnlyList<ChannelDto>> Channels(CancellationToken ct) => notifications.ChannelsAsync(ct);

    [HttpPost("channels"), HasPermission(Permissions.NotificationsManage)]
    public Task<ChannelDto> CreateChannel(ChannelUpsert request, CancellationToken ct) => notifications.SaveChannelAsync(null, request, ct);

    [HttpPut("channels/{id:guid}"), HasPermission(Permissions.NotificationsManage)]
    public Task<ChannelDto> UpdateChannel(Guid id, ChannelUpsert request, CancellationToken ct) => notifications.SaveChannelAsync(id, request, ct);

    [HttpDelete("channels/{id:guid}"), HasPermission(Permissions.NotificationsManage)]
    public async Task<IActionResult> DeleteChannel(Guid id, CancellationToken ct) { await notifications.DeleteChannelAsync(id, ct); return NoContent(); }

    [HttpPost("channels/{id:guid}/test"), HasPermission(Permissions.NotificationsManage)]
    public async Task<object> TestChannel(Guid id, CancellationToken ct) => new { eventId = await notifications.TestChannelAsync(id, ct) };

    [HttpGet("escalations"), HasPermission(Permissions.NotificationsManage)]
    public Task<IReadOnlyList<EscalationDto>> Escalations(CancellationToken ct) => notifications.EscalationsAsync(ct);

    [HttpPost("escalations"), HasPermission(Permissions.NotificationsManage)]
    public Task<EscalationDto> CreateEscalation(EscalationUpsert request, CancellationToken ct) => notifications.SaveEscalationAsync(null, request, ct);

    [HttpPut("escalations/{id:guid}"), HasPermission(Permissions.NotificationsManage)]
    public Task<EscalationDto> UpdateEscalation(Guid id, EscalationUpsert request, CancellationToken ct) => notifications.SaveEscalationAsync(id, request, ct);

    [HttpDelete("escalations/{id:guid}"), HasPermission(Permissions.NotificationsManage)]
    public async Task<IActionResult> DeleteEscalation(Guid id, CancellationToken ct) { await notifications.DeleteEscalationAsync(id, ct); return NoContent(); }

    [HttpGet("templates"), HasPermission(Permissions.NotificationsManage)]
    public Task<IReadOnlyList<TemplateDto>> Templates(CancellationToken ct) => notifications.TemplatesAsync(ct);

    [HttpPut("templates/{eventType}"), HasPermission(Permissions.NotificationsManage)]
    public Task<TemplateDto> SaveTemplate(string eventType, TemplateUpsert request, CancellationToken ct) => notifications.SaveTemplateAsync(eventType, request, ct);

    [HttpDelete("templates/{eventType}"), HasPermission(Permissions.NotificationsManage)]
    public async Task<IActionResult> ResetTemplate(string eventType, CancellationToken ct) { await notifications.ResetTemplateAsync(eventType, ct); return NoContent(); }

    [HttpGet("log"), HasPermission(Permissions.NotificationsManage)]
    public Task<IReadOnlyList<EventLogDto>> Log([FromQuery] int take = 100, CancellationToken ct = default) => notifications.EventLogAsync(take, ct);
}

[ApiController, Route("api/v1/dashboards"), HasPermission(Permissions.ReportsView)]
public sealed class DashboardsController(IDashboardService dashboards) : ControllerBase
{
    [HttpGet("global")]
    public Task<GlobalDashboardDto> Global(CancellationToken ct) => dashboards.GlobalAsync(ct);

    [HttpGet("products/{productId:guid}")]
    public Task<ProductDashboardDto> Product(Guid productId, CancellationToken ct) => dashboards.ProductAsync(productId, ct);

    [HttpGet("execution")]
    public Task<ExecutionDashboardDto> Execution(CancellationToken ct) => dashboards.ExecutionAsync(ct);
}

[ApiController, Route("api/v1/reports"), HasPermission(Permissions.ReportsView)]
public sealed class ReportsController(IReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ReportDefinitionDto>> Catalog(CancellationToken ct) => reports.CatalogAsync(ct);

    public sealed record RunReportRequest(ReportParameters Parameters, IReadOnlyList<string>? Columns);

    [HttpPost("{key}/run")]
    public async Task<object> Run(string key, RunReportRequest request, CancellationToken ct)
    {
        var r = await reports.RunAsync(key, request.Parameters, request.Columns, ct);
        // Tuples do not serialize; send the summary as label/value objects.
        return new { r.Key, r.Title, r.Description, r.Definition, r.Columns, r.Rows, Summary = r.Summary.Select(s => new { label = s.Label, value = s.Value }), r.GeneratedAt, r.ParameterText };
    }

    [HttpPost("{key}/export")]
    public async Task<IActionResult> Export(string key, RunReportRequest request, [FromQuery] string format = "xlsx", CancellationToken ct = default)
    {
        var file = await reports.ExportAsync(key, request.Parameters, request.Columns, format, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("saved")]
    public Task<IReadOnlyList<SavedReportDto>> Saved(CancellationToken ct) => reports.SavedAsync(ct);

    [HttpPost("saved")]
    public Task<SavedReportDto> CreateSaved(SavedReportUpsert request, CancellationToken ct) => reports.SaveAsync(null, request, ct);

    [HttpPut("saved/{id:guid}")]
    public Task<SavedReportDto> UpdateSaved(Guid id, SavedReportUpsert request, CancellationToken ct) => reports.SaveAsync(id, request, ct);

    [HttpDelete("saved/{id:guid}")]
    public async Task<IActionResult> DeleteSaved(Guid id, CancellationToken ct) { await reports.DeleteSavedAsync(id, ct); return NoContent(); }
}

/// <summary>Source repositories, builds, change impact, integration keys and issue trackers (user-facing).</summary>
[ApiController, Route("api/v1/integrations")]
public sealed class IntegrationsController(IBuildService builds, IIssueTrackerService trackers, IUserDirectory directory) : ControllerBase
{
    [HttpGet("builds"), HasPermission(Permissions.FeaturesView)]
    public Task<IReadOnlyList<BuildSummaryDto>> Builds([FromQuery] Guid? productId, [FromQuery] int take = 50, CancellationToken ct = default) => builds.ListAsync(productId, take, ct);

    [HttpGet("builds/{id:guid}"), HasPermission(Permissions.FeaturesView)]
    public Task<BuildImpactDto> Build(Guid id, CancellationToken ct) => builds.GetAsync(id, ct);

    /// <summary>What-if impact of a set of changed files or commits, without recording a build.</summary>
    [HttpPost("impact"), HasPermission(Permissions.FeaturesView)]
    public Task<BuildImpactDto> Impact(BuildReport report, CancellationToken ct) => builds.AnalyzeAsync(report, ct);

    /// <summary>Records a build from the UI (CI systems use /api/v1/ci/builds with an integration key).</summary>
    [HttpPost("builds"), HasPermission(Permissions.IntegrationsManage)]
    public Task<BuildImpactDto> Record(BuildReport report, [FromServices] ICurrentUser user, CancellationToken ct) => builds.RecordAsync(report, user.UserName, ct);

    [HttpGet("features/{featureId:guid}/changes"), HasPermission(Permissions.FeaturesView)]
    public Task<IReadOnlyList<FeatureChangeDto>> FeatureChanges(Guid featureId, CancellationToken ct) => builds.FeatureChangesAsync(featureId, ct);

    [HttpGet("repositories"), HasPermission(Permissions.FeaturesView)]
    public Task<IReadOnlyList<RepositoryDto>> Repositories(CancellationToken ct) => builds.RepositoriesAsync(ct);

    [HttpPost("repositories"), HasPermission(Permissions.IntegrationsManage)]
    public Task<RepositoryDto> CreateRepository(RepositoryUpsert request, CancellationToken ct) => builds.SaveRepositoryAsync(null, request, ct);

    [HttpPut("repositories/{id:guid}"), HasPermission(Permissions.IntegrationsManage)]
    public Task<RepositoryDto> UpdateRepository(Guid id, RepositoryUpsert request, CancellationToken ct) => builds.SaveRepositoryAsync(id, request, ct);

    [HttpDelete("repositories/{id:guid}"), HasPermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> DeleteRepository(Guid id, CancellationToken ct) { await builds.DeleteRepositoryAsync(id, ct); return NoContent(); }

    [HttpGet("keys"), HasPermission(Permissions.IntegrationsManage)]
    public Task<IReadOnlyList<IntegrationKeyDto>> Keys(CancellationToken ct) => builds.KeysAsync(ct);

    public sealed record KeyRequest(string Name);

    [HttpPost("keys"), HasPermission(Permissions.IntegrationsManage)]
    public Task<IntegrationKeyResult> CreateKey(KeyRequest request, CancellationToken ct) => builds.CreateKeyAsync(request.Name, ct);

    public sealed record EnabledRequest(bool Enabled);

    [HttpPut("keys/{id:guid}/enabled"), HasPermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> EnableKey(Guid id, EnabledRequest request, CancellationToken ct) { await builds.SetKeyEnabledAsync(id, request.Enabled, ct); return NoContent(); }

    [HttpDelete("keys/{id:guid}"), HasPermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> DeleteKey(Guid id, CancellationToken ct) { await builds.DeleteKeyAsync(id, ct); return NoContent(); }

    [HttpGet("trackers"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<TrackerDto>> Trackers(CancellationToken ct) => trackers.ListAsync(ct);

    [HttpPost("trackers"), HasPermission(Permissions.IntegrationsManage)]
    public Task<TrackerDto> CreateTracker(TrackerUpsert request, CancellationToken ct) => trackers.SaveAsync(null, request, ct);

    [HttpPut("trackers/{id:guid}"), HasPermission(Permissions.IntegrationsManage)]
    public Task<TrackerDto> UpdateTracker(Guid id, TrackerUpsert request, CancellationToken ct) => trackers.SaveAsync(id, request, ct);

    [HttpDelete("trackers/{id:guid}"), HasPermission(Permissions.IntegrationsManage)]
    public async Task<IActionResult> DeleteTracker(Guid id, CancellationToken ct) { await trackers.DeleteAsync(id, ct); return NoContent(); }

    /// <summary>Active users for assignee and subscription pickers.</summary>
    [HttpGet("/api/v1/users/directory"), HasPermission(Permissions.TestCasesView)]
    public async Task<IEnumerable<object>> Directory(CancellationToken ct)
        => (await directory.ListAsync(ct)).Select(u => new { u.Id, u.UserName, u.DisplayName });
}

/// <summary>Endpoints for CI/CD systems, authenticated with an integration key (header X-Wings-Integration-Key).</summary>
[ApiController, Route("api/v1/ci"), Authorize(Policy = IntegrationKeyHandler.Policy)]
public sealed class CiController(IBuildService builds, IReleaseService releases, ICurrentUser user) : ControllerBase
{
    /// <summary>Records a build with its changed files/commits, computes impact and optionally runs the affected tests.</summary>
    [HttpPost("builds")]
    public Task<BuildImpactDto> Build(BuildReport report, CancellationToken ct) => builds.RecordAsync(report, user.UserName, ct);

    /// <summary>Quality-gate verdict for a release candidate. ?enforce=true answers 409 when blocked so a pipeline step fails.</summary>
    [HttpGet("gates")]
    public async Task<IActionResult> Gates([FromQuery] string product, [FromQuery] string release, [FromQuery] bool enforce = false, CancellationToken ct = default)
    {
        var result = await releases.CheckAsync(product, release, ct);
        return enforce && result.Blocked ? Conflict(result) : Ok(result);
    }

    [HttpGet("builds/{id:guid}")]
    public Task<BuildImpactDto> Get(Guid id, CancellationToken ct) => builds.GetAsync(id, ct);
}
