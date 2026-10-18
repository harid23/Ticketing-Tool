import type { ReactNode } from 'react';
import type { Evaluation, EvaluationStatus, HistoryItem, SlaStatus, TicketStatus } from '../types';

// Dates arrive as UTC from the API; the browser shows them in the user's local time zone.
export const fmtDateTime = (iso?: string | null) => (iso ? new Date(iso.endsWith('Z') ? iso : iso + 'Z').toLocaleString() : '—');
export const fmtDate = (d?: string | null) => (d ? new Date(d + 'T00:00:00').toLocaleDateString() : '—');

const STATUS_STYLE: Record<TicketStatus, string> = { OPEN: '#365a70', WIP: '#725900', CLOSED: '#236547', CANCELLED: '#626a70', REOPENED: '#654b82' };

export function StatusBadge({ status }: { status: TicketStatus }) {
  return <span className="rr-badge" style={{ borderColor: STATUS_STYLE[status], color: STATUS_STYLE[status] }}>{status}</span>;
}
export function SlaBadge({ sla, deadline }: { sla: SlaStatus; deadline?: string | null }) {
  const breached = sla === 'BREACHED';
  return <span className="rr-badge" style={{ borderColor: breached ? '#a53131' : '#236547', color: breached ? '#8b2929' : '#236547' }}
    title={deadline ? `Deadline: ${fmtDateTime(deadline)}` : 'No SLA deadline'}>{breached ? 'SLA BREACHED' : 'Within SLA'}</span>;
}

export function Card({ title, children, actions }: { title: string; children: ReactNode; actions?: ReactNode }) {
  return <section className="rr-card"><header><h3>{title}</h3>{actions}</header>{children}</section>;
}
export function Field({ label, children }: { label: string; children: ReactNode }) {
  return <div className="rr-field"><dt>{label}</dt><dd>{children ?? '—'}</dd></div>;
}
export const Loading = () => <p role="status">Loading…</p>;
export const ErrorBox = ({ message }: { message: string }) => <p role="alert" className="rr-error">{message}</p>;
export const Empty = ({ text }: { text: string }) => <p className="rr-empty">{text}</p>;

export function Timeline({ items }: { items: HistoryItem[] }) {
  if (!items.length) return <Empty text="No history yet." />;
  return (
    <ol className="rr-timeline">
      {items.map(h => (
        <li key={h.historyId}>
          <strong>{h.eventType === 'StatusChange' ? `${h.oldStatus ?? 'Created'} → ${h.newStatus}` : h.reason}</strong>
          {h.eventType === 'StatusChange' && h.reason && <span> — {h.reason}</span>}
          {h.comments && <div className="rr-muted">“{h.comments}”</div>}
          <div className="rr-muted">{h.changedByName} · {fmtDateTime(h.changedAt)}</div>
        </li>
      ))}
    </ol>
  );
}

const ALLOCATION_STEPS = ['Interest', 'Interview', 'Results', 'Outcome'];

function allocationProgress(status: EvaluationStatus): { activeStep: number; terminal: 'selected' | 'rejected' | 'closed' | null; label: string } {
  switch (status) {
    case 'PENDING': return { activeStep: 0, terminal: null, label: 'Awaiting interest' };
    case 'NOT_INTERESTED': return { activeStep: 0, terminal: 'rejected', label: 'Not interested' };
    case 'INTERESTED':
    case 'ASSIGNED': return { activeStep: 1, terminal: null, label: 'Interest recorded' };
    case 'IN_PROGRESS':
    case 'INTERVIEW_SCHEDULED': return { activeStep: 2, terminal: null, label: 'Interview scheduled' };
    case 'WAITING_FOR_RESULTS': return { activeStep: 3, terminal: null, label: 'Waiting for results' };
    case 'SELECTED': return { activeStep: 4, terminal: 'selected', label: 'Selected' };
    case 'NOT_SELECTED':
    case 'REJECTED': return { activeStep: 3, terminal: 'rejected', label: 'Rejected' };
    case 'CLOSED': return { activeStep: 3, terminal: 'closed', label: 'Closed by another cluster' };
  }
}

export function AllocationFlow({ evaluations, renderActions, emptyText = 'No clusters have expressed interest in this request yet.' }: {
  evaluations: Evaluation[]; renderActions?: (evaluation: Evaluation) => ReactNode; emptyText?: string
}) {
  if (!evaluations.length) return <Empty text={emptyText} />;
  return <div className="rr-allocation-flows">
    {evaluations.map(evaluation => {
      const progress = allocationProgress(evaluation.status);
      return <section className="rr-allocation-flow" key={evaluation.evaluationId} aria-label={`${evaluation.clusterCode} allocation progress`}>
        <header className="rr-allocation-flow-header">
          <div><strong>{evaluation.clusterCode}</strong><span>{evaluation.clusterName}</span></div>
          <span className={`rr-flow-status rr-flow-status-${progress.terminal ?? 'active'}`}>{progress.label}</span>
        </header>
        <ol className="rr-flow-steps">
          {ALLOCATION_STEPS.map((step, index) => {
            const state = progress.terminal === 'selected' || index < progress.activeStep
              ? 'complete'
              : index === progress.activeStep
                ? progress.terminal ?? 'current'
                : 'upcoming';
            return <li className={`rr-flow-step rr-flow-step-${state}`} key={step}>
              <span className="rr-flow-marker" aria-hidden="true">{state === 'complete' ? '✓' : index + 1}</span>
              <span className="rr-flow-label">{step}</span>
            </li>;
          })}
        </ol>
        <p className="rr-allocation-flow-detail">
          {evaluation.responseAt && <>Interest response: {fmtDateTime(evaluation.responseAt)} · </>}
          {evaluation.interviewDate && <>Interview: {fmtDate(evaluation.interviewDate)} · </>}
          {evaluation.rejectionReason && <>Reason: {evaluation.rejectionReason}</>}
          {!evaluation.responseAt && !evaluation.interviewDate && !evaluation.rejectionReason && 'Progress updates appear here as the cluster works the allocation.'}
        </p>
        {renderActions && <div className="rr-allocation-flow-actions">{renderActions(evaluation)}</div>}
      </section>;
    })}
  </div>;
}
