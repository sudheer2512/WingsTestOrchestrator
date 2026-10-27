using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wings.Api.Security;
using Wings.Application.Execution;
using Wings.Application.Orchestration;
using Wings.Application.Security;
using Wings.Domain.Orchestration;

namespace Wings.Api.Controllers;

[ApiController, Route("api/v1/environments")]
public sealed class EnvironmentsController(IEnvironmentService environments) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<EnvironmentDto>> List(CancellationToken ct) => environments.ListAsync(ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TestCasesView)]
    public Task<EnvironmentDto> Get(Guid id, CancellationToken ct) => environments.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.EnvironmentsManage)]
    public Task<EnvironmentDto> Create(EnvironmentUpsert request, CancellationToken ct) => environments.CreateAsync(request, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.EnvironmentsManage)]
    public Task<EnvironmentDto> Update(Guid id, EnvironmentUpsert request, CancellationToken ct) => environments.UpdateAsync(id, request, ct);

    public sealed record CloneRequest(string Code, string Name);

    [HttpPost("{id:guid}/clone"), HasPermission(Permissions.EnvironmentsManage)]
    public Task<EnvironmentDto> Clone(Guid id, CloneRequest request, CancellationToken ct) => environments.CloneAsync(id, request.Code, request.Name, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.EnvironmentsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await environments.DeleteAsync(id, ct); return NoContent(); }

    [HttpGet("compare"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<EnvironmentComparisonRow>> Compare([FromQuery] Guid left, [FromQuery] Guid right, CancellationToken ct) => environments.CompareAsync(left, right, ct);

    [HttpPost("{id:guid}/health-check"), HasPermission(Permissions.TestCasesView)]
    public Task<EnvironmentDto> HealthCheck(Guid id, CancellationToken ct) => environments.CheckHealthAsync(id, ct);
}

[ApiController, Route("api/v1/test-data")]
public sealed class TestDataController(ITestDataService data) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<DataSetDto>> List(CancellationToken ct) => data.ListAsync(ct);

    [HttpPost, HasPermission(Permissions.TestDataManage)]
    public Task<DataSetDto> Create(DataSetUpsert request, CancellationToken ct) => data.CreateAsync(request, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.TestDataManage)]
    public Task<DataSetDto> Update(Guid id, DataSetUpsert request, CancellationToken ct) => data.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.TestDataManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await data.DeleteAsync(id, ct); return NoContent(); }

    [HttpGet("{id:guid}/records"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<DataRecordDto>> Records(Guid id, CancellationToken ct) => data.RecordsAsync(id, ct);

    [HttpPut("{id:guid}/records"), HasPermission(Permissions.TestDataManage)]
    public Task<DataRecordDto> UpsertRecord(Guid id, DataRecordUpsert request, CancellationToken ct) => data.UpsertRecordAsync(id, request, ct);

    [HttpDelete("{id:guid}/records/{recordId:guid}"), HasPermission(Permissions.TestDataManage)]
    public async Task<IActionResult> DeleteRecord(Guid id, Guid recordId, CancellationToken ct) { await data.DeleteRecordAsync(id, recordId, ct); return NoContent(); }

    [HttpPost("{id:guid}/import"), HasPermission(Permissions.TestDataManage), RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ImportSummary> Import(Guid id, IFormFile file, [FromForm] bool replace, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return await data.ImportAsync(id, s, Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant(), replace, ct);
    }

    [HttpGet("reservations"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<ReservationDto>> Reservations([FromQuery] bool activeOnly = true, CancellationToken ct = default) => data.ReservationsAsync(activeOnly, ct);

    [HttpPost("reservations/{reservationId:guid}/release"), HasPermission(Permissions.TestDataManage)]
    public async Task<IActionResult> Release(Guid reservationId, CancellationToken ct) { await data.ReleaseAsync(reservationId, ct); return NoContent(); }
}

[ApiController, Route("api/v1/plans")]
public sealed class PlansController(IPlanService plans) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<PlanSummaryDto>> List([FromQuery] bool templates = false, CancellationToken ct = default) => plans.ListAsync(templates, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TestCasesView)]
    public Task<PlanDetailDto> Get(Guid id, CancellationToken ct) => plans.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.PlansManage)]
    public Task<PlanDetailDto> Create(PlanUpsert request, CancellationToken ct) => plans.CreateAsync(request, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.PlansManage)]
    public Task<PlanDetailDto> Update(Guid id, PlanUpsert request, CancellationToken ct) => plans.UpdateAsync(id, request, ct);

    public sealed record ClonePlanRequest(string Name, bool AsTemplate);

    [HttpPost("{id:guid}/clone"), HasPermission(Permissions.PlansManage)]
    public Task<PlanDetailDto> Clone(Guid id, ClonePlanRequest request, CancellationToken ct) => plans.CloneAsync(id, request.Name, request.AsTemplate, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.PlansManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await plans.DeleteAsync(id, ct); return NoContent(); }

    public sealed record StatusRequest(PlanStatus Status, string? Comment);

    [HttpPost("{id:guid}/status"), HasPermission(Permissions.PlansManage)]
    public Task<PlanDetailDto> Status(Guid id, StatusRequest request, CancellationToken ct) => plans.SetStatusAsync(id, request.Status, request.Comment, ct);

    [HttpPost("{id:guid}/execute"), HasPermission(Permissions.TestsExecute)]
    public Task<RunSummaryDto> Execute(Guid id, ExecutePlanRequest request, CancellationToken ct) => plans.ExecuteAsync(id, request, null, ct);
}

[ApiController, Route("api/v1/campaigns")]
public sealed class CampaignsController(ICampaignService campaigns) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<CampaignDto>> List(CancellationToken ct) => campaigns.ListAsync(ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TestCasesView)]
    public Task<CampaignDto> Get(Guid id, CancellationToken ct) => campaigns.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.PlansManage)]
    public Task<CampaignDto> Create(CampaignUpsert request, CancellationToken ct) => campaigns.CreateAsync(request, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.PlansManage)]
    public Task<CampaignDto> Update(Guid id, CampaignUpsert request, CancellationToken ct) => campaigns.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.PlansManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await campaigns.DeleteAsync(id, ct); return NoContent(); }

    [HttpPost("{id:guid}/execute"), HasPermission(Permissions.TestsExecute)]
    public Task<IReadOnlyList<RunSummaryDto>> Execute(Guid id, CancellationToken ct) => campaigns.ExecuteAsync(id, null, ct);
}

[ApiController, Route("api/v1/schedules")]
public sealed class SchedulesController(IScheduleService schedules) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<ScheduleDto>> List(CancellationToken ct) => schedules.ListAsync(ct);

    [HttpPost, HasPermission(Permissions.SchedulesManage)]
    public Task<ScheduleDto> Create(ScheduleUpsert request, CancellationToken ct) => schedules.CreateAsync(request, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.SchedulesManage)]
    public Task<ScheduleDto> Update(Guid id, ScheduleUpsert request, CancellationToken ct) => schedules.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.SchedulesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await schedules.DeleteAsync(id, ct); return NoContent(); }

    [HttpPost("{id:guid}/webhook-token"), HasPermission(Permissions.SchedulesManage)]
    public Task<WebhookTokenResult> RotateToken(Guid id, CancellationToken ct) => schedules.RotateTokenAsync(id, $"{Request.Scheme}://{Request.Host}", ct);

    [HttpPost("{id:guid}/run-now"), HasPermission(Permissions.TestsExecute)]
    public Task<IReadOnlyList<RunSummaryDto>> RunNow(Guid id, CancellationToken ct) => schedules.RunNowAsync(id, ct);
}

/// <summary>Called by build and deployment pipelines. Authenticated by the schedule's own token, not a user.</summary>
[ApiController, Route("api/v1/triggers"), AllowAnonymous, EnableRateLimiting("login")]
public sealed class TriggersController(IScheduleService schedules) : ControllerBase
{
    public const string TokenHeader = "X-Wings-Trigger-Token";

    [HttpPost("schedules/{id:guid}")]
    public async Task<object> Trigger(Guid id, [FromHeader(Name = TokenHeader)] string? token, [FromBody] TriggerRequest? request, CancellationToken ct)
    {
        var runs = await schedules.TriggerAsync(id, token ?? "", request ?? new TriggerRequest(), ct);
        return new { runs = runs.Select(r => new { r.Id, r.NumberDisplay, r.Status }) };
    }
}
