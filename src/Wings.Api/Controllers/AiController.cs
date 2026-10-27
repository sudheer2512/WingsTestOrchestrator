using Microsoft.AspNetCore.Mvc;
using Wings.Api.Security;
using Wings.Application.Ai;
using Wings.Application.Common;
using Wings.Application.Security;
using Wings.Application.Testing;

namespace Wings.Api.Controllers;

/// <summary>
/// AI testing assistant. Asking needs ai.use; applying a suggestion needs the permission of the normal action it turns into
/// (creating test cases, uploading or editing test code, commenting on defects), and the result follows the usual review workflow.
/// </summary>
[ApiController, Route("api/v1/ai")]
public sealed class AiController(IAiAssistant ai) : ControllerBase
{
    [HttpGet("status")]
    public Task<AiStatusDto> Status(CancellationToken ct) => ai.StatusAsync(ct);

    [HttpGet("policy"), HasPermission(Permissions.AiManage)]
    public Task<AiPolicyDto> Policy(CancellationToken ct) => ai.PolicyAsync(ct);

    [HttpPut("policy"), HasPermission(Permissions.AiManage)]
    public Task<AiPolicyDto> SavePolicy(AiPolicyDto request, CancellationToken ct) => ai.SavePolicyAsync(request, ct);

    [HttpPost("test-suggestions"), HasPermission(Permissions.AiUse)]
    public Task<TestGenerationResult> SuggestTests(GenerateTestsRequest request, CancellationToken ct) => ai.GenerateTestsAsync(request, ct);

    [HttpPost("test-suggestions/{id:guid}/apply"), HasPermission(Permissions.TestCasesEdit)]
    public Task<ApplyResult> ApplyTests(Guid id, ApplyTestsRequest request, CancellationToken ct) => ai.ApplyTestsAsync(id, request, ct);

    [HttpPost("code"), HasPermission(Permissions.AiUse)]
    public Task<CodeGenerationResult> GenerateCode(GenerateCodeRequest request, CancellationToken ct) => ai.GenerateCodeAsync(request, ct);

    [HttpPost("code/{id:guid}/apply"), HasPermission(Permissions.TestCodeUpload)]
    public Task<PackageVersionSummaryDto> ApplyCode(Guid id, ApplyCodeRequest request, CancellationToken ct) => ai.ApplyCodeAsync(id, request, ct);

    [HttpPost("failure-analysis/{testExecutionId:guid}"), HasPermission(Permissions.AiUse)]
    public Task<FailureAnalysisResult> AnalyzeFailure(Guid testExecutionId, CancellationToken ct) => ai.AnalyzeFailureAsync(testExecutionId, ct);

    [HttpPost("failure-analysis/{id:guid}/defects/{defectId:guid}"), HasPermission(Permissions.DefectsManage)]
    public async Task<IActionResult> AnalysisToDefect(Guid id, Guid defectId, CancellationToken ct) { await ai.ApplyAnalysisToDefectAsync(id, defectId, ct); return NoContent(); }

    [HttpPost("test-selection"), HasPermission(Permissions.AiUse)]
    public Task<SelectionResult> SelectTests(SelectTestsRequest request, CancellationToken ct) => ai.SelectTestsAsync(request, ct);

    [HttpPost("maintenance/{productId:guid}"), HasPermission(Permissions.AiUse)]
    public Task<MaintenanceResult> Maintenance(Guid productId, CancellationToken ct) => ai.MaintenanceAsync(productId, ct);

    [HttpPost("repairs/{testExecutionId:guid}"), HasPermission(Permissions.AiUse)]
    public Task<RepairResult> ProposeRepair(Guid testExecutionId, CancellationToken ct) => ai.ProposeRepairAsync(testExecutionId, ct);

    [HttpPost("repairs/{id:guid}/apply"), HasPermission(Permissions.TestCodeEdit)]
    public Task<PackageVersionSummaryDto> ApplyRepair(Guid id, CancellationToken ct) => ai.ApplyRepairAsync(id, ct);

    [HttpGet("history")]
    public Task<PagedResult<AiInteractionDto>> History([FromQuery] bool mine = true, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => ai.HistoryAsync(mine, page, pageSize, ct);

    [HttpGet("history/{id:guid}")]
    public Task<AiInteractionDto> Interaction(Guid id, CancellationToken ct) => ai.InteractionAsync(id, ct);

    [HttpPost("history/{id:guid}/feedback")]
    public async Task<IActionResult> Feedback(Guid id, AiFeedbackRequest request, CancellationToken ct) { await ai.FeedbackAsync(id, request, ct); return NoContent(); }
}
