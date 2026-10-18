namespace ReleaseTicketing.Core.Domain;

public enum TicketStatus { Open, Wip, Closed, Cancelled, Reopened }
public enum SlaStatus { WithinSla, Breached }
public enum EvaluationStatus
{
    Pending, NotInterested, Interested, Assigned, InProgress, InterviewScheduled, WaitingForResults,
    Selected, NotSelected, Rejected, Closed
}
public enum ClusterResponse { YesInterested, NoNotInterested }
public enum SelectionResult { Selected, NotSelected }
public enum AllocationStatus { InterviewScheduled, WaitingForResults, Selected, Rejected }
public enum EmployeeSource { Master, Manual }
public enum NotificationStatus { Pending, Sent, Failed }

public enum NotificationType
{
    TicketCreated, ClusterEvaluationRequest, ClusterInterested, ClusterNotInterested,
    AssignmentCreated, InterviewRequired, ResourceSelected, ResourceNotSelected, Reassignment,
    SlaWarning, SlaBreach, AutoClosed, Cancelled, Reopened, CompetencyNotification
}

/// <summary>Role names. These must match the App Role "value" you create in Microsoft Entra ID.</summary>
public static class RoleNames
{
    public const string Pm = "PM";
    public const string Asdm = "ASDM";
    public const string Sdm = Asdm;
    public const string Pmo = "Admin";
    public const string ClusterManager = Asdm;
    public const string Head = "Admin";
    public const string CompetencyPmo = "Admin";
    public const string Admin = "Admin";
    public static readonly string[] All = [Pm, Asdm, Admin];

    public static string? Normalize(string role) => role switch
    {
        Pm => Pm,
        Asdm or "SDM" or "Manager" or "ClusterManager" => Asdm,
        Admin or "Head" or "PMO" or "CompetencyPmo" => Admin,
        _ => null
    };
}

public static class ClosureReasons
{
    public const string SelectedByCluster = "SELECTED_BY_CLUSTER";
    public const string SlaExpired = "SLA_EXPIRED_NO_SELECTION";
    public const string ClosedByHead = "CLOSED_BY_HEAD";
}

public static class AttachmentKinds
{
    public const string Softlock = "SOFTLOCK";
    public const string ReopenEmail = "REOPEN_EMAIL";
    public const string Other = "OTHER";
    public static readonly string[] All = [Softlock, ReopenEmail, Other];
}
