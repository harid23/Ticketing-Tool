using System.ComponentModel.DataAnnotations;
using ReleaseTicketing.Core.Domain;

namespace ReleaseTicketing.Core.Dtos;

public class CreateReleaseRequestDto : IValidatableObject
{
    [Required, StringLength(20)] public string EmployeeId { get; set; } = "";
    [Required, StringLength(200)] public string EmployeeName { get; set; } = "";
    [Required] public int? ClusterId { get; set; }
    [Required] public int? ReleaseCategoryId { get; set; }
    [Required] public DateOnly? ReleaseDate { get; set; }
    [Required, StringLength(1000)] public string ReasonForRelease { get; set; } = "";
    public bool RecommendedForNewAssignment { get; set; }
    public bool EmployeeAware { get; set; }
    [Required, StringLength(2000)] public string EmployeeSkills { get; set; } = "";
    [Required, StringLength(2000)] public string FeedbackProvided { get; set; } = "";
    [Required, StringLength(4000)] public string DetailedFeedback { get; set; } = "";
    [StringLength(2000)] public string? CommitmentMade { get; set; }
    public bool FactoredInForecast { get; set; }
    [Required, StringLength(50)] public string Grade { get; set; } = "";
    [Required, StringLength(100)] public string Location { get; set; } = "";
    [Required, StringLength(200)] public string CurrentSupervisor { get; set; } = "";
    [Required, StringLength(50)] public string CurrentProjectId { get; set; } = "";
    [Required, StringLength(200)] public string CurrentProjectName { get; set; } = "";
    [StringLength(2000)] public string? Remarks { get; set; }
    [Required, StringLength(100)] public string ReleaseLocation { get; set; } = "";
    [Required, StringLength(50)] public string Psl { get; set; } = "";
    [Required, StringLength(50)] public string Band { get; set; } = "";
    public bool BackfillRequired { get; set; }

    public const int MinFeedbackLength = 100;
    public IEnumerable<ValidationResult> Validate(ValidationContext ctx)
    {
        if ((DetailedFeedback ?? "").Trim().Length < MinFeedbackLength)
            yield return new ValidationResult($"Detailed feedback must be at least {MinFeedbackLength} characters.", [nameof(DetailedFeedback)]);
    }
}

public class UpdateReleaseRequestDto : IValidatableObject
{
    [Required] public DateOnly? ReleaseDate { get; set; }
    [Required, StringLength(1000)] public string ReasonForRelease { get; set; } = "";
    public bool RecommendedForNewAssignment { get; set; }
    public bool EmployeeAware { get; set; }
    [Required, StringLength(2000)] public string EmployeeSkills { get; set; } = "";
    [Required, StringLength(2000)] public string FeedbackProvided { get; set; } = "";
    [Required, StringLength(4000)] public string DetailedFeedback { get; set; } = "";
    [StringLength(2000)] public string? CommitmentMade { get; set; }
    public bool FactoredInForecast { get; set; }
    [StringLength(2000)] public string? Remarks { get; set; }
    [Required, StringLength(100)] public string ReleaseLocation { get; set; } = "";
    public bool BackfillRequired { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext ctx)
    {
        if ((DetailedFeedback ?? "").Trim().Length < CreateReleaseRequestDto.MinFeedbackLength)
            yield return new ValidationResult($"Detailed feedback must be at least {CreateReleaseRequestDto.MinFeedbackLength} characters.", [nameof(DetailedFeedback)]);
    }
}

public class RespondDto { [Required] public ClusterResponse? Response { get; set; } }
public class AssignDto { [StringLength(1000)] public string? Comments { get; set; } }
public class UpdateAllocationStatusDto
{
    [Required] public AllocationStatus? Status { get; set; }
    [StringLength(1000)] public string? RejectionReason { get; set; }
    public DateOnly? InterviewDate { get; set; }
    [StringLength(1000)] public string? Comments { get; set; }
}
public class CompleteEvaluationDto
{
    [Required] public SelectionResult? SelectionResult { get; set; }
    public DateOnly? InterviewDate { get; set; }
    [StringLength(1000)] public string? RejectionReason { get; set; }
    [StringLength(1000)] public string? Comments { get; set; }
}
public class CancelDto
{
    [Required, MinLength(10), StringLength(1000)] public string Reason { get; set; } = "";
    /// <summary>Day the internal opportunity was identified. The 2-working-day window counts from here.</summary>
    [Required] public DateOnly? OpportunityIdentifiedOn { get; set; }
}
public class ReopenDto
{
    [Required, MinLength(10), StringLength(1000)] public string Reason { get; set; } = "";
    public int? RequestingClusterId { get; set; }
}
public class CloseDto { [Required, MinLength(3), StringLength(1000)] public string Reason { get; set; } = ""; }

public class ReleaseRequestQuery
{
    public string? TicketNumber { get; set; }
    public string? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public int? ClusterId { get; set; }
    public string? Status { get; set; }          // OPEN, WIP ...
    public string? SlaStatus { get; set; }       // WITHIN_SLA | BREACHED
    public int? OwnerUserId { get; set; }
    public int? ReleaseCategoryId { get; set; }
    public DateOnly? ReleaseDateFrom { get; set; }
    public DateOnly? ReleaseDateTo { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public bool? Mine { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
}

public class AvailableResourcesQuery
{
    public string? Search { get; set; }
    public string? Grade { get; set; }
    public string? Location { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record EmployeeDto(string EmployeeId, string Name, string? Grade, string? Location, string? Supervisor,
    string? ProjectId, string? ProjectName, string? Psl, string? Band, string? Skills);
public record ClusterDto(int Id, string Code, string Name);
public record ReleaseCategoryDto(int Id, string Code, string Name, bool RequiresClusterCirculation, bool RequiresInterview);
public record MeDto(int? UserId, string DisplayName, string? Email, int? ClusterId, IReadOnlyCollection<string> Roles);

public record ReleaseRequestSummaryDto(int Id, string TicketNumber, string EmployeeId, string EmployeeName,
    string InitiatingCluster, string Category, TicketStatus Status, SlaStatus SlaStatus, string? OwnerCluster,
    bool AssignedToHead, DateOnly ReleaseDate, DateTime CreatedAt, DateTime? EvaluationDeadlineAt);

public class ReleaseRequestDetailDto
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = "";
    public TicketStatus Status { get; set; }
    public SlaStatus SlaStatus { get; set; }
    public EmployeeSource EmployeeSource { get; set; }
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Location { get; set; } = "";
    public string CurrentSupervisor { get; set; } = "";
    public string CurrentProjectId { get; set; } = "";
    public string CurrentProjectName { get; set; } = "";
    public string Psl { get; set; } = "";
    public string Band { get; set; } = "";
    public DateOnly ReleaseDate { get; set; }
    public string ReleaseLocation { get; set; } = "";
    public string ReasonForRelease { get; set; } = "";
    public string Category { get; set; } = "";
    public int ReleaseCategoryId { get; set; }
    public bool RecommendedForNewAssignment { get; set; }
    public bool EmployeeAware { get; set; }
    public string EmployeeSkills { get; set; } = "";
    public string FeedbackProvided { get; set; } = "";
    public string DetailedFeedback { get; set; } = "";
    public string? CommitmentMade { get; set; }
    public bool FactoredInForecast { get; set; }
    public bool BackfillRequired { get; set; }
    public string? Remarks { get; set; }
    public string InitiatingCluster { get; set; } = "";
    public string? OwnerCluster { get; set; }
    public string? OwnerName { get; set; }
    public bool AssignedToHead { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? EvaluationDeadlineAt { get; set; }
    public bool SlaExceptionApproved { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedByName { get; set; }
    public string? ClosureReason { get; set; }
    public string? CancellationReason { get; set; }
    public int? ReopenedFromTicketId { get; set; }
    public string? ReopenedFromTicketNumber { get; set; }
    public IReadOnlyList<EvaluationDto> Evaluations { get; set; } = [];
    /// <summary>What the CURRENT user may do. The UI shows buttons from this list; the API still re-checks every call.</summary>
    public IReadOnlyList<string> AllowedActions { get; set; } = [];
}

public record EvaluationDto(int EvaluationId, int ReleaseRequestId, int ClusterId, string ClusterCode, string ClusterName,
    EvaluationStatus Status, ClusterResponse? Response, DateTime? ResponseAt, int? ResponseSequence,
    DateTime? AssignedAt, DateTime? StartedAt, DateTime? CompletedAt, DateOnly? InterviewDate,
    SelectionResult? SelectionResult, string? RejectionReason,
    bool CanRespond, bool CanStart, bool CanComplete, bool CanAssign);

public record ClusterEvaluationInboxItemDto(
    int TicketId, int EvaluationId, string TicketNumber, string EmployeeId, string EmployeeName,
    string Grade, string Location, string CurrentProjectName, string EmployeeSkills,
    DateOnly ReleaseDate, string ReleaseLocation, string ReasonForRelease,
    DateTime? EvaluationDeadlineAt, DateTime CreatedAt);

public record AvailableResourceDto(
    int TicketId, string TicketNumber, string EmployeeId, string EmployeeName, string Grade, string Location,
    string CurrentProjectName, string EmployeeSkills, string InitiatingCluster, string Category,
    DateOnly ReleaseDate, DateTime CreatedAt, DateTime? EvaluationDeadlineAt,
    int? EvaluationId, EvaluationStatus? MyAllocationStatus, bool CanShowInterest);

public record AllocationTicketDto(
    int TicketId, int EvaluationId, string TicketNumber, string EmployeeId, string EmployeeName, string Grade,
    string Location, string CurrentProjectName, string EmployeeSkills, int ClusterId, string ClusterCode,
    DateTime? InterestAt, EvaluationStatus Status, TicketStatus TicketStatus, DateTime CreatedAt, DateTime? EvaluationDeadlineAt,
    string? RejectionReason, bool CanUpdateStatus);

public record HistoryDto(int HistoryId, string EventType, TicketStatus? OldStatus, TicketStatus NewStatus,
    string ChangedByName, DateTime ChangedAt, string? Reason, string? Comments);
public record AuditDto(long AuditId, string EntityType, string EntityId, string Action, string? OldValue, string? NewValue,
    string PerformedByName, DateTime PerformedAt, string? IpAddress);
public record ActivityNotificationDto(long Id, int TicketId, string TicketNumber, string EmployeeName,
    string? ClusterCode, string Message, DateTime OccurredAt);
public record AttachmentDto(int AttachmentId, string Kind, string FileName, string FileType, long FileSize, DateTime UploadedAt, int UploadedBy);

public record DashboardDto(int TotalOpen, int TotalWip, int TotalClosed, int TotalCancelled, int TotalReopened,
    int SlaAtRisk, int SlaBreached, int AwaitingClusterResponse, int AwaitingInterview, int AwaitingFinalDecision,
    int AllocatedCount, int ReleasedCount);
