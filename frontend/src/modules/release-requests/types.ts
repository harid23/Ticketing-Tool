export type TicketStatus = 'OPEN' | 'WIP' | 'CLOSED' | 'CANCELLED' | 'REOPENED';
export type SlaStatus = 'WITHIN_SLA' | 'BREACHED';
export type EvaluationStatus = 'PENDING' | 'NOT_INTERESTED' | 'INTERESTED' | 'ASSIGNED' | 'IN_PROGRESS' |
  'INTERVIEW_SCHEDULED' | 'WAITING_FOR_RESULTS' | 'SELECTED' | 'NOT_SELECTED' | 'REJECTED' | 'CLOSED';
export type AllocationStatus = 'INTERVIEW_SCHEDULED' | 'WAITING_FOR_RESULTS' | 'SELECTED' | 'REJECTED';
export type ClusterResponse = 'YES_INTERESTED' | 'NO_NOT_INTERESTED';

export interface Me { userId: number | null; displayName: string; email: string | null; clusterId: number | null; roles: string[] }
export interface Cluster { id: number; code: string; name: string }
export interface ActivityNotification { id: number; ticketId: number; ticketNumber: string; employeeName: string; clusterCode: string | null; message: string; occurredAt: string }
export interface LocalDemoScenario { scenario: string; ticketId: number; ticketNumber: string; status: TicketStatus; slaStatus: SlaStatus; deadline: string | null }
export interface ReleaseCategory { id: number; code: string; name: string; requiresClusterCirculation: boolean; requiresInterview: boolean }
export interface Employee { employeeId: string; name: string; grade?: string; location?: string; supervisor?: string; projectId?: string; projectName?: string; psl?: string; band?: string; skills?: string }

export interface TicketSummary {
  id: number; ticketNumber: string; employeeId: string; employeeName: string; initiatingCluster: string; category: string;
  status: TicketStatus; slaStatus: SlaStatus; ownerCluster: string | null; assignedToHead: boolean;
  releaseDate: string; createdAt: string; evaluationDeadlineAt: string | null;
}
export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number }

export interface Evaluation {
  evaluationId: number; releaseRequestId: number; clusterId: number; clusterCode: string; clusterName: string;
  status: EvaluationStatus; response: ClusterResponse | null; responseAt: string | null; responseSequence: number | null;
  assignedAt: string | null; startedAt: string | null; completedAt: string | null; interviewDate: string | null;
  selectionResult: 'SELECTED' | 'NOT_SELECTED' | null; rejectionReason: string | null;
  canRespond: boolean; canStart: boolean; canComplete: boolean; canAssign: boolean;
}

export interface ClusterEvaluationInboxItem {
  ticketId: number; evaluationId: number; ticketNumber: string; employeeId: string; employeeName: string;
  grade: string; location: string; currentProjectName: string; employeeSkills: string;
  releaseDate: string; releaseLocation: string; reasonForRelease: string;
  evaluationDeadlineAt: string | null; createdAt: string;
}

export interface AvailableResource {
  ticketId: number; ticketNumber: string; employeeId: string; employeeName: string; grade: string; location: string;
  currentProjectName: string; employeeSkills: string; initiatingCluster: string; category: string; releaseDate: string;
  createdAt: string; evaluationDeadlineAt: string | null; evaluationId: number | null;
  myAllocationStatus: EvaluationStatus | null; canShowInterest: boolean;
}

export interface AllocationTicket {
  ticketId: number; evaluationId: number; ticketNumber: string; employeeId: string; employeeName: string; grade: string;
  location: string; currentProjectName: string; employeeSkills: string; clusterId: number; clusterCode: string;
  interestAt: string | null; status: EvaluationStatus; ticketStatus: TicketStatus; createdAt: string; evaluationDeadlineAt: string | null;
  rejectionReason: string | null; canUpdateStatus: boolean;
}

export interface TicketDetail {
  id: number; ticketNumber: string; status: TicketStatus; slaStatus: SlaStatus; slaExceptionApproved: boolean; employeeSource: 'MASTER' | 'MANUAL';
  employeeId: string; employeeName: string; grade: string; location: string; currentSupervisor: string;
  currentProjectId: string; currentProjectName: string; psl: string; band: string;
  releaseDate: string; releaseLocation: string; reasonForRelease: string; category: string; releaseCategoryId: number;
  recommendedForNewAssignment: boolean; employeeAware: boolean; employeeSkills: string; feedbackProvided: string;
  detailedFeedback: string; commitmentMade: string | null; factoredInForecast: boolean; backfillRequired: boolean; remarks: string | null;
  initiatingCluster: string; ownerCluster: string | null; ownerName: string | null; assignedToHead: boolean;
  createdByName: string; createdAt: string; updatedAt: string; evaluationDeadlineAt: string | null;
  closedAt: string | null; closedByName: string | null; closureReason: string | null; cancellationReason: string | null;
  reopenedFromTicketId: number | null; reopenedFromTicketNumber: string | null;
  evaluations: Evaluation[]; allowedActions: string[];
}

export interface HistoryItem { historyId: number; eventType: string; oldStatus: TicketStatus | null; newStatus: TicketStatus; changedByName: string; changedAt: string; reason: string | null; comments: string | null }
export interface AuditItem { auditId: number; entityType: string; entityId: string; action: string; oldValue: string | null; newValue: string | null; performedByName: string; performedAt: string; ipAddress: string | null }
export interface Attachment { attachmentId: number; kind: string; fileName: string; fileType: string; fileSize: number; uploadedAt: string; uploadedBy: number }
export interface Dashboard { totalOpen: number; totalWip: number; totalClosed: number; totalCancelled: number; totalReopened: number; slaAtRisk: number; slaBreached: number; awaitingClusterResponse: number; awaitingInterview: number; awaitingFinalDecision: number; allocatedCount: number; releasedCount: number }

export interface CreateTicketInput {
  employeeId: string; employeeName: string; clusterId: number | null; releaseCategoryId: number | null; releaseDate: string;
  reasonForRelease: string; recommendedForNewAssignment: boolean; employeeAware: boolean; employeeSkills: string; feedbackProvided: string;
  detailedFeedback: string; commitmentMade: string; factoredInForecast: boolean; grade: string; location: string; currentSupervisor: string;
  currentProjectId: string; currentProjectName: string; remarks: string; releaseLocation: string; psl: string; band: string; backfillRequired: boolean;
}

export const MIN_FEEDBACK_LENGTH = 100;   // UX only - the backend enforces it too
