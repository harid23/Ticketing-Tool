using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;

namespace ReleaseTicketing.Core.Services;

public record SlaRunResult(int RemindersQueued, int Breached, int AutoClosed);

/// <summary>Called by the Azure Function timer. All times are UTC. Rules come from sla_configuration, not code.</summary>
public class SlaService(AppDbContext db, AuditService audit, NotificationService notify, TicketWorkflow wf, TimeProvider clock, ILogger<SlaService> log)
{
    public async Task<SlaRunResult> ProcessAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var cfg = await db.SlaConfigurations.AsNoTracking().FirstAsync(c => c.TicketType == SlaConfiguration.ReleaseRequest && c.IsActive, ct);
        var ids = await db.ReleaseRequests
            .Where(t => TicketStateMachine.ActiveStatuses.Contains(t.Status) && t.EvaluationDeadlineAt != null &&
                !t.Evaluations.Any(e => e.Status == EvaluationStatus.Selected))
            .Select(t => t.ReleaseRequestId).ToListAsync(ct);

        int reminders = 0, breached = 0, closed = 0;
        foreach (var id in ids)
        {
            try
            {
                var t = await wf.LoadAsync(id, ct);
                if (t.Evaluations.Any(e => e.Status == EvaluationStatus.Selected)) continue;
                var deadline = t.EvaluationDeadlineAt!.Value;
                var elapsedDays = (now - (t.EvaluationStartAt ?? t.CreatedAt)).TotalDays;
                var exceptionApproved = await db.AuditLogs.AsNoTracking().AnyAsync(
                    audit => audit.ReleaseRequestId == id && audit.Action == "SlaExceptionApproved", ct);
                var ownerEmail = await notify.UserEmailAsync(t.CurrentOwnerUserId);
                var head = await notify.RoleEmailsAsync(Domain.RoleNames.Head);
                var extra = new Dictionary<string, string?> { ["Deadline"] = deadline.ToString("yyyy-MM-dd HH:mm 'UTC'"), ["Owner"] = t.CurrentOwnerCluster?.Code ?? (t.AssignedToHead ? "Head" : "unassigned") };

                if (cfg.RemindersEnabled && !exceptionApproved && t.SlaStatus == SlaStatus.WithinSla)
                {
                    var due = cfg.ReminderDayList.Where(d => elapsedDays >= d).DefaultIfEmpty(-1).Max();
                    if (due > 0)
                        reminders += await notify.EnqueueAsync(NotificationType.SlaWarning, t, head.Append(ownerEmail), extra, dedupeScope: $"D{due}");
                }

                if (t.SlaStatus == SlaStatus.WithinSla && now >= deadline)
                {
                    t.SlaStatus = SlaStatus.Breached; t.UpdatedAt = now;
                    wf.AddEvent(t, "SlaBreached", $"SLA breached: not resolved within {cfg.DurationDays} {cfg.CalendarType.ToLowerInvariant()} days");
                    audit.Record("ReleaseRequest", id, "SlaBreached", new { SlaStatus = SlaStatus.WithinSla }, new { SlaStatus = SlaStatus.Breached }, id);
                    await notify.EnqueueAsync(NotificationType.SlaBreach, t, head.Concat(cfg.EscalationEmailList).Append(ownerEmail), extra);
                    breached++;
                }

                if (cfg.AutoCloseEnabled && exceptionApproved && now >= deadline)
                {
                    await wf.CloseAsync(t, ClosureReasons.SlaExpired, "Automatically closed after the approved 14-day SLA extension expired without a selection.");
                    await notify.EnqueueAsync(NotificationType.AutoClosed, t, head.Append(ownerEmail).Append(await notify.UserEmailAsync(t.CreatedBy)), new() { ["Reason"] = ClosureReasons.SlaExpired });
                    closed++;
                }
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "SLA processing failed for ticket id {TicketId}", id);   // one bad ticket must not stop the rest
            }
            finally { db.ChangeTracker.Clear(); }
        }
        log.LogInformation("SLA run complete: {Reminders} reminders, {Breached} breached, {Closed} auto-closed", reminders, breached, closed);
        return new SlaRunResult(reminders, breached, closed);
    }
}
