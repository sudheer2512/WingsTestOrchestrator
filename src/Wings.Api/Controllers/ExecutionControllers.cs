using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wings.Agent.Contracts;
using Wings.Api.Security;
using Wings.Application.Common;
using Wings.Application.Execution;
using Wings.Application.Security;
using Wings.Domain.Execution;

namespace Wings.Api.Controllers;

/// <summary>Endpoints called by execution agents (agent-key authentication only).</summary>
[ApiController, Route("api/v1/agent"), Authorize(AuthenticationSchemes = AgentKeyHandler.Scheme, Policy = AgentKeyHandler.Policy)]
public sealed class AgentProtocolController(IAgentGateway gateway) : ControllerBase
{
    private Guid AgentId => Guid.Parse(User.FindFirst(AgentKeyHandler.AgentIdClaim)!.Value);

    [HttpPost("register")]
    public Task<AgentRegistrationResponse> Register(AgentRegistration registration, CancellationToken ct) => gateway.RegisterAsync(AgentId, registration, ct);

    [HttpPost("heartbeat")]
    public Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request, CancellationToken ct) => gateway.HeartbeatAsync(AgentId, request, ct);

    [HttpPost("jobs/claim")]
    public Task<ClaimResponse> Claim(ClaimRequest request, CancellationToken ct) => gateway.ClaimAsync(AgentId, request, ct);

    [HttpGet("packages/{versionId:guid}")]
    public async Task<IActionResult> Package(Guid versionId, CancellationToken ct)
    {
        var (content, name) = await gateway.DownloadPackageAsync(AgentId, versionId, ct);
        return File(content, "application/octet-stream", name);
    }

    [HttpPost("jobs/{jobId:guid}/start")]
    public async Task<IActionResult> Start(Guid jobId, CancellationToken ct) { await gateway.StartJobAsync(AgentId, jobId, ct); return NoContent(); }

    [HttpPost("jobs/{jobId:guid}/logs")]
    public async Task<IActionResult> Logs(Guid jobId, LogBatch batch, CancellationToken ct) { await gateway.AppendLogsAsync(AgentId, jobId, batch, ct); return NoContent(); }

    [HttpPost("jobs/{jobId:guid}/results")]
    public async Task<IActionResult> Results(Guid jobId, ResultBatch batch, CancellationToken ct) { await gateway.ReportResultsAsync(AgentId, jobId, batch, ct); return NoContent(); }

    [HttpPost("jobs/{jobId:guid}/artifacts"), RequestSizeLimit(600L * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 600L * 1024 * 1024)]
    public async Task<object> Artifact(Guid jobId, IFormFile file, [FromForm] string kind, [FromForm] string? testIdentifier, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return new { id = await gateway.UploadArtifactAsync(AgentId, jobId, kind, file.FileName, file.ContentType, testIdentifier, s, ct) };
    }

    [HttpPost("jobs/{jobId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid jobId, JobCompletion completion, CancellationToken ct) { await gateway.CompleteJobAsync(AgentId, jobId, completion, ct); return NoContent(); }
}

[ApiController, Route("api/v1/runs")]
public sealed class RunsController(IExecutionService runs) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<PagedResult<RunSummaryDto>> List([FromQuery] RunFilter filter, CancellationToken ct) => runs.ListAsync(filter, ct);

    [HttpPost, HasPermission(Permissions.TestsExecute)]
    public async Task<ActionResult<RunSummaryDto>> Start(StartRunRequest request, CancellationToken ct)
    {
        var run = await runs.StartAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = run.Id }, run);
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.TestCasesView)]
    public Task<RunDetailDto> Get(Guid id, CancellationToken ct) => runs.GetAsync(id, ct);

    [HttpGet("jobs/{jobId:guid}/logs"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<LogLineDto>> Logs(Guid jobId, [FromQuery] int after = 0, [FromQuery] int max = 2000, CancellationToken ct = default) => runs.LogsAsync(jobId, after, max, ct);

    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.TestsExecute)]
    public Task<RunSummaryDto> Cancel(Guid id, CancellationToken ct) => runs.CancelAsync(id, ct);

    [HttpPost("{id:guid}/pause"), HasPermission(Permissions.TestsExecute)]
    public Task<RunSummaryDto> Pause(Guid id, CancellationToken ct) => runs.SetPausedAsync(id, true, ct);

    [HttpPost("{id:guid}/resume"), HasPermission(Permissions.TestsExecute)]
    public Task<RunSummaryDto> Resume(Guid id, CancellationToken ct) => runs.SetPausedAsync(id, false, ct);

    public sealed record PriorityRequest(int Priority);

    [HttpPut("{id:guid}/priority"), HasPermission(Permissions.TestsExecute)]
    public Task<RunSummaryDto> Priority(Guid id, PriorityRequest request, CancellationToken ct) => runs.SetPriorityAsync(id, request.Priority, ct);

    /// <summary>Evidence download. Images and video are shown inline; everything else is forced to download so it cannot run script in this origin.</summary>
    [HttpGet("{id:guid}/artifacts/{artifactId:guid}"), HasPermission(Permissions.TestCasesView)]
    public async Task<IActionResult> Artifact(Guid id, Guid artifactId, [FromQuery] bool download = false, CancellationToken ct = default)
    {
        var (meta, content) = await runs.OpenArtifactAsync(id, artifactId, ct);
        var inline = !download && meta.ContentType is "image/png" or "image/jpeg" or "image/webp" or "video/webm" or "video/mp4";
        if (inline)
        {
            Response.Headers.ContentDisposition = $"inline; filename=\"{meta.FileName.Replace("\"", "")}\"";
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src 'self'; media-src 'self'";
            return File(content, meta.ContentType, enableRangeProcessing: true);
        }
        return File(content, "application/octet-stream", meta.FileName);
    }

    [HttpPost("{id:guid}/tests/{testId:guid}/manual"), HasPermission(Permissions.TestsExecute)]
    public Task<TestExecutionDto> Manual(Guid id, Guid testId, ManualResultRequest request, CancellationToken ct) => runs.RecordManualAsync(id, testId, request, ct);

    [HttpGet("locks"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<ResourceLockDto>> Locks(CancellationToken ct) => runs.ResourceLocksAsync(ct);

    [HttpGet("history"), HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<ExecutionHistoryDto>> History([FromQuery] Guid? featureId, [FromQuery] Guid? testCaseId, [FromQuery] int take = 100, CancellationToken ct = default)
        => runs.HistoryAsync(featureId, testCaseId, take, ct);
}

[ApiController, Route("api/v1/agents")]
public sealed class AgentsController(IAgentAdminService agents) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TestCasesView)]
    public Task<IReadOnlyList<AgentDto>> List(CancellationToken ct) => agents.ListAsync(ct);

    [HttpPost, HasPermission(Permissions.AgentsManage)]
    public Task<AgentKeyResult> Create(AgentUpsert request, CancellationToken ct) => agents.CreateAsync(request, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.AgentsManage)]
    public Task<AgentDto> Update(Guid id, AgentUpsert request, CancellationToken ct) => agents.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/rotate-key"), HasPermission(Permissions.AgentsManage)]
    public Task<AgentKeyResult> Rotate(Guid id, CancellationToken ct) => agents.RotateKeyAsync(id, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.AgentsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await agents.DeleteAsync(id, ct); return NoContent(); }
}
