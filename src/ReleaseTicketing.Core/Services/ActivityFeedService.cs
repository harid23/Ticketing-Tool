using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReleaseTicketing.Core.Abstractions;
using ReleaseTicketing.Core.Data;
using ReleaseTicketing.Core.Domain;
using ReleaseTicketing.Core.Dtos;
using ReleaseTicketing.Core.Exceptions;

namespace ReleaseTicketing.Core.Services;

public class ActivityFeedService(AppDbContext db, ICurrentUser user, TimeProvider clock)
{
    private static readonly string[] ActivityActions =
    [
        "ClusterResponded", "AllocationStatusChanged", "ClosedByCompetingSelection",
        "SlaBreached", "SlaExceptionApproved", "Closed", "StatusChanged"
    ];

    public async Task<IReadOnlyList<ActivityNotificationDto>> GetRecentAsync(CancellationToken ct)
    {
        user.Require(RoleNames.Pm, RoleNames.Asdm, RoleNames.Admin);
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-7);
        var query = from audit in db.AuditLogs.AsNoTracking()
                    join ticket in db.ReleaseRequests.AsNoTracking() on audit.ReleaseRequestId equals ticket.ReleaseRequestId
                    where audit.PerformedAt >= since && ActivityActions.Contains(audit.Action) &&
                        (audit.PerformedBy == null || audit.PerformedBy != user.UserId)
                    select new { Audit = audit, Ticket = ticket };

        if (user.IsInRole(RoleNames.Admin))
        {
            // Admin can see activity on every request.
        }
        else if (user.IsInRole(RoleNames.Pm))
        {
            var userId = user.UserId;
            query = query.Where(item => userId != null && item.Ticket.CreatedBy == userId);
        }
        else
        {
            var clusterId = user.ClusterId
                ?? throw new ForbiddenException("Your account is not assigned to a cluster.");
            query = query.Where(item => item.Ticket.Evaluations.Any(evaluation => evaluation.ClusterId == clusterId));
        }

        var entries = await query.OrderByDescending(item => item.Audit.PerformedAt)
            .ThenByDescending(item => item.Audit.AuditId)
            .Take(50)
            .ToListAsync(ct);

        return entries.Select(item =>
        {
            var cluster = ReadValue(item.Audit.NewValue, "cluster") ??
                ReadValue(item.Audit.NewValue, "selectedCluster");
            var message = item.Audit.Action switch
            {
                "ClusterResponded" => $"{cluster ?? "A cluster"} {ReadValue(item.Audit.NewValue, "response") switch
                {
                    "YesInterested" => "showed interest",
                    "NoNotInterested" => "declined",
                    _ => "responded"
                }}.",
                "AllocationStatusChanged" => $"{cluster ?? "A cluster"} updated allocation to {FormatStatus(ReadValue(item.Audit.NewValue, "status"))}.",
                "ClosedByCompetingSelection" => $"{cluster ?? "A cluster"}'s allocation was closed after another cluster selected the employee.",
                "SlaBreached" => "The 14-day SLA has expired.",
                "SlaExceptionApproved" => "Admin approved a 14-day SLA extension for allocation results.",
                "Closed" => $"The request was closed. {ReadValue(item.Audit.NewValue, "reason") ?? ""}".Trim(),
                "StatusChanged" => $"Request status changed to {FormatStatus(ReadValue(item.Audit.NewValue, "status"))}.",
                _ => "The request was updated."
            };

            return new ActivityNotificationDto(item.Audit.AuditId, item.Ticket.ReleaseRequestId,
                item.Ticket.TicketNumber, item.Ticket.EmployeeName, cluster, message, item.Audit.PerformedAt);
        }).ToList();
    }

    private static string? ReadValue(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var item in document.RootElement.EnumerateObject())
                if (string.Equals(item.Name, property, StringComparison.OrdinalIgnoreCase))
                    return item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : item.Value.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
        return null;
    }

    private static string FormatStatus(string? status)
        => string.IsNullOrWhiteSpace(status) ? "updated" : string.Concat(status.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));
}
