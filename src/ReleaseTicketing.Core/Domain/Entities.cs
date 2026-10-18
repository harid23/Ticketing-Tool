using System.ComponentModel.DataAnnotations;

namespace ReleaseTicketing.Core.Domain;

public class Role { public int Id { get; set; } public string Name { get; set; } = ""; public string Description { get; set; } = ""; }

public class User
{
    public int Id { get; set; }
    public string EntraObjectId { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int? ClusterId { get; set; }          // set for cluster managers; admin-maintained
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public List<UserRole> UserRoles { get; set; } = [];
}

public class UserRole { public int UserId { get; set; } public User User { get; set; } = null!; public int RoleId { get; set; } public Role Role { get; set; } = null!; }

public class Cluster
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int? ManagerUserId { get; set; }
    public string? NotificationEmail { get; set; }   // distribution list
}

public class ReleaseCategory
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool RequiresClusterCirculation { get; set; }
    public bool RequiresInterview { get; set; }
    public bool RequiresCompetencyNotification { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Employee master - filled by an import/sync job, never queried from Excel at ticket time.</summary>
public class Employee
{
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Grade { get; set; }
    public string? Location { get; set; }
    public string? Supervisor { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? Psl { get; set; }
    public string? Band { get; set; }
    public string? Skills { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastSyncedAt { get; set; }
}

public class ReleaseRequest
{
    public int ReleaseRequestId { get; set; }
    public string TicketNumber { get; set; } = "";

    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public EmployeeSource EmployeeSource { get; set; }

    public DateOnly ReleaseDate { get; set; }
    public string ReasonForRelease { get; set; } = "";
    public int ReleaseCategoryId { get; set; }
    public ReleaseCategory ReleaseCategory { get; set; } = null!;

    public bool RecommendedForNewAssignment { get; set; }
    public bool EmployeeAware { get; set; }
    public string EmployeeSkills { get; set; } = "";
    public string FeedbackProvided { get; set; } = "";
    public string DetailedFeedback { get; set; } = "";
    public string? CommitmentMade { get; set; }
    public bool FactoredInForecast { get; set; }

    public string Grade { get; set; } = "";
    public string Location { get; set; } = "";
    public string CurrentSupervisor { get; set; } = "";
    public string CurrentProjectId { get; set; } = "";
    public string CurrentProjectName { get; set; } = "";
    public string? Remarks { get; set; }
    public string ReleaseLocation { get; set; } = "";
    public string Psl { get; set; } = "";
    public string Band { get; set; } = "";
    public bool BackfillRequired { get; set; }

    public int InitiatingClusterId { get; set; }
    public Cluster InitiatingCluster { get; set; } = null!;

    public TicketStatus Status { get; set; }
    public SlaStatus SlaStatus { get; set; }

    public int? CurrentOwnerUserId { get; set; }
    public User? CurrentOwnerUser { get; set; }
    public int? CurrentOwnerClusterId { get; set; }
    public Cluster? CurrentOwnerCluster { get; set; }
    /// <summary>True when the ticket is with the Head role (no cluster owns it).</summary>
    public bool AssignedToHead { get; set; }

    public int CreatedBy { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public DateTime? EvaluationStartAt { get; set; }
    public DateTime? EvaluationDeadlineAt { get; set; }

    public DateTime? ClosedAt { get; set; }
    public int? ClosedBy { get; set; }
    public string? ClosedByName { get; set; }
    public string? ClosureReason { get; set; }

    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }

    public int? ReopenedFromTicketId { get; set; }
    public ReleaseRequest? ReopenedFrom { get; set; }

    public List<ClusterEvaluation> Evaluations { get; set; } = [];
    public List<TicketStatusHistory> History { get; set; } = [];
    public List<TicketAttachment> Attachments { get; set; } = [];
}

public class ClusterEvaluation
{
    [Key]
    public int EvaluationId { get; set; }
    public int ReleaseRequestId { get; set; }
    public ReleaseRequest ReleaseRequest { get; set; } = null!;
    public int ClusterId { get; set; }
    public Cluster Cluster { get; set; } = null!;

    public EvaluationStatus Status { get; set; }
    public ClusterResponse? Response { get; set; }
    public DateTime? ResponseAt { get; set; }
    public int? ResponseBy { get; set; }
    /// <summary>Assigned by the backend inside a locked transaction; 1 = first YES.</summary>
    public int? ResponseSequence { get; set; }

    public DateTime? AssignedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateOnly? InterviewDate { get; set; }
    public SelectionResult? SelectionResult { get; set; }
    public string? RejectionReason { get; set; }
    public int? OwnerUserId { get; set; }
}

/// <summary>Timeline. EventType = "StatusChange" for real status changes; other values are workflow events.</summary>
public class TicketStatusHistory
{
    public int HistoryId { get; set; }
    public int ReleaseRequestId { get; set; }
    public ReleaseRequest ReleaseRequest { get; set; } = null!;
    public string EventType { get; set; } = "StatusChange";
    public TicketStatus? OldStatus { get; set; }
    public TicketStatus NewStatus { get; set; }
    public int? ChangedBy { get; set; }
    public string ChangedByName { get; set; } = "";
    public DateTime ChangedAt { get; set; }
    public string? Reason { get; set; }
    public string? Comments { get; set; }
}

public class AuditLog
{
    public long AuditId { get; set; }
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public int? ReleaseRequestId { get; set; }     // lets us fetch everything about one ticket; no FK on purpose
    public string Action { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public int? PerformedBy { get; set; }
    public string PerformedByName { get; set; } = "";
    public DateTime PerformedAt { get; set; }
    public string? IpAddress { get; set; }
}

public class TicketAttachment
{
    [Key]
    public int AttachmentId { get; set; }
    public int ReleaseRequestId { get; set; }
    public ReleaseRequest ReleaseRequest { get; set; } = null!;
    public string Kind { get; set; } = AttachmentKinds.Other;
    public string FileName { get; set; } = "";
    public string BlobPath { get; set; } = "";
    public string FileType { get; set; } = "";
    public long FileSize { get; set; }
    public int UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class NotificationQueueItem
{
    public int NotificationId { get; set; }
    public int? ReleaseRequestId { get; set; }
    public ReleaseRequest? ReleaseRequest { get; set; }
    public string Recipient { get; set; } = "";
    public NotificationType Type { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public NotificationStatus Status { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? LastError { get; set; }
    public string? DedupeKey { get; set; }
}

public class NotificationHistory
{
    public int Id { get; set; }
    public int NotificationId { get; set; }
    public NotificationQueueItem Notification { get; set; } = null!;
    public DateTime AttemptedAt { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class SlaConfiguration
{
    public const string ReleaseRequest = "RELEASE_REQUEST";
    public int Id { get; set; }
    public string TicketType { get; set; } = ReleaseRequest;
    public int DurationDays { get; set; } = 14;
    public string CalendarType { get; set; } = "CALENDAR";      // CALENDAR | WORKING
    public bool AutoCloseEnabled { get; set; } = true;
    public int AutoCloseGraceHours { get; set; } = 24;           // cutoff after the deadline - see docs/open-questions.md
    public bool RemindersEnabled { get; set; } = true;
    public string ReminderDays { get; set; } = "10,13";
    public int CancellationWindowWorkingDays { get; set; } = 2;
    public int AtRiskDays { get; set; } = 3;
    public string EscalationEmails { get; set; } = "";           // e.g. the "Senthil" mailbox; semicolon separated
    public bool IsActive { get; set; } = true;

    public IEnumerable<int> ReminderDayList => ReminderDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var d) ? d : -1).Where(d => d > 0);
    public IEnumerable<string> EscalationEmailList => EscalationEmails.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class WorkingDay { public DateOnly Date { get; set; } public bool IsWorkingDay { get; set; } public string? HolidayName { get; set; } }

public class TicketCounter { public int Year { get; set; } public int LastValue { get; set; } }

/// <summary>The only place that knows which status changes are legal.</summary>
public static class TicketStateMachine
{
    public static readonly TicketStatus[] ActiveStatuses = [TicketStatus.Open, TicketStatus.Wip, TicketStatus.Reopened];

    private static readonly Dictionary<TicketStatus, TicketStatus[]> Allowed = new()
    {
        [TicketStatus.Open] = [TicketStatus.Wip, TicketStatus.Closed, TicketStatus.Cancelled],
        [TicketStatus.Reopened] = [TicketStatus.Wip, TicketStatus.Closed, TicketStatus.Cancelled],
        [TicketStatus.Wip] = [TicketStatus.Closed, TicketStatus.Cancelled],
        [TicketStatus.Closed] = [],
        [TicketStatus.Cancelled] = [],
    };

    public static bool IsActive(TicketStatus s) => ActiveStatuses.Contains(s);
    public static bool CanTransition(TicketStatus from, TicketStatus to) => Allowed[from].Contains(to);
}
