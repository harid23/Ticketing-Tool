using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Dtos;
using ReleaseTicketing.Core.Exceptions;
using ReleaseTicketing.Core.Services;

namespace ReleaseTicketing.Api.Controllers;

// Controllers stay thin: bind the request, call ONE service method, return the right HTTP status.
// All rules and permission checks live in the services.

[ApiController, Route("api/v1")]
public class ReferenceDataController(ReferenceDataService svc) : ControllerBase
{
    [HttpGet("me")] public ActionResult<MeDto> Me() => Ok(svc.Me());

    [HttpGet("employees/{employeeId}")]
    public async Task<ActionResult<EmployeeDto>> GetEmployee(string employeeId, CancellationToken ct)
    {
        var e = await svc.GetEmployeeAsync(employeeId, ct);
        if (e is null) return NotFound(new ProblemDetails { Status = 404, Title = "Employee not found", Detail = "Not in the employee master. You may enter details manually." });
        return Ok(e);
    }

    [HttpGet("clusters")] public async Task<IReadOnlyList<ClusterDto>> Clusters(CancellationToken ct) => await svc.GetClustersAsync(ct);
    [HttpGet("release-categories")] public async Task<IReadOnlyList<ReleaseCategoryDto>> Categories(CancellationToken ct) => await svc.GetCategoriesAsync(ct);
}

[ApiController, Route("api/v1/release-requests")]
public class ReleaseRequestsController(ReleaseRequestService svc) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<PagedResult<ReleaseRequestSummaryDto>>> List([FromQuery] ReleaseRequestQuery q, CancellationToken ct) => Ok(await svc.ListAsync(q, ct));

    [HttpGet("{ticketId:int}")] public async Task<ActionResult<ReleaseRequestDetailDto>> Get(int ticketId, CancellationToken ct) => Ok(await svc.GetAsync(ticketId, ct));

    [HttpPost]
    public async Task<ActionResult<ReleaseRequestDetailDto>> Create(CreateReleaseRequestDto dto, CancellationToken ct)
    {
        var created = await svc.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { ticketId = created.Id }, created);
    }

    [HttpPut("{ticketId:int}")] public async Task<ActionResult<ReleaseRequestDetailDto>> Update(int ticketId, UpdateReleaseRequestDto dto, CancellationToken ct) => Ok(await svc.UpdateAsync(ticketId, dto, ct));
    [HttpPost("{ticketId:int}/cancel")] public async Task<ActionResult<ReleaseRequestDetailDto>> Cancel(int ticketId, CancelDto dto, CancellationToken ct) => Ok(await svc.CancelAsync(ticketId, dto, ct));
    [HttpPost("{ticketId:int}/close")] public async Task<ActionResult<ReleaseRequestDetailDto>> Close(int ticketId, CloseDto dto, CancellationToken ct) => Ok(await svc.CloseAsync(ticketId, dto, ct));
    [HttpPost("{ticketId:int}/sla-exception/approve")]
    public async Task<ActionResult<ReleaseRequestDetailDto>> ApproveSlaException(int ticketId, CancellationToken ct)
        => Ok(await svc.ApproveSlaExceptionAsync(ticketId, ct));

    [HttpPost("{ticketId:int}/reopen")]
    public async Task<ActionResult<ReleaseRequestDetailDto>> Reopen(int ticketId, ReopenDto dto, CancellationToken ct)
    {
        var created = await svc.ReopenAsync(ticketId, dto, ct);
        return CreatedAtAction(nameof(Get), new { ticketId = created.Id }, created);
    }

    [HttpGet("{ticketId:int}/history")] public async Task<IReadOnlyList<HistoryDto>> History(int ticketId, CancellationToken ct) => await svc.GetHistoryAsync(ticketId, ct);

    [HttpGet("{ticketId:int}/audit"), Authorize(Roles = $"{RoleNames.Head},{RoleNames.Admin},{RoleNames.Pmo}")]
    public async Task<IReadOnlyList<AuditDto>> Audit(int ticketId, CancellationToken ct) => await svc.GetAuditAsync(ticketId, ct);
}

[ApiController, Route("api/v1/release-requests/{ticketId:int}/evaluations")]
public class EvaluationsController(EvaluationService svc, ReleaseRequestService tickets) : ControllerBase
{
    [HttpGet] public async Task<IReadOnlyList<EvaluationDto>> List(int ticketId, CancellationToken ct) => (await tickets.GetAsync(ticketId, ct)).Evaluations;

    [HttpPost("{evaluationId:int}/respond")]
    public async Task<ActionResult<EvaluationDto>> Respond(int ticketId, int evaluationId, RespondDto dto, CancellationToken ct)
        => Ok(await svc.RespondAsync(ticketId, evaluationId, dto.Response!.Value, ct));

    [HttpPut("{evaluationId:int}/status")]
    public async Task<ActionResult<EvaluationDto>> UpdateStatus(int ticketId, int evaluationId, UpdateAllocationStatusDto dto, CancellationToken ct)
        => Ok(await svc.UpdateAllocationStatusAsync(ticketId, evaluationId, dto, ct));

    [HttpPost("{evaluationId:int}/start")] public async Task<ActionResult<EvaluationDto>> Start(int ticketId, int evaluationId, CancellationToken ct) => Ok(await svc.StartAsync(ticketId, evaluationId, ct));
    [HttpPost("{evaluationId:int}/complete")] public async Task<ActionResult<EvaluationDto>> Complete(int ticketId, int evaluationId, CompleteEvaluationDto dto, CancellationToken ct) => Ok(await svc.CompleteAsync(ticketId, evaluationId, dto, ct));
}

[ApiController, Route("api/v1/resources")]
public class AvailableResourcesController(EvaluationService svc) : ControllerBase
{
    [HttpGet("available")]
    public async Task<ActionResult<PagedResult<AvailableResourceDto>>> Available([FromQuery] AvailableResourcesQuery q, CancellationToken ct)
        => Ok(await svc.GetAvailableResourcesAsync(q, ct));
}

[ApiController, Route("api/v1/cluster-evaluations")]
public class ClusterEvaluationInboxController(EvaluationService svc) : ControllerBase
{
    [HttpGet("owned")]
    public async Task<ActionResult<IReadOnlyList<AllocationTicketDto>>> Owned(CancellationToken ct)
        => Ok(await svc.GetOwnedAllocationTicketsAsync(ct));

    [HttpGet("pending")]
    public async Task<ActionResult<IReadOnlyList<ClusterEvaluationInboxItemDto>>> Pending(CancellationToken ct)
        => Ok(await svc.GetPendingForCurrentClusterAsync(ct));
}

[ApiController, Route("api/v1/notifications")]
public class ActivityNotificationsController(ActivityFeedService svc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ActivityNotificationDto>>> Recent(CancellationToken ct)
        => Ok(await svc.GetRecentAsync(ct));
}

[ApiController, Route("api/v1/local-demo")]
public class LocalDemoController(IWebHostEnvironment environment, LocalDemoScenarioService scenarios) : ControllerBase
{
    [HttpPost("scenarios")]
    public async Task<ActionResult<IReadOnlyList<LocalDemoScenarioDto>>> SeedScenarios(CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();
        return Ok(await scenarios.SeedAsync(ct));
    }
}

[ApiController, Route("api/v1/release-requests/{ticketId:int}/attachments")]
public class AttachmentsController(AttachmentService svc) : ControllerBase
{
    [HttpGet] public async Task<IReadOnlyList<AttachmentDto>> List(int ticketId, CancellationToken ct) => await svc.ListAsync(ticketId, ct);

    [HttpPost, RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<AttachmentDto>> Upload(int ticketId, [FromForm] string kind, IFormFile file, CancellationToken ct)
    {
        if (file is null) throw new RequestValidationException("file", "A file is required.");
        await using var stream = file.OpenReadStream();
        var created = await svc.UploadAsync(ticketId, kind, file.FileName, file.ContentType, file.Length, stream, ct);
        return CreatedAtAction(nameof(List), new { ticketId }, created);
    }

    [HttpGet("{attachmentId:int}/download")]
    public async Task<IActionResult> Download(int ticketId, int attachmentId, CancellationToken ct)
    {
        var (content, name, type) = await svc.DownloadAsync(ticketId, attachmentId, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(content, string.IsNullOrWhiteSpace(type) ? "application/octet-stream" : type, name);   // sent as a download, never rendered inline
    }
}

[ApiController, Route("api/v1/dashboard")]
public class DashboardController(ReleaseRequestService svc) : ControllerBase
{
    [HttpGet("summary")] public async Task<DashboardDto> Summary(CancellationToken ct) => await svc.DashboardAsync(ct);
}
