using Microsoft.EntityFrameworkCore;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Exceptions;

namespace ReleaseTicketing.Core.Services;

/// <summary>
/// Operations shared by several services: change request status, add workflow history, and close a request.
/// Nothing here calls SaveChanges - the caller owns the transaction.
/// </summary>
public class TicketWorkflow(AppDbContext db, AuditService audit, NotificationService notify, ICurrentUser user, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Loads a ticket for modification, including evaluations and category.</summary>
    public async Task<ReleaseRequest> LoadAsync(int id, CancellationToken ct) =>
        await db.ReleaseRequests
            .Include(t => t.ReleaseCategory).Include(t => t.InitiatingCluster)
            .Include(t => t.CurrentOwnerCluster)
            .Include(t => t.Evaluations).ThenInclude(e => e.Cluster)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.ReleaseRequestId == id, ct)
        ?? throw new NotFoundException($"Release request {id} was not found.");

    public void AddEvent(ReleaseRequest t, string eventType, string description, string? comments = null)
        => db.TicketStatusHistory.Add(new TicketStatusHistory
        {
            ReleaseRequestId = t.ReleaseRequestId, EventType = eventType, OldStatus = t.Status, NewStatus = t.Status,
            ChangedBy = user.UserId, ChangedByName = user.DisplayName, ChangedAt = Now, Reason = description, Comments = comments
        });

    public void ChangeStatus(ReleaseRequest t, TicketStatus to, string reason, string? comments = null)
    {
        if (t.Status == to) return;
        if (!TicketStateMachine.CanTransition(t.Status, to))
            throw new BusinessRuleException($"Ticket {t.TicketNumber} cannot move from {t.Status} to {to}.");
        var from = t.Status;
        t.Status = to; t.UpdatedAt = Now;
        db.TicketStatusHistory.Add(new TicketStatusHistory
        {
            ReleaseRequestId = t.ReleaseRequestId, EventType = "StatusChange", OldStatus = from, NewStatus = to,
            ChangedBy = user.UserId, ChangedByName = user.DisplayName, ChangedAt = Now, Reason = reason, Comments = comments
        });
        audit.Record("ReleaseRequest", t.ReleaseRequestId, "StatusChanged", new { Status = from }, new { Status = to, Reason = reason }, t.ReleaseRequestId);
    }

    public async Task CloseAsync(ReleaseRequest t, string reason, string? comments)
    {
        t.ClosedAt = Now; t.ClosedBy = user.UserId; t.ClosedByName = user.IsSystem ? "SYSTEM" : user.DisplayName; t.ClosureReason = reason;
        ChangeStatus(t, TicketStatus.Closed, reason, comments);
        audit.Record("ReleaseRequest", t.ReleaseRequestId, "Closed", null, new { Reason = reason, By = t.ClosedByName }, t.ReleaseRequestId);

        if (t.ReleaseCategory?.RequiresCompetencyNotification == true)
        {
            await notify.EnqueueAsync(NotificationType.CompetencyNotification, t, await notify.RoleEmailsAsync(RoleNames.CompetencyPmo));
            AddEvent(t, "CompetencyNotified", "Competency PMO notification queued");
        }
    }
}
