using Microsoft.EntityFrameworkCore;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Dtos;
using ReleaseTicketing.Core.Exceptions;

namespace ReleaseTicketing.Core.Services;

/// <summary>
/// Owns the per-cluster allocation workflow. Every mutation locks the parent request in a transaction,
/// so competing clusters can progress independently while only one can select the resource.
/// </summary>
public class EvaluationService(AppDbContext db, AuditService audit, NotificationService notify, TicketWorkflow wf, ICurrentUser user, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<AvailableResourceDto>> GetAvailableResourcesAsync(AvailableResourcesQuery q, CancellationToken ct)
    {
        user.Require(RoleNames.Pm, RoleNames.Asdm, RoleNames.Admin);
        var now = Now;
        var query = db.ReleaseRequests.AsNoTracking()
            .Where(t => TicketStateMachine.ActiveStatuses.Contains(t.Status) &&
                t.ReleaseCategory.RequiresClusterCirculation &&
                t.EvaluationDeadlineAt != null && t.EvaluationDeadlineAt > now &&
                !t.Evaluations.Any(e => e.Status == EvaluationStatus.Selected));

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim();
            query = query.Where(t => t.EmployeeId.Contains(search) || t.EmployeeName.Contains(search) ||
                t.EmployeeSkills.Contains(search) || t.CurrentProjectName.Contains(search) || t.TicketNumber.Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(q.Grade))
        {
            var grade = q.Grade.Trim();
            query = query.Where(t => t.Grade.Contains(grade));
        }
        if (!string.IsNullOrWhiteSpace(q.Location))
        {
            var location = q.Location.Trim();
            query = query.Where(t => t.Location.Contains(location));
        }

        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var requests = await query
            .OrderBy(t => t.EvaluationDeadlineAt).ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Include(t => t.ReleaseCategory).Include(t => t.InitiatingCluster).Include(t => t.Evaluations)
            .ToListAsync(ct);

        var clusterId = user.IsInRole(RoleNames.Asdm) ? user.ClusterId : null;
        var items = requests.Select(t =>
        {
            var mine = clusterId is null ? null : t.Evaluations.FirstOrDefault(e => e.ClusterId == clusterId.Value);
            return new AvailableResourceDto(
                t.ReleaseRequestId, t.TicketNumber, t.EmployeeId, t.EmployeeName, t.Grade, t.Location,
                t.CurrentProjectName, t.EmployeeSkills, t.InitiatingCluster.Code, t.ReleaseCategory.Name,
                t.ReleaseDate, t.CreatedAt, t.EvaluationDeadlineAt, mine?.EvaluationId, mine?.Status,
                user.IsInRole(RoleNames.Asdm) && mine?.Status == EvaluationStatus.Pending);
        }).ToList();
        return new PagedResult<AvailableResourceDto>(items, page, size, total);
    }

    public async Task<IReadOnlyList<AllocationTicketDto>> GetOwnedAllocationTicketsAsync(CancellationToken ct)
    {
        user.Require(RoleNames.Pm, RoleNames.Asdm);
        var isAsdm = user.IsInRole(RoleNames.Asdm);
        var currentClusterId = user.ClusterId;
        var now = Now;
        var query = db.ClusterEvaluations.AsNoTracking()
            .Where(e => e.Response == ClusterResponse.YesInterested)
            .AsQueryable();

        if (user.IsInRole(RoleNames.Asdm))
        {
            var clusterId = user.ClusterId ?? throw new ForbiddenException("Your account is not assigned to a cluster.");
            query = query.Where(e => e.ClusterId == clusterId);
        }
        else if (user.IsInRole(RoleNames.Pm))
        {
            var userId = user.UserId;
            query = query.Where(e => e.ReleaseRequest.CreatedBy == userId);
        }

        return await query
            .OrderBy(e => e.ReleaseRequest.EvaluationDeadlineAt).ThenBy(e => e.ReleaseRequest.CreatedAt)
            .Select(e => new AllocationTicketDto(
                e.ReleaseRequestId, e.EvaluationId, e.ReleaseRequest.TicketNumber,
                e.ReleaseRequest.EmployeeId, e.ReleaseRequest.EmployeeName, e.ReleaseRequest.Grade,
                e.ReleaseRequest.Location, e.ReleaseRequest.CurrentProjectName, e.ReleaseRequest.EmployeeSkills,
                e.ClusterId, e.Cluster.Code, e.ResponseAt, e.Status, e.ReleaseRequest.Status, e.ReleaseRequest.CreatedAt,
                e.ReleaseRequest.EvaluationDeadlineAt, e.RejectionReason,
                (isAsdm && currentClusterId == e.ClusterId) &&
                TicketStateMachine.ActiveStatuses.Contains(e.ReleaseRequest.Status) &&
                (e.ReleaseRequest.EvaluationDeadlineAt == null || e.ReleaseRequest.EvaluationDeadlineAt > now) &&
                (e.Status == EvaluationStatus.Interested || e.Status == EvaluationStatus.Assigned ||
                    e.Status == EvaluationStatus.InProgress || e.Status == EvaluationStatus.InterviewScheduled ||
                    e.Status == EvaluationStatus.WaitingForResults)))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ClusterEvaluationInboxItemDto>> GetPendingForCurrentClusterAsync(CancellationToken ct)
    {
        user.Require(RoleNames.Asdm);
        var clusterId = user.ClusterId ?? throw new ForbiddenException("Your account is not assigned to a cluster.");

        return await db.ClusterEvaluations.AsNoTracking()
            .Where(e => e.ClusterId == clusterId && e.Status == EvaluationStatus.Pending &&
                e.ReleaseRequest.EvaluationDeadlineAt > Now &&
                e.ReleaseRequest.Status != TicketStatus.Closed && e.ReleaseRequest.Status != TicketStatus.Cancelled)
            .OrderBy(e => e.ReleaseRequest.EvaluationDeadlineAt)
            .ThenBy(e => e.ReleaseRequest.CreatedAt)
            .Select(e => new ClusterEvaluationInboxItemDto(
                e.ReleaseRequestId, e.EvaluationId, e.ReleaseRequest.TicketNumber,
                e.ReleaseRequest.EmployeeId, e.ReleaseRequest.EmployeeName,
                e.ReleaseRequest.Grade, e.ReleaseRequest.Location, e.ReleaseRequest.CurrentProjectName,
                e.ReleaseRequest.EmployeeSkills, e.ReleaseRequest.ReleaseDate, e.ReleaseRequest.ReleaseLocation,
                e.ReleaseRequest.ReasonForRelease, e.ReleaseRequest.EvaluationDeadlineAt,
                e.ReleaseRequest.CreatedAt))
            .ToListAsync(ct);
    }

    public static EvaluationDto ToDto(ClusterEvaluation e, ReleaseRequest t, ICurrentUser u)
    {
        var active = TicketStateMachine.IsActive(t.Status) && (t.EvaluationDeadlineAt is null || t.EvaluationDeadlineAt > DateTime.UtcNow);
        var canUpdate = active && CanUpdate(u, e);
        var canRespond = active && u.IsInRole(RoleNames.Asdm) && u.ClusterId == e.ClusterId && e.Status == EvaluationStatus.Pending;
        return new EvaluationDto(e.EvaluationId, e.ReleaseRequestId, e.ClusterId, e.Cluster.Code, e.Cluster.Name, e.Status, e.Response,
            e.ResponseAt, e.ResponseSequence, e.AssignedAt, e.StartedAt, e.CompletedAt, e.InterviewDate, e.SelectionResult, e.RejectionReason,
            CanRespond: canRespond,
            CanStart: canUpdate && e.Status == EvaluationStatus.Interested,
            CanComplete: canUpdate && e.Status == EvaluationStatus.WaitingForResults,
            CanAssign: false);
    }

    private static bool CanUpdate(ICurrentUser u, ClusterEvaluation e)
        => u.IsInRole(RoleNames.Admin) || (u.IsInRole(RoleNames.Asdm) && u.ClusterId == e.ClusterId);

    private Task LockTicketAsync(int ticketId, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE release_requests SET UpdatedAt = UpdatedAt WHERE ReleaseRequestId = {ticketId}", ct);

    private async Task<(ReleaseRequest T, ClusterEvaluation E)> LoadLockedAsync(int ticketId, int evaluationId, CancellationToken ct)
    {
        await LockTicketAsync(ticketId, ct);
        var t = await wf.LoadAsync(ticketId, ct);
        var e = t.Evaluations.FirstOrDefault(x => x.EvaluationId == evaluationId)
            ?? throw new NotFoundException($"Allocation ticket {evaluationId} was not found for resource request {ticketId}.");
        if (!TicketStateMachine.IsActive(t.Status)) throw new BusinessRuleException($"Resource request {t.TicketNumber} is {t.Status}; no allocation actions are possible.");
        if (t.EvaluationDeadlineAt is { } deadline && Now >= deadline)
            throw new BusinessRuleException($"The original SLA for resource request {t.TicketNumber} has expired.");
        return (t, e);
    }

    public async Task<EvaluationDto> RespondAsync(int ticketId, int evaluationId, ClusterResponse response, CancellationToken ct)
    {
        user.Require(RoleNames.Asdm);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (t, e) = await LoadLockedAsync(ticketId, evaluationId, ct);
        if (user.ClusterId != e.ClusterId) throw new ForbiddenException("You can only express interest for your assigned cluster.");
        if (e.Status != EvaluationStatus.Pending) throw new ConflictException($"{e.Cluster.Code} has already responded.");

        e.Response = response;
        e.ResponseAt = Now;
        e.ResponseBy = user.UserId;
        if (response == ClusterResponse.YesInterested)
        {
            e.ResponseSequence = (t.Evaluations.Max(x => x.ResponseSequence) ?? 0) + 1;
            e.Status = EvaluationStatus.Interested;
            e.OwnerUserId = user.UserId;
        }
        else e.Status = EvaluationStatus.NotInterested;

        wf.AddEvent(t, "ClusterResponse", response == ClusterResponse.YesInterested
            ? $"{e.Cluster.Code} expressed interest (sequence {e.ResponseSequence})" : $"{e.Cluster.Code} declined the resource");
        audit.Record("ClusterEvaluation", e.EvaluationId, "ClusterResponded", null,
            new { Cluster = e.Cluster.Code, response, e.ResponseSequence }, t.ReleaseRequestId);

        await notify.EnqueueAsync(response == ClusterResponse.YesInterested ? NotificationType.ClusterInterested : NotificationType.ClusterNotInterested,
            t, [await notify.UserEmailAsync(t.CreatedBy)], new() { ["Cluster"] = e.Cluster.Code });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(e, t, user);
    }

    public async Task<EvaluationDto> UpdateAllocationStatusAsync(int ticketId, int evaluationId, UpdateAllocationStatusDto dto, CancellationToken ct)
    {
        DtoValidator.EnsureValid(dto);
        user.Require(RoleNames.Asdm, RoleNames.Admin);
        if (dto.Status == AllocationStatus.Rejected && string.IsNullOrWhiteSpace(dto.RejectionReason))
            throw new RequestValidationException("rejectionReason", "A rejection reason is required.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (t, e) = await LoadLockedAsync(ticketId, evaluationId, ct);
        if (!CanUpdate(user, e)) throw new ForbiddenException("You can only update allocation tickets for your cluster.");
        if (e.Response != ClusterResponse.YesInterested ||
            e.Status is EvaluationStatus.Pending or EvaluationStatus.NotInterested or EvaluationStatus.NotSelected or EvaluationStatus.Rejected or EvaluationStatus.Closed)
            throw new BusinessRuleException("This cluster does not have an active allocation ticket for the resource.");

        var target = dto.Status!.Value switch
        {
            AllocationStatus.InterviewScheduled => EvaluationStatus.InterviewScheduled,
            AllocationStatus.WaitingForResults => EvaluationStatus.WaitingForResults,
            AllocationStatus.Selected => EvaluationStatus.Selected,
            AllocationStatus.Rejected => EvaluationStatus.Rejected,
            _ => throw new RequestValidationException("status", "Unsupported allocation status.")
        };
        if (!CanTransition(e.Status, target))
            throw new BusinessRuleException($"Allocation status cannot move from {e.Status} to {target}.");

        var oldStatus = e.Status;
        var now = Now;
        e.Status = target;
        e.StartedAt ??= target == EvaluationStatus.InterviewScheduled ? now : null;
        e.InterviewDate = dto.InterviewDate ?? e.InterviewDate;
        e.CompletedAt = target is EvaluationStatus.Selected or EvaluationStatus.Rejected ? now : null;
        e.SelectionResult = target switch
        {
            EvaluationStatus.Selected => SelectionResult.Selected,
            EvaluationStatus.Rejected => SelectionResult.NotSelected,
            _ => null
        };
        e.RejectionReason = target == EvaluationStatus.Rejected ? dto.RejectionReason!.Trim() : null;
        t.UpdatedAt = now;

        wf.AddEvent(t, "AllocationStatus",
            $"{e.Cluster.Code} allocation status changed from {oldStatus} to {target}", dto.Comments ?? e.RejectionReason);
        audit.Record("ClusterEvaluation", e.EvaluationId, "AllocationStatusChanged",
            new { Status = oldStatus }, new { Status = target, dto.RejectionReason, e.InterviewDate, Cluster = e.Cluster.Code }, t.ReleaseRequestId);

        if (target == EvaluationStatus.Selected)
        {
            if (t.Evaluations.Any(other => other.EvaluationId != e.EvaluationId && other.Status == EvaluationStatus.Selected))
                throw new ConflictException("Another cluster has already selected this resource.");

            t.CurrentOwnerClusterId = e.ClusterId;
            t.CurrentOwnerUserId = e.OwnerUserId;
            t.AssignedToHead = false;
            foreach (var competitor in t.Evaluations.Where(other => other.EvaluationId != e.EvaluationId && IsAllocationActive(other.Status)))
            {
                var prior = competitor.Status;
                competitor.Status = EvaluationStatus.Closed;
                competitor.CompletedAt = now;
                audit.Record("ClusterEvaluation", competitor.EvaluationId, "ClosedByCompetingSelection",
                    new { Status = prior }, new { Status = EvaluationStatus.Closed, SelectedCluster = e.Cluster.Code }, t.ReleaseRequestId);
                wf.AddEvent(t, "AllocationClosed", $"{competitor.Cluster.Code} allocation closed because {e.Cluster.Code} selected the resource.");
            }

            var recipients = new[] { e.Cluster.NotificationEmail, await notify.UserEmailAsync(t.CreatedBy) };
            await notify.EnqueueAsync(NotificationType.ResourceSelected, t, recipients, new() { ["Cluster"] = e.Cluster.Code });
        }
        else if (target == EvaluationStatus.Rejected)
        {
            await notify.EnqueueAsync(NotificationType.ResourceNotSelected, t,
                [e.Cluster.NotificationEmail, await notify.UserEmailAsync(t.CreatedBy)],
                new() { ["Cluster"] = e.Cluster.Code, ["Reason"] = e.RejectionReason });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(e, t, user);
    }

    private static bool CanTransition(EvaluationStatus current, EvaluationStatus target)
    {
        if (target == EvaluationStatus.Rejected)
            return IsAllocationActive(current);
        return (current, target) switch
        {
            (EvaluationStatus.Interested or EvaluationStatus.Assigned, EvaluationStatus.InterviewScheduled) => true,
            (EvaluationStatus.InProgress or EvaluationStatus.InterviewScheduled, EvaluationStatus.WaitingForResults) => true,
            (EvaluationStatus.WaitingForResults, EvaluationStatus.Selected) => true,
            _ => false
        };
    }

    private static bool IsAllocationActive(EvaluationStatus status)
        => status is EvaluationStatus.Interested or EvaluationStatus.Assigned or EvaluationStatus.InProgress
            or EvaluationStatus.InterviewScheduled or EvaluationStatus.WaitingForResults or EvaluationStatus.Pending;

    public Task<EvaluationDto> StartAsync(int ticketId, int evaluationId, CancellationToken ct)
        => UpdateAllocationStatusAsync(ticketId, evaluationId,
            new UpdateAllocationStatusDto { Status = AllocationStatus.InterviewScheduled }, ct);

    public Task<EvaluationDto> CompleteAsync(int ticketId, int evaluationId, CompleteEvaluationDto dto, CancellationToken ct)
    {
        DtoValidator.EnsureValid(dto);
        return UpdateAllocationStatusAsync(ticketId, evaluationId, new UpdateAllocationStatusDto
        {
            Status = dto.SelectionResult == SelectionResult.Selected ? AllocationStatus.Selected : AllocationStatus.Rejected,
            RejectionReason = dto.RejectionReason,
            InterviewDate = dto.InterviewDate,
            Comments = dto.Comments
        }, ct);
    }
}
