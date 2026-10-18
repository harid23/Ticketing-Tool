using Microsoft.EntityFrameworkCore;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Dtos;

namespace ReleaseTicketing.Api.Controllers;

public record LocalDemoScenarioDto(string Scenario, int TicketId, string TicketNumber,
    TicketStatus Status, SlaStatus SlaStatus, DateTime? Deadline);

public class LocalDemoScenarioService(AppDbContext db, ICurrentUser user, TimeProvider clock)
{
    public async Task<IReadOnlyList<LocalDemoScenarioDto>> SeedAsync(CancellationToken ct)
    {
        user.Require(RoleNames.Pm, RoleNames.Asdm, RoleNames.Admin);
        var userId = await EnsureLocalPmAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var year = now.Year;
        var definitions = new[]
        {
            new ScenarioDefinition("Fresh request · 14 days remaining", "FRESH", 0, TicketStatus.Open, SlaStatus.WithinSla, null),
            new ScenarioDefinition("Near deadline · 1 day remaining", "D13", -13, TicketStatus.Open, SlaStatus.WithinSla, EvaluationStatus.InterviewScheduled),
            new ScenarioDefinition("SLA breached · manual decision required", "BREACH", -14, TicketStatus.Open, SlaStatus.Breached, EvaluationStatus.WaitingForResults),
            new ScenarioDefinition("Auto-closed · approved extension expired", "CLOSED", -29, TicketStatus.Closed, SlaStatus.Breached, null, true)
        };
        var created = new List<(ScenarioDefinition Definition, ReleaseRequest Ticket)>();

        foreach (var definition in definitions)
        {
            var ticketNumber = $"DEMO-{year}-{definition.Code}";
            var ticket = await db.ReleaseRequests
                .Include(request => request.Evaluations)
                .FirstOrDefaultAsync(request => request.TicketNumber == ticketNumber, ct);
            if (ticket is not null)
            {
                if (definition.ExtensionApproved &&
                    !await db.AuditLogs.AnyAsync(audit => audit.ReleaseRequestId == ticket.ReleaseRequestId &&
                        audit.Action == "SlaExceptionApproved", ct))
                {
                    var legacyCreatedAt = now.AddDays(definition.CreatedDaysAgo);
                    var legacyInitialDeadline = legacyCreatedAt.AddDays(14);
                    var extendedDeadline = legacyInitialDeadline.AddDays(14);
                    ticket.CreatedAt = legacyCreatedAt;
                    ticket.EvaluationStartAt = legacyCreatedAt;
                    ticket.EvaluationDeadlineAt = extendedDeadline;
                    ticket.ReleaseDate = DateOnly.FromDateTime(extendedDeadline);
                    ticket.UpdatedAt = now;
                    ticket.ClosedAt = extendedDeadline;
                    ticket.Status = TicketStatus.Closed;
                    ticket.SlaStatus = SlaStatus.Breached;
                    ticket.ClosedByName = "SYSTEM";
                    ticket.ClosureReason = ClosureReasons.SlaExpired;
                    db.TicketStatusHistory.Add(new TicketStatusHistory
                    {
                        ReleaseRequestId = ticket.ReleaseRequestId,
                        EventType = "DemoScenarioRefreshed",
                        OldStatus = TicketStatus.Closed,
                        NewStatus = TicketStatus.Closed,
                        ChangedByName = "SYSTEM",
                        ChangedAt = now,
                        Reason = "Synthetic SLA sample refreshed to represent closure after an approved 14-day extension."
                    });
                    db.AuditLogs.Add(SlaExtensionAudit(ticket, legacyInitialDeadline, extendedDeadline));
                    if (!await db.AuditLogs.AnyAsync(audit => audit.ReleaseRequestId == ticket.ReleaseRequestId &&
                        audit.Action == "SlaBreached" && audit.PerformedAt == legacyInitialDeadline, ct))
                        db.AuditLogs.Add(SlaBreachAudit(ticket, legacyInitialDeadline));
                }
                created.Add((definition, ticket));
                continue;
            }

            var createdAt = now.AddDays(definition.CreatedDaysAgo);
            var initialDeadline = createdAt.AddDays(14);
            var deadline = definition.ExtensionApproved ? initialDeadline.AddDays(14) : initialDeadline;
            ticket = new ReleaseRequest
            {
                TicketNumber = ticketNumber,
                EmployeeId = $"DEMO-{year}-{definition.Code}",
                EmployeeName = $"Demo Employee {definition.Code}",
                EmployeeSource = EmployeeSource.Manual,
                ReleaseDate = DateOnly.FromDateTime(deadline),
                ReasonForRelease = "Synthetic local-only scenario data for exercising release and allocation workflows.",
                ReleaseCategoryId = 1,
                RecommendedForNewAssignment = true,
                EmployeeAware = true,
                EmployeeSkills = "C#, SQL, workflow testing",
                FeedbackProvided = "Demo feedback supplied for local scenario testing.",
                DetailedFeedback = "Synthetic test data. Use this request to validate allocation status changes, the 14-day SLA, notifications, and ticket closure behavior.",
                CommitmentMade = "No",
                FactoredInForecast = true,
                Grade = "G5",
                Location = "Chennai",
                CurrentSupervisor = "Demo Supervisor",
                CurrentProjectId = $"DEMO-{definition.Code}",
                CurrentProjectName = $"Demo SLA Scenario - {definition.Code}",
                Remarks = "DEMO DATA — safe to distinguish from business requests.",
                ReleaseLocation = "Chennai",
                Psl = "DEMO",
                Band = "B2",
                BackfillRequired = false,
                InitiatingClusterId = 1,
                Status = definition.Status,
                SlaStatus = definition.SlaStatus,
                CreatedBy = userId,
                CreatedByName = "Local PM demo",
                CreatedAt = createdAt,
                UpdatedAt = now,
                EvaluationStartAt = createdAt,
                EvaluationDeadlineAt = deadline,
                ClosedAt = definition.Status == TicketStatus.Closed ? deadline : null,
                ClosedByName = definition.Status == TicketStatus.Closed ? "SYSTEM" : null,
                ClosureReason = definition.Status == TicketStatus.Closed ? ClosureReasons.SlaExpired : null,
                Evaluations = []
            };

            foreach (var clusterId in await db.Clusters.Where(cluster => cluster.IsActive).Select(cluster => cluster.Id).ToListAsync(ct))
            {
                var evaluatedStatus = clusterId switch
                {
                    2 when definition.AllocationStatus == EvaluationStatus.InterviewScheduled => EvaluationStatus.InterviewScheduled,
                    3 when definition.AllocationStatus == EvaluationStatus.InterviewScheduled => EvaluationStatus.WaitingForResults,
                    3 when definition.AllocationStatus == EvaluationStatus.WaitingForResults => EvaluationStatus.WaitingForResults,
                    _ => EvaluationStatus.Pending
                };
                var interested = evaluatedStatus != EvaluationStatus.Pending;
                ticket.Evaluations.Add(new ClusterEvaluation
                {
                    ClusterId = clusterId,
                    Status = evaluatedStatus,
                    Response = interested ? ClusterResponse.YesInterested : null,
                    ResponseAt = interested ? createdAt.AddDays(Math.Max(1, -definition.CreatedDaysAgo / 2)) : null,
                    ResponseSequence = interested ? (clusterId == 2 ? 1 : 2) : null,
                    StartedAt = evaluatedStatus is EvaluationStatus.InterviewScheduled or EvaluationStatus.WaitingForResults
                        ? createdAt.AddDays(Math.Max(1, -definition.CreatedDaysAgo / 3)) : null,
                    OwnerUserId = null
                });
            }

            ticket.History.Add(new TicketStatusHistory
            {
                EventType = "StatusChange",
                NewStatus = TicketStatus.Open,
                ChangedBy = userId,
                ChangedByName = "Local PM demo",
                ChangedAt = createdAt,
                Reason = "Demo release request created"
            });
            if (definition.Status == TicketStatus.Closed)
            {
                ticket.History.Add(new TicketStatusHistory
                {
                    EventType = "StatusChange",
                    OldStatus = TicketStatus.Open,
                    NewStatus = TicketStatus.Closed,
                    ChangedByName = "SYSTEM",
                    ChangedAt = deadline,
                    Reason = ClosureReasons.SlaExpired,
                    Comments = "Automatically closed after the 14-day SLA and 24-hour grace period."
                });
            }

            db.ReleaseRequests.Add(ticket);
            created.Add((definition, ticket));
        }

        await db.SaveChangesAsync(ct);

        foreach (var (definition, ticket) in created.Where(item => item.Definition.AllocationStatus is not null))
        {
            if (await db.AuditLogs.AnyAsync(audit => audit.ReleaseRequestId == ticket.ReleaseRequestId &&
                audit.Action == "AllocationStatusChanged", ct))
                continue;

            foreach (var evaluation in ticket.Evaluations.Where(item => item.Status is EvaluationStatus.InterviewScheduled or EvaluationStatus.WaitingForResults))
            {
                db.AuditLogs.Add(new AuditLog
                {
                    EntityType = "ClusterEvaluation",
                    EntityId = evaluation.EvaluationId.ToString(),
                    ReleaseRequestId = ticket.ReleaseRequestId,
                    Action = "AllocationStatusChanged",
                    OldValue = "{\"status\":\"Interested\"}",
                    NewValue = $"{{\"status\":\"{evaluation.Status}\",\"cluster\":\"EY{evaluation.ClusterId}\"}}",
                    PerformedByName = "Demo Scenario",
                    PerformedAt = evaluation.StartedAt ?? now
                });
            }
        }

        foreach (var (definition, ticket) in created.Where(item => item.Definition.SlaStatus == SlaStatus.Breached))
        {
            if (definition.ExtensionApproved &&
                !await db.AuditLogs.AnyAsync(audit => audit.ReleaseRequestId == ticket.ReleaseRequestId &&
                    audit.Action == "SlaExceptionApproved", ct))
                db.AuditLogs.Add(SlaExtensionAudit(ticket, ticket.EvaluationDeadlineAt!.Value.AddDays(-14),
                    ticket.EvaluationDeadlineAt.Value));
            if (!await db.AuditLogs.AnyAsync(audit => audit.ReleaseRequestId == ticket.ReleaseRequestId &&
                audit.Action == "SlaBreached" && audit.PerformedAt == ticket.EvaluationDeadlineAt, ct))
                db.AuditLogs.Add(SlaBreachAudit(ticket, ticket.EvaluationDeadlineAt!.Value));
            if (definition.Status == TicketStatus.Closed &&
                !await db.AuditLogs.AnyAsync(audit => audit.ReleaseRequestId == ticket.ReleaseRequestId &&
                    audit.Action == "Closed" && audit.PerformedAt == ticket.ClosedAt, ct))
                db.AuditLogs.Add(new AuditLog
                {
                    EntityType = "ReleaseRequest",
                    EntityId = ticket.ReleaseRequestId.ToString(),
                    ReleaseRequestId = ticket.ReleaseRequestId,
                    Action = "Closed",
                    NewValue = $"{{\"reason\":\"{ClosureReasons.SlaExpired}\",\"by\":\"SYSTEM\"}}",
                    PerformedByName = "SYSTEM",
                    PerformedAt = ticket.ClosedAt!.Value
                });
        }
        await db.SaveChangesAsync(ct);

        return created.Select(item => new LocalDemoScenarioDto(item.Definition.Name, item.Ticket.ReleaseRequestId,
            item.Ticket.TicketNumber, item.Ticket.Status, item.Ticket.SlaStatus, item.Ticket.EvaluationDeadlineAt)).ToList();
    }

    private async Task<int> EnsureLocalPmAsync(CancellationToken ct)
    {
        var pm = await db.Users.Include(candidate => candidate.UserRoles)
            .FirstOrDefaultAsync(candidate => candidate.EntraObjectId == "local-dev-pm-none", ct);
        if (pm is null)
        {
            pm = new User
            {
                EntraObjectId = "local-dev-pm-none",
                Email = "local-dev-pm-none@localhost",
                DisplayName = "Local PM demo",
                CreatedAt = clock.GetUtcNow().UtcDateTime
            };
            db.Users.Add(pm);
        }
        if (pm.UserRoles.All(membership => membership.RoleId != 1))
            pm.UserRoles.Add(new UserRole { User = pm, RoleId = 1 });
        await db.SaveChangesAsync(ct);
        return pm.Id;
    }

    private static AuditLog SlaExtensionAudit(ReleaseRequest ticket, DateTime originalDeadline, DateTime extendedDeadline)
        => new()
        {
            EntityType = "ReleaseRequest",
            EntityId = ticket.ReleaseRequestId.ToString(),
            ReleaseRequestId = ticket.ReleaseRequestId,
            Action = "SlaExceptionApproved",
            OldValue = $"{{\"deadline\":\"{originalDeadline:O}\"}}",
            NewValue = $"{{\"deadline\":\"{extendedDeadline:O}\",\"extensionDays\":14}}",
            PerformedByName = "Admin Demo",
            PerformedAt = originalDeadline
        };

    private static AuditLog SlaBreachAudit(ReleaseRequest ticket, DateTime breachedAt)
        => new()
        {
            EntityType = "ReleaseRequest",
            EntityId = ticket.ReleaseRequestId.ToString(),
            ReleaseRequestId = ticket.ReleaseRequestId,
            Action = "SlaBreached",
            OldValue = "{\"slaStatus\":\"WithinSla\"}",
            NewValue = "{\"slaStatus\":\"Breached\"}",
            PerformedByName = "SYSTEM",
            PerformedAt = breachedAt
        };

    private sealed record ScenarioDefinition(string Name, string Code, int CreatedDaysAgo,
        TicketStatus Status, SlaStatus SlaStatus, EvaluationStatus? AllocationStatus, bool ExtensionApproved = false);
}
